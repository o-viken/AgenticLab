#!/usr/bin/env node

import process from "node:process";
import { createRequire } from "node:module";

const { chromium } = createRequire(import.meta.url)("playwright");

const baseUrl = process.env.THESERIES_URL ?? "http://127.0.0.1:5140";
const users = positiveInt("THESERIES_USERS", 10);
const rounds = positiveInt("THESERIES_ROUNDS", 3);
const warmupMs = positiveInt("THESERIES_WARMUP_MS", 1000);
const messagePrefix = process.env.THESERIES_MESSAGE ?? "Load test message";

const browser = await chromium.launch({ headless: true });
const contexts = [];
const results = [];

try {
    for (let user = 0; user < users; user++) {
        const context = await browser.newContext();
        contexts.push(context);
        const page = await context.newPage();
        page.on("pageerror", error => results.push({ user: user + 1, error: error.message }));
        await page.goto(baseUrl, { waitUntil: "networkidle" });
        await page.waitForFunction(() => document.querySelector(".chat-log")?._tsStickInit === true);
        await page.locator("#agent option").first().waitFor({ state: "attached" });
    }

    await delay(warmupMs);
    const started = Date.now();
    const runs = contexts.map(async (context, user) => {
        const samples = [];
        for (let round = 0; round < rounds; round++) {
            const sample = await runRound(context.pages()[0], user + 1, round + 1);
            samples.push(sample);
            if (sample.error) break;
        }
        return samples;
    });
    const completed = await Promise.all(runs);
    const elapsedMs = Date.now() - started;
    const samples = completed.flat();
    const firstExchange = samples.map(sample => sample.firstExchangeMs);
    const completion = samples.map(sample => sample.completedMs);
    results.push(...samples.filter(sample => sample.error));

    console.log(JSON.stringify({
        url: baseUrl,
        users,
        rounds,
        samples: samples.length,
        successfulSamples: samples.filter(sample => !sample.error).length,
        elapsedMs,
        firstExchangeMs: summary(firstExchange),
        completionMs: summary(completion),
        browserJsHeapBytes: await browserHeap(contexts),
        errors: results,
    }, null, 2));

    if (results.length > 0 || samples.length !== users * rounds) {
        process.exitCode = 1;
    }
} catch (error) {
    console.error(JSON.stringify({ url: baseUrl, error: error.message }, null, 2));
    process.exitCode = 1;
} finally {
    await Promise.all(contexts.map(context => context.close()));
    await browser.close();
}

async function runRound(page, user, round) {
    const message = `${messagePrefix} ${user}/${round}`;
    const start = performance.now();
    try {
        const input = page.locator("#message");
        await input.fill(message);
        await page.getByRole("button", { name: "Send", exact: true }).click();
        await page.waitForFunction(expected =>
            document.querySelector(".turn.pending .entry.user p")?.textContent === expected, message);
        const firstExchangeMs = Math.round(performance.now() - start);
        await page.waitForFunction(() => {
            const input = document.querySelector("#message");
            return input && !input.disabled;
        });
        const failure = page.locator(".turn.pending .entry.agent .error");
        if (await failure.count()) {
            throw new Error(await failure.innerText());
        }
        return { user, round, firstExchangeMs, completedMs: Math.round(performance.now() - start) };
    } catch (error) {
        return { user, round, error: error.message };
    }
}

async function browserHeap(contexts) {
    let total = 0;
    let observed = false;
    for (const context of contexts) {
        for (const page of context.pages()) {
            const heap = await page.evaluate(() => performance.memory?.usedJSHeapSize ?? null);
            if (typeof heap === "number") {
                total += heap;
                observed = true;
            }
        }
    }
    return observed ? total : null;
}

function summary(values) {
    const ordered = values.filter(value => Number.isFinite(value)).sort((a, b) => a - b);
    if (ordered.length === 0) return null;
    return {
        min: ordered[0],
        p50: percentile(ordered, 0.5),
        p95: percentile(ordered, 0.95),
        max: ordered.at(-1),
    };
}

function percentile(values, fraction) {
    return values[Math.min(values.length - 1, Math.ceil(values.length * fraction) - 1)];
}

function positiveInt(name, fallback) {
    const value = Number.parseInt(process.env[name] ?? `${fallback}`, 10);
    if (!Number.isInteger(value) || value < 1) {
        throw new Error(`${name} must be a positive integer.`);
    }
    return value;
}

function delay(milliseconds) {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}