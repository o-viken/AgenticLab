const assert = require('node:assert/strict');
const readline = require('node:readline');
const { Readable } = require('node:stream');
const base = 'http://127.0.0.1:5891';
const post = (path, body, signal) => fetch(base + path, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal,
});
async function* events(response) {
    for await (const line of readline.createInterface({ input: Readable.fromWeb(response.body) })) {
        if (line.startsWith('data: ')) yield JSON.parse(line.slice(6));
    }
}
async function heldRun(sessionId) {
    const abort = new AbortController();
    const response = await post('/chat/stream', {
        message: 'API verification', agent: 'ChatAgent', sessionId, conversationId: sessionId,
        breakpoints: ['before-model'],
    }, abort.signal);
    assert.equal(response.status, 200);
    const stream = events(response);
    for await (const event of { [Symbol.asyncIterator]: () => ({ next: () => stream.next() }) }) {
        if (event.kind === 'breakpoint') return { stream, notice: JSON.parse(event.data), abort };
    }
    throw new Error('Missing pause');
}
(async () => {
    const invalid = await post('/chat/stream', { message: 'test', sessionId: 'invalid', conversationId: 'invalid', breakpoints: ['unknown'] });
    assert.equal(invalid.status, 400);
    const baseline = (await (await fetch('http://127.0.0.1:5893/stats')).json()).requests;
    const first = await heldRun('api-first');
    const second = await heldRun('api-second');
    assert.equal((await post('/chat/control', { sessionId: 'api-first', action: 'resume', breakpointId: second.notice.Id })).status, 409);
    assert.equal((await post('/chat/control', { sessionId: 'api-first', breakpoints: [] })).status, 204);
    assert.equal((await (await fetch('http://127.0.0.1:5893/stats')).json()).requests, baseline);
    assert.equal((await post('/chat/control', { sessionId: 'api-first', action: 'resume', breakpointId: first.notice.Id })).status, 204);
    let final = false;
    for await (const event of first.stream) if (event.kind === 'final') final = true;
    assert.ok(final);
    assert.equal((await (await fetch('http://127.0.0.1:5893/stats')).json()).requests, baseline + 1);
    assert.equal((await post('/chat/control', { sessionId: 'api-second', action: 'stop' })).status, 204);
    second.abort.abort();
    console.log('PASS: invalid kinds 400; cross-run/stale IDs 409; live settings preserve latch; concurrent runs isolated; continue and stop succeed.');
})().catch(error => { console.error(error); process.exitCode = 1; });