import assert from "node:assert/strict";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";

const { chromium } = createRequire(import.meta.url)("playwright");
const baseUrl = process.env.THESERIES_URL ?? "http://127.0.0.1:5186";
const expectedHosts = (process.env.AGENTICLAB_HOSTS ?? process.env.THESERIES_HOSTS)?.split(",").map(key => key.trim());
const legacyHosts = JSON.parse(process.env.THESERIES_HOST_ALIASES ?? "{}");
const screenshots = process.env.THESERIES_SCREENSHOTS ?? path.join(tmpdir(), "agentic-lab-web-smoke");
mkdirSync(screenshots, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
let currentPage;

try {
    for (const viewport of [{ width: 1440, height: 1000 }, { width: 1024, height: 900 }, { width: 390, height: 844 }, { width: 1920, height: 1080 }]) {
        const context = await browser.newContext({ viewport });
        const page = currentPage = await context.newPage();
        page.setDefaultTimeout(15000);
        page.on("pageerror", error => errors.push(error.message));
        await openFlow(page);
        await checkHosts(page, viewport.width);
        await checkConversationHeader(page);
        await checkExecutionControls(page);
        await capture(page, `flow-${viewport.width}`);
        const layout = await page.evaluate(() => {
            const conversation = document.querySelector(".side-left").getBoundingClientRect();
            const flow = document.querySelector(".flow-main-col").getBoundingClientRect();
            return { conversationX: conversation.x, conversationY: conversation.y, flowX: flow.x, flowY: flow.y };
        });
        if (viewport.width >= 900) assert.ok(layout.flowX > layout.conversationX, "Desktop workspace must have two panes");
        else assert.ok(layout.flowY > layout.conversationY, "Narrow workspace must stack conversation before flow");

        await page.locator("#message").fill("Unsent layout test");
        await page.getByRole("tab", { name: /^Settings/ }).click();
        await page.locator("#settings-panel").waitFor({ state: "visible" });
        await page.getByRole("tab", { name: "Conversation", exact: true }).click();
        await page.locator("#message").waitFor({ state: "visible" });
        assert.equal(await page.locator("#message").inputValue(), "Unsent layout test");

        if (viewport.width === 1440) {
            const splitter = page.getByRole("separator", { name: "Resize the Conversation panel" });
            await splitter.focus();
            await page.keyboard.press("ArrowLeft");
            await page.waitForFunction(() => localStorage.getItem("theseries-panels")?.endsWith("|0"));
            const keyboardWidth = await page.evaluate(() => Number(localStorage.getItem("theseries-panels").split("|")[3]));
            const handle = await splitter.boundingBox();
            await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
            await page.mouse.down();
            await page.mouse.move(handle.x + handle.width / 2 - 30, handle.y + handle.height / 2, { steps: 5 });
            await page.mouse.up();
            await page.waitForFunction(previous => Number(localStorage.getItem("theseries-panels")?.split("|")[3]) < previous, keyboardWidth);
            const stored = await page.evaluate(() => localStorage.getItem("theseries-panels"));
            assert.equal(stored.split("|").length, 7);
            await openFlow(page);
            assert.equal(await page.evaluate(() => localStorage.getItem("theseries-panels")), stored);
            await page.getByRole("tab", { name: /^Settings/ }).click();
            await page.getByRole("button", { name: "Reset layout", exact: true }).click();
            await page.waitForFunction(() => localStorage.getItem("theseries-panels")?.endsWith("|1"));
            await page.evaluate(() => localStorage.setItem("theseries-panels", "0|0|0|480|300|360"));
            await openFlow(page);
            const restored = await page.locator(".side-left").boundingBox();
            assert.ok(Math.abs(restored.width - 480) < 2, "Legacy conversation width must be restored");
            await page.evaluate(() => document.documentElement.style.zoom = "2");
            await capture(page, "flow-200-percent");
            await page.evaluate(() => document.documentElement.style.zoom = "");
            await page.evaluate(() => localStorage.setItem("theseries-panels", "0|0|0|240|300|360"));
            await openFlow(page);
            await checkConversationHeader(page);
        }
        await checkDocksAndDiscovery(page, viewport.width);
        await checkA2ADetails(page, viewport.width);
        await checkLearning(page, viewport.width);
        await page.goto(new URL("/discovery", baseUrl).href, { waitUntil: "networkidle" });
        await capture(page, `discovery-${viewport.width}`);
        await page.goto(new URL("/design-system", baseUrl).href, { waitUntil: "networkidle" });
        await page.getByRole("button", { name: "New conversation", exact: true }).click();
        await page.waitForFunction(() => document.querySelector("section > output")?.textContent === "1 activations");
        await page.getByRole("group", { name: "Execution mode" }).getByRole("button", { name: "Manual", exact: true }).click();
        await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:last-child')?.getAttribute("aria-pressed") === "true");
        await capture(page, `design-system-${viewport.width}`);
        await page.emulateMedia({ reducedMotion: "reduce" });
        assert.equal(await page.locator(".spinner").evaluate(element => getComputedStyle(element).animationName), "none");
        await page.emulateMedia({ reducedMotion: "no-preference" });
        const missing = await page.goto(new URL("/missing-design-test", baseUrl).href, { waitUntil: "networkidle" });
        assert.equal(missing.status(), 404);
        await page.getByRole("heading", { name: "Page not found", exact: true }).waitFor();
        await capture(page, `not-found-${viewport.width}`);
        await context.close();
        currentPage = null;
        console.log(`Flow, docks, Discovery, Learn and design system ${viewport.width}x${viewport.height}: passed`);
    }
    assert.deepEqual(errors, [], "No unhandled browser errors");
    console.log(`Screenshots: ${screenshots}`);
} catch (error) {
    if (currentPage && !currentPage.isClosed()) await currentPage.screenshot({ path: path.join(screenshots, "failure.png"), fullPage: true });
    console.error(error);
    process.exitCode = 1;
} finally {
    await browser.close();
}

async function checkHosts(page, width) {
    const hosts = await page.locator("#host option").evaluateAll(options => options.map(option => ({
        key: option.value, label: option.label,
    })));
    const keys = hosts.map(host => host.key);
    assert.equal(keys[0], "default", "Default is always first");
    assert.equal(await page.locator("#host").inputValue(), "default", "Fresh contexts start with Default");
    if (expectedHosts) assert.deepEqual(keys, expectedHosts, "Only configured hosts are available");

    async function restore(stored, expected) {
        await page.evaluate(key => localStorage.setItem("theseries-vendor", key), stored);
        await openFlow(page);
        assert.equal(await page.locator("#host").inputValue(), expected, `Saved host ${stored}`);
        assert.ok(await page.locator("#agent option").count(), "Every available host offers an available agent");
    }

    for (const host of hosts) {
        await restore(host.key.toUpperCase(), host.key);
        const icon = page.locator(".selected-vendor .brand-icon");
        if (host.key === "default") assert.equal(await icon.count(), 0, "Default uses neutral branding");
        if (await icon.count()) {
            const mask = await icon.evaluate(element => getComputedStyle(element).maskImage);
            const matched = mask.match(/url\(["']?([^"')]+)["']?\)/);
            assert.ok(matched, `Host ${host.key} has a CSS mask`);
            const asset = new URL(matched[1], page.url());
            assert.equal(asset.origin, new URL(baseUrl).origin, "Brand assets are local");
            assert.match(asset.pathname, /\/_content\/[^/]+\/host\.svg$/);
            const response = await page.request.get(asset.href);
            assert.ok(response.ok(), `Host ${host.key} logo loads`);
            assert.match(response.headers()["content-type"], /image\/svg\+xml/);
            const painted = await page.evaluate(async url => {
                const image = new Image();
                image.src = url;
                await image.decode();
                const canvas = document.createElement("canvas");
                canvas.width = canvas.height = 24;
                const context = canvas.getContext("2d");
                context.drawImage(image, 0, 0, 24, 24);
                return context.getImageData(0, 0, 24, 24).data.some((value, index) => index % 4 === 3 && value > 0);
            }, asset.href);
            assert.ok(painted, `Host ${host.key} logo is nonblank`);
            const visible = page.locator(".brand-icon:visible").first();
            const bounds = await visible.boundingBox();
            assert.ok(bounds && bounds.width >= 20 && bounds.height >= 20, "Visible host icon has stable dimensions");
            await capture(page, `host-${host.key}-${width}`);
        }
    }
    if (width === 1440) {
        for (const [stored, expected] of Object.entries(legacyHosts)) await restore(stored, expected);
    }
    await restore("unavailable-smoke-host", "default");
    await page.evaluate(() => localStorage.removeItem("theseries-vendor"));
}

async function checkExecutionControls(page) {
    const controls = page.getByRole("group", { name: "Execution controls", exact: true });
    const buttons = controls.getByRole("button");
    assert.deepEqual(await buttons.evaluateAll(elements => elements.map(element => element.getAttribute("aria-label"))), ["Pause", "Next", "Stop"]);
    assert.ok(await buttons.evaluateAll(elements => elements.every(element => element.disabled && element.classList.contains("icon-only"))));
    const initial = await buttons.evaluateAll(elements => elements.map(element => {
        const bounds = element.getBoundingClientRect();
        const toolbar = element.closest(".run-controls").getBoundingClientRect();
        return { x: bounds.x - toolbar.x, y: bounds.y - toolbar.y, width: bounds.width, height: bounds.height };
    }));
    const modes = page.getByRole("group", { name: "Execution mode", exact: true });
    await modes.getByRole("button", { name: "Manual", exact: true }).click();
    await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:last-child')?.getAttribute("aria-pressed") === "true");
    const manual = await buttons.evaluateAll(elements => elements.map(element => {
        const bounds = element.getBoundingClientRect();
        const toolbar = element.closest(".run-controls").getBoundingClientRect();
        return { x: bounds.x - toolbar.x, y: bounds.y - toolbar.y, width: bounds.width, height: bounds.height };
    }));
    assert.deepEqual(manual, initial, "Execution action slots must not move when mode changes");
    assert.ok(await buttons.evaluateAll(elements => elements.every(element => element.disabled)));
    await modes.getByRole("button", { name: "Auto", exact: true }).click();
    await page.waitForFunction(() => document.querySelector('[aria-label="Execution mode"] button:first-child')?.getAttribute("aria-pressed") === "true");
}

async function checkConversationHeader(page) {
    const header = page.locator(".conversation-head");
    const newConversation = header.getByRole("button", { name: "New conversation", exact: true });
    assert.equal(await newConversation.count(), 1);
    assert.equal(await newConversation.getAttribute("title"), "New conversation");
    assert.ok(await newConversation.evaluate(button => button.classList.contains("icon-only")));
    assert.equal(await page.locator(".chat-composer").getByRole("button", { name: "New conversation", exact: true }).count(), 0);
    const fits = await header.evaluate(element => {
        const boundary = element.getBoundingClientRect();
        const buttons = [...element.querySelectorAll("button")];
        const bounds = buttons.map(button => button.getBoundingClientRect());
        return buttons.every((button, index) => button.scrollWidth <= button.clientWidth + 1
            && bounds[index].left >= boundary.left && bounds[index].right <= boundary.right
            && (index === 0 || bounds[index].left >= bounds[index - 1].right - 1));
    });
    assert.ok(fits, "Conversation tabs and header actions fit without overlapping");
}

async function checkDocksAndDiscovery(page, width) {
    await openFlow(page);
    const hostSubtitle = page.locator(".node.harness > .subtitle").first();
    assert.equal(await hostSubtitle.innerText(), "Agent service");
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Technical labels", exact: true }).check();
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelector(".node.harness > .subtitle")?.textContent.includes("AiService"));
    assert.doesNotMatch(await hostSubtitle.innerText(), /Client/, "The compact host describes the service, not the client");
    await page.locator("#message").fill("Preserved while inspecting");
    await page.getByRole("button", { name: "Expand the Execution panel", exact: true }).click();
    await page.locator(".main-panel-body.bottom-max").waitFor();
    await page.getByRole("button", { name: "Restore the Execution panel", exact: true }).click();
    await page.locator(".main-panel-body.bottom-max").waitFor({ state: "hidden" });
    await page.getByRole("button", { name: "Collapse the Execution panel", exact: true }).click();
    await page.locator(".side-bottom.collapsed").waitFor();
    await page.getByRole("button", { name: "Expand the Execution panel", exact: true }).click();
    await page.locator(".side-bottom.collapsed").waitFor({ state: "hidden" });
    await page.getByRole("checkbox", { name: "Learn", exact: true }).check();
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).check();
    await page.keyboard.press("Escape");
    const anatomy = page.locator(".node.harness.anatomy");
    await anatomy.waitFor();
    assert.equal(await anatomy.getByRole("button", { name: "Client", exact: true }).count(), 0, "Client is not a host inspector section");
    assert.equal(await anatomy.getByTitle("Learn about the client", { exact: true }).count(), 0, "Client has no anatomy info button");
    await page.getByRole("button", { name: "System prompt", exact: true }).click();
    await page.locator(".details-dock").waitFor({ state: "visible" });
    const learn = page.locator(".learn-dock");
    await learn.waitFor({ state: "visible" });
    await learn.getByRole("button", { name: /^Client\b/ }).click();
    await learn.getByRole("heading", { name: "Client", exact: true }).waitFor();
    assert.match(await learn.locator(".concept-body").innerText(), /outside the agent host/);
    assert.equal(await page.locator("#host-detail-heading").innerText(), "System prompt", "Opening Client in Learn preserves Details");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
    const regions = await page.evaluate(() => [".primary-workspace", ".details-dock", ".learn-dock"].map(selector => {
        const bounds = document.querySelector(selector).getBoundingClientRect();
        return { left: bounds.left, right: bounds.right, top: bounds.top, bottom: bounds.bottom, width: bounds.width };
    }));
    if (width >= 1200) {
        assert.ok(regions[0].right <= regions[1].left + 1 && regions[1].right <= regions[2].left + 1, "Docks must not overlap");
        assert.ok(regions.every(region => region.width >= 240), "All expanded regions stay readable");
    } else {
        assert.ok(regions[0].bottom <= regions[1].top + 1 && regions[1].bottom <= regions[2].top + 1, "Narrow docks stack independently");
    }
    await capture(page, `flow-docks-${width}`);
    await page.getByRole("button", { name: "Close details", exact: true }).click();
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await page.locator(".learn-dock").isVisible(), "Closing Details must not close Learn");
    const trigger = page.getByRole("button", { name: "Discovery", exact: true });
    const dialog = page.getByRole("dialog", { name: "Discovery", exact: true });
    await trigger.click();
    await dialog.waitFor();
    await page.keyboard.press("Tab");
    assert.ok(await dialog.evaluate(element => element.contains(document.activeElement)), "Dialog contains keyboard focus");
    assert.notEqual(await dialog.evaluate(element => getComputedStyle(element, "::backdrop").backgroundColor), "rgba(0, 0, 0, 0)");
    await capture(page, `discovery-overlay-${width}`);
    await page.keyboard.press("Escape");
    await dialog.waitFor({ state: "hidden" });
    assert.equal(await page.evaluate(() => document.activeElement?.textContent.trim()), "Discovery");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
    await trigger.click();
    await dialog.waitFor();
    await page.mouse.click(2, 2);
    await dialog.waitFor({ state: "hidden" });
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting");
}

async function checkA2ADetails(page, width) {
    await openFlow(page);
    await page.getByRole("combobox", { name: "Host", exact: true }).selectOption({ label: "Default" });
    await page.waitForFunction(() => [...document.querySelector("#agent").options].some(option => option.label.toLowerCase() === "orchestrator"));
    const selectedAgent = await page.locator("#agent").evaluate(element => [...element.options].find(option => option.label.toLowerCase() === "orchestrator").value);
    await page.locator("#agent").selectOption(selectedAgent);
    await page.getByRole("button", { name: "Technical", exact: true }).click();
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).uncheck();
    await page.getByRole("checkbox", { name: "A2A agents", exact: true }).check();
    await page.keyboard.press("Escape");
    const chips = page.locator(".a2a.node-inner .skill-chip .host-section");
    await page.locator(".a2a.node-inner .skill-chip").first().waitFor();
    await page.locator(".a2a-flow .a2a-agent-title h4").first().waitFor();
    assert.equal(await page.locator(".host-section").count(), 0, "Collapsed host shows plain labels, including A2A agents");
    assert.equal(await page.locator(".inspect-icon").count(), 0, "Collapsed host hides all inspect icons");
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).check();
    await page.keyboard.press("Escape");
    await chips.first().waitFor();
    const names = (await chips.allTextContents()).map(name => name.trim());
    const firstAgent = names[0];
    const learnVisible = await page.getByRole("checkbox", { name: "Learn", exact: true }).isChecked();
    await page.locator("#message").fill("Preserved while inspecting A2A");
    await chips.first().focus();
    await page.keyboard.press("Enter");
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");
    assert.match(await page.locator("#details-content").innerText(), /Agent2Agent \(A2A\)/);
    assert.match(await page.locator("#details-content").innerText(), /No request captured at this position/);
    assert.match(await page.locator("#details-content").innerText(), /model settings and tools are not exposed/);
    await capture(page, `a2a-details-${width}`);

    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).uncheck();
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelectorAll(".host-section").length === 0);
    assert.equal(await page.locator(".inspect-icon").count(), 0);
    assert.ok(await page.locator(".details-dock").isVisible(), "Collapsing the host keeps A2A Details open");
    assert.equal(await page.locator("#host-detail-heading").textContent(), firstAgent);
    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).check();
    await page.keyboard.press("Escape");
    await chips.first().waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");

    await page.getByRole("button", { name: "Collapse the Details panel", exact: true }).click();
    await page.locator(".details-dock .collapsed").waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "false");
    await chips.first().click();
    await page.locator("#host-detail-heading").waitFor();
    assert.equal(await chips.first().getAttribute("aria-expanded"), "true");
    if (names.length > 1) {
        await chips.nth(1).click();
        await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name, names[1]);
        assert.equal(await chips.first().getAttribute("aria-expanded"), "false");
        assert.equal(await chips.nth(1).getAttribute("aria-expanded"), "true");
    }

    const remoteHeading = page.locator(".a2a-flow .a2a-agent-title").getByRole("button", { name: firstAgent, exact: true });
    await remoteHeading.click();
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    const ids = await page.locator('[id^="host-section-a2a-"]').evaluateAll(elements => elements.map(element => element.id));
    assert.equal(new Set(ids).size, ids.length, "Remote-agent inspector triggers have unique IDs");
    await page.keyboard.press("Escape");
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await remoteHeading.evaluate(element => document.activeElement === element), "Closing A2A Details restores the remote heading's focus");

    await page.getByRole("button", { name: "View options", exact: true }).click();
    await page.getByRole("checkbox", { name: "Expand agent host", exact: true }).check();
    await page.keyboard.press("Escape");
    const catalogueHeading = page.locator(".a2a.node-inner").getByRole("button", { name: "A2A agents", exact: true });
    await catalogueHeading.click();
    await page.locator("#details-content").getByRole("button", { name: firstAgent, exact: true }).click();
    await page.waitForFunction(name => document.querySelector("#host-detail-heading")?.textContent === name
        && document.activeElement?.id === "host-detail-heading", firstAgent);
    await page.keyboard.press("Escape");
    await page.locator(".details-dock").waitFor({ state: "hidden" });
    assert.ok(await catalogueHeading.evaluate(element => document.activeElement === element), "Drilling into an A2A catalogue entry retains the external focus origin");
    assert.equal(await page.locator("#message").inputValue(), "Preserved while inspecting A2A");
    assert.equal(await page.locator("#agent").inputValue(), selectedAgent);
    assert.equal(await page.getByRole("checkbox", { name: "Learn", exact: true }).isChecked(), learnVisible);
}

async function checkLearning(page, width) {
    await page.goto(new URL("/learn", baseUrl).href, { waitUntil: "networkidle" });
    assert.equal(await page.getByRole("button", { name: "Discovery", exact: true }).count(), 0);
    await page.getByRole("button", { name: "Next step", exact: true }).click();
    await page.waitForFunction(() => document.querySelector(".intro-count")?.textContent.trim().startsWith("2"));
    await capture(page, `learn-${width}`);
    const stages = ["model-to-agent", "anatomy-of-agent", "where-to-run", "agent-loop", "wider-ecosystem"];
    for (const stage of stages) {
        await page.goto(new URL(`/learn?stage=${stage}`, baseUrl).href, { waitUntil: "networkidle" });
        const complete = page.getByRole("button", { name: "Show complete diagram", exact: true });
        if (await complete.count()) {
            await complete.click();
            await page.waitForFunction(() => Number(document.querySelector("[data-beat]")?.dataset.beat) > 0);
        }
        await capture(page, `learn-${stage}-${width}`);
    }
}

async function openFlow(page) {
    await page.goto(new URL("/", baseUrl).href, { waitUntil: "networkidle" });
    await page.waitForFunction(() => document.querySelector(".chat-log")?._tsStickInit === true);
    await page.locator("#agent option").first().waitFor({ state: "attached" });
}

async function capture(page, name) {
    await page.evaluate(() => document.fonts.ready);
    const metrics = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth > innerWidth + 1,
        fonts: [...document.fonts].filter(font => font.status === "loaded").map(font => font.family),
        font: getComputedStyle(document.querySelector("h1")).fontFamily,
        overflowingBars: [...document.querySelectorAll(".app-header, .selection-bar, .conversation-head, .main-panel-head, .run-controls, .discovery-toolbar, .stage-heading")]
            .filter(element => element.getClientRects().length && element.scrollWidth > element.clientWidth + 1)
            .map(element => element.className)
    }));
    assert.equal(metrics.overflow, false, `${name}: no page-level horizontal overflow`);
    assert.deepEqual(metrics.overflowingBars, [], `${name}: all toolbar content fits`);
    assert.match(metrics.font, /IBM Plex Sans/);
    assert.ok(metrics.fonts.some(font => font.includes("IBM Plex Sans")), `${name}: local UI font is loaded`);
    await page.screenshot({ path: path.join(screenshots, `${name}.png`), fullPage: true });
}