import http from 'node:http'

const sessions = new Map()
const conversations = new Map()
const agents = [
  { name: 'Research', description: 'Research with tools', tools: ['SearchWiki', 'Calculate'], modelId: 'test-model' },
  { name: 'Chat', description: 'Conversation', tools: [], modelId: 'test-model' },
  { name: 'Question', description: 'Asks a question', tools: ['AskQuestion'], modelId: 'test-model' },
  { name: 'Coder', description: 'Workspace agent', tools: ['ReadFile'], modelId: 'test-model', requiresWorkspace: true },
]

function emitNext(session) {
  clearTimeout(session.timer)
  if (session.response.destroyed || session.response.writableEnded || session.question || (session.manual ? !session.credit : session.paused)) return
  session.timer = setTimeout(() => {
    if (session.manual && !session.credit || !session.manual && session.paused) return
    session.credit = false
    if (session.index >= session.events.length) { session.response.end(); return }
    const event = { sequence: session.index + 1, turn: session.index ? 1 : 0, ...session.events[session.index++] }
    if (!session.response.headersSent) session.response.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-store' })
    session.response.write(`event: flow\r\ndata: ${JSON.stringify(event)}\r\n\r\n`)
    if (event.kind === 'ask-question') session.question = true
    if (event.kind === 'final' || event.kind === 'error') { session.response.end(); return }
    emitNext(session)
  }, session.manual ? 5 : 90)
}

const server = http.createServer(async (request, response) => {
  const chunks = []
  for await (const chunk of request) chunks.push(chunk)
  const body = Buffer.concat(chunks).toString()
  const payload = body ? JSON.parse(body) : {}
  function json(value, code = 200) { response.writeHead(code, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(value)) }
  if (request.url === '/health') { json({ ok: true }); return }
  if (request.url === '/agents') { json({ agents, default: 'Research' }); return }
  if (request.url === '/vendors') {
    json({ vendors: [
      { key: 'default', displayName: 'Default', modes: agents.map(agent => ({ agent: agent.name, label: agent.name })) },
      { key: 'chatgpt', displayName: 'ChatGPT', modes: [{ agent: 'Chat', label: 'Chat' }] },
    ] }); return
  }
  if (request.url === '/chat/stream') {
    const count = (conversations.get(payload.conversationId) ?? 0) + 1
    conversations.set(payload.conversationId, count)
    const events = [
      { kind: 'received', label: 'Message received' },
      { kind: 'llm-request', label: 'Model request sent', data: '{"instructions":"fixture","messages":[]}' },
    ]
    if (payload.agent === 'Research') events.push(
      { kind: 'llm-response', label: 'Model requested tools', data: '{"toolCalls":[{}]}' },
      { kind: 'tool-call', label: 'Search requested', detail: 'agentic AI', callId: 'search', toolCall: { name: 'SearchWiki', arguments: { query: 'agentic AI' } } },
      { kind: 'tool-call', label: 'Calculation requested', detail: '2 + 2', callId: 'calculate', toolCall: { name: 'Calculate', arguments: { expression: '2 + 2' } } },
      { kind: 'tool-result', label: 'Search returned', callId: 'search', data: 'An agent combines a host with a model.' },
      { kind: 'tool-result', label: 'Calculation returned', callId: 'calculate', data: '4' },
      { kind: 'llm-request', label: 'Model request sent', turn: 2 },
    )
    if (payload.agent === 'Question') events.push({ kind: 'ask-question', label: 'Question asked', data: 'Which topic should I use?', callId: 'question' })
    events.push({ kind: 'llm-response', label: 'Model response received', data: '{"text":"Answer"}' },
      { kind: 'final', label: 'Final answer', data: `This is answer ${count}. **An agent combines a host with a model.**\n\nThe model chooses a next step. The host checks permissions and executes allowed tools.`, detail: `Answer ${count}` })
    if (payload.message === 'interrupt') events.splice(2)
    if (payload.message === 'error') events.splice(2, events.length, { kind: 'error', label: 'Test error', detail: 'The upstream run failed.' })
    const session = { response, events, index: 0, manual: payload.manual, paused: false, credit: false, question: false }
    sessions.set(payload.sessionId, session)
    response.on('close', () => { clearTimeout(session.timer); sessions.delete(payload.sessionId) })
    emitNext(session)
    return
  }
  if (request.url === '/chat/control') {
    const session = sessions.get(payload.sessionId)
    if (!session) { json({ title: 'Session not found' }, 404); return }
    if (payload.manual !== undefined) session.manual = payload.manual
    if (payload.action === 'next') session.credit = true
    if (payload.action === 'pause') session.paused = true
    if (payload.action === 'resume') session.paused = false
    if (payload.action === 'answer') {
      session.question = false
      session.events.at(-1).data = `You chose: ${payload.answer}`
    }
    if (payload.action === 'stop') { clearTimeout(session.timer); session.response.end() }
    else emitNext(session)
    response.writeHead(204); response.end(); return
  }
  if (request.url === '/chat/reset') { conversations.delete(payload.conversationId); response.writeHead(204); response.end(); return }
  json({ title: 'Not found' }, 404)
})
server.listen(5197, '127.0.0.1', () => console.log('React test fixture http://127.0.0.1:5197'))