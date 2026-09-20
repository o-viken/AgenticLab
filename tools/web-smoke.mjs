import assert from "node:assert/strict";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";

const { chromium } = createRequire(import.meta.url)("playwright");
const baseUrl = process.env.THESERIES_URL ?? "http://127.0.0.1:5186";
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
    await page.getByRole("button", { name: "System prompt", exact: true }).click();
    await page.locator(".details-dock").waitFor({ state: "visible" });
    await page.locator(".learn-dock").waitFor({ state: "visible" });
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