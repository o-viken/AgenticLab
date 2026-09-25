import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import path from 'node:path';

const { chromium } = createRequire(import.meta.url)('playwright');
const baseUrl = process.env.AGENTICLAB_URL ?? 'http://localhost:5140';
const screenshots = path.join(tmpdir(), 'agentic-lab-windfarm-smoke');
mkdirSync(screenshots, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
let activePage;

try {
    for (const width of [1440, 1024, 390, 1920]) {
        const context = await browser.newContext({ viewport: { width, height: width === 390 ? 844 : 1000 } });
        const page = activePage = await context.newPage();
        page.setDefaultTimeout(15000);
        page.on('pageerror', error => errors.push(error.message));
        await page.addInitScript(() => {
            const setItem = Storage.prototype.setItem;
            window.panelLayoutWrites = [];
            document.addEventListener('click', event => {
                if (event.target.closest('.panel-collapse, .panel-rail, .collapse-conversation'))
                    window.panelClickStarted = performance.now();
            }, true);
            Storage.prototype.setItem = function (key, value) {
                if (key === 'agenticlab-panels') {
                    window.panelLayoutWrites.push({
                        value,
                        collapsed: ['.side-left', '.learn-dock .side-right', '.side-bottom']
                            .map(selector => document.querySelector(selector)?.classList.contains('collapsed') ?? false),
                    });
                }
                return setItem.call(this, key, value);
            };
        });
        await page.goto(baseUrl, { waitUntil: 'networkidle' });
        await page.waitForFunction(() => document.querySelector('.chat-log')?._tsStickInit === true);
        await page.getByRole('combobox', { name: 'Host', exact: true }).selectOption('windfarm');
        const panel = page.getByRole('region', { name: 'Windfarm maintenance case' });
        await panel.getByRole('button', { name: 'Start case', exact: true }).waitFor();
        assert.ok(await panel.locator('img').evaluate(image => image.complete && image.naturalWidth > 0));
        await panel.getByRole('button', { name: 'Start case', exact: true }).click();
        await panel.getByRole('button', { name: 'Reset case', exact: true }).waitFor();
        await panel.getByRole('status').filter({ hasText: 'Investigating' }).waitFor();
        const firstId = await panel.locator('.case-id').getAttribute('title');
        assert.match(await panel.locator('.case-id').innerText(), /r0$/);
        assert.match(await page.locator('#message').inputValue(), /WT-07/);
        assert.equal(await panel.getByRole('button', { name: 'Approve plan', exact: true }).count(), 0);

        const draft = await page.locator('#message').inputValue();
        const mountedPanel = await panel.elementHandle();
        await panel.getByRole('combobox', { name: 'Scenario' }).selectOption('replanning');
        await page.getByRole('checkbox', { name: 'Learn', exact: true }).check();
        await page.getByRole('button', { name: 'Technical', exact: true }).click();
        await page.getByRole('button', { name: 'View options', exact: true }).click();
        await page.getByRole('checkbox', { name: 'Expand agent host', exact: true }).check();
        await page.getByRole('checkbox', { name: 'Prompt signature', exact: true }).check();
        await page.keyboard.press('Escape');
        for (const title of ['Learn', 'Conversation', 'Execution']) {
            for (const command of ['Collapse', 'Expand']) {
                await page.evaluate(() => { window.panelLayoutWrites = []; });
                await page.getByRole('button', { name: `${command} the ${title} panel`, exact: true }).click();
                await page.waitForFunction(() => window.panelLayoutWrites.length > 0);
                const write = await page.evaluate(() => window.panelLayoutWrites.at(-1));
                assert.deepEqual(write.collapsed, write.value.split('|').slice(0, 3).map(flag => flag === '1'),
                    `${title} must render its new layout before saving preferences`);
                const latency = await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => {
                    document.querySelector('.flow-body').getBoundingClientRect();
                    requestAnimationFrame(() => resolve(performance.now() - window.panelClickStarted));
                })));
                assert.ok(latency < 1000, `${command} ${title} must not stall on layout (${Math.round(latency)} ms)`);
            }
        }
        await panel.locator('.case-id').waitFor();
        assert.ok(await mountedPanel.evaluate(element => element.isConnected), 'Collapse must not recreate the case panel');
        await mountedPanel.dispose();
        assert.equal(await panel.getByRole('combobox', { name: 'Scenario' }).inputValue(), 'replanning');
        assert.equal(await panel.locator('.case-id').getAttribute('title'), firstId, 'Collapse must retain the active case');
        assert.equal(await page.locator('#message').inputValue(), draft, 'Collapse must retain the draft');
        await page.getByRole('checkbox', { name: 'Learn', exact: true }).uncheck();

        await page.getByRole('tab', { name: /^Settings/ }).click();
        await page.getByRole('checkbox', { name: 'WindfarmTelemetry', exact: true }).waitFor();
        assert.match(await page.getByRole('checkbox', { name: 'WindfarmDraftPlan', exact: true }).locator('..').getAttribute('title'), /Medium risk/);
        await page.getByRole('tab', { name: 'Conversation', exact: true }).click();
        await panel.getByRole('combobox', { name: 'Scenario' }).selectOption('replanning');
        await panel.getByRole('button', { name: 'Reset case', exact: true }).click();
        await page.waitForFunction(previous => {
            const value = document.querySelector('.windfarm-panel .case-id')?.getAttribute('title');
            return value && value !== previous;
        }, firstId);
        const secondId = await panel.locator('.case-id').getAttribute('title');
        await panel.getByRole('combobox', { name: 'Scenario' }).selectOption('missing-evidence');
        await panel.getByRole('button', { name: 'Reset case', exact: true }).click();
        await panel.getByRole('status').filter({ hasText: 'Evidence hold' }).waitFor();
        assert.notEqual(await panel.locator('.case-id').getAttribute('title'), secondId);
        const fits = await page.evaluate(() => {
            const panel = document.querySelector('.windfarm-panel');
            const composer = document.querySelector('.chat-composer');
            const boundary = panel.getBoundingClientRect();
            const composeBounds = composer.getBoundingClientRect();
            return {
                overflow: document.documentElement.scrollWidth > window.innerWidth + 1,
                panelOverflow: panel.scrollWidth > panel.clientWidth + 1,
                composerVisible: composeBounds.height >= 80,
                separated: boundary.bottom <= composeBounds.top + 1,
                hasStyles: getComputedStyle(panel).overflowY === 'auto',
                buttonsFit: [...panel.querySelectorAll('button')].every(button => button.scrollWidth <= button.clientWidth + 1),
            };
        });
        assert.deepEqual(fits, { overflow: false, panelOverflow: false, composerVisible: true, separated: true, hasStyles: true, buttonsFit: true });
        await page.screenshot({ path: path.join(screenshots, `windfarm-${width}.png`), fullPage: true });
        await page.getByRole('button', { name: 'New conversation', exact: true }).click();
        await panel.getByRole('button', { name: 'Start case', exact: true }).waitFor();
        assert.equal(await panel.locator('.case-id').count(), 0);
        await context.close();
        activePage = null;
        console.log(`Windfarm ${width}px: scenario creation/reset, panel collapse, evidence hold, controls and assets passed`);
    }
    assert.deepEqual(errors, [], 'No unhandled browser errors');
    console.log(`Screenshots: ${screenshots}`);
} catch (error) {
    if (activePage && !activePage.isClosed()) await activePage.screenshot({ path: path.join(screenshots, 'failure.png'), fullPage: true });
    console.error(error);
    process.exitCode = 1;
} finally {
    await browser.close();
}