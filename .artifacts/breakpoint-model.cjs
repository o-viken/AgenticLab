const http = require('node:http');
let requests = 0;
http.createServer(async (request, response) => {
    if (request.url === '/stats') {
        response.setHeader('Content-Type', 'application/json');
        response.end(JSON.stringify({ requests }));
        return;
    }
    let body = '';
    for await (const chunk of request) body += chunk;
    const payload = JSON.parse(body);
    requests++;
    const hasResult = payload.messages.some(message => message.role === 'tool');
    const hasCalculator = payload.tools?.some(tool => tool.function.name === 'Calculate');
    const useTool = hasCalculator && !hasResult;
    response.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache' });
    const delta = useTool
        ? { role: 'assistant', tool_calls: [{ index: 0, id: 'calculate-' + requests, type: 'function', function: { name: 'Calculate', arguments: '{"expression":"2+2"}' } }] }
        : { role: 'assistant', content: '4 (local verification model)' };
    const envelope = { id: 'test-' + requests, object: 'chat.completion.chunk', created: 1700000000, model: 'local-test' };
    response.write('data: ' + JSON.stringify({ ...envelope, choices: [{ index: 0, delta, finish_reason: null }] }) + '\n\n');
    response.write('data: ' + JSON.stringify({ ...envelope, choices: [{ index: 0, delta: {}, finish_reason: useTool ? 'tool_calls' : 'stop' }] }) + '\n\n');
    response.end('data: [DONE]\n\n');
}).listen(5893, '127.0.0.1', () => console.log('Local verification model: http://127.0.0.1:5893'));