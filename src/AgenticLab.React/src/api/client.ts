import { agentsSchema, vendorsSchema, type Catalogs, type ChatRequest, type ControlRequest, type FlowEvent } from './contracts'
import { readFlowStream } from './stream'

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) { super(message); this.status = status }
}

export interface FlowApi {
  catalogs(signal: AbortSignal): Promise<Catalogs>
  stream(request: ChatRequest, onEvent: (event: FlowEvent) => void, signal: AbortSignal): Promise<void>
  control(request: ControlRequest, signal: AbortSignal, starting?: boolean): Promise<void>
  reset(conversationId: string, signal: AbortSignal): Promise<void>
}

async function checked(response: Response): Promise<Response> {
  if (response.ok) return response
  let message = `Request failed (${response.status}).`
  try {
    const value = await response.json()
    if (typeof value === 'string') message = value
    else if (typeof value.detail === 'string') message = value.detail
    else if (typeof value.title === 'string') message = value.title
  } catch { }
  throw new ApiError(response.status, message.slice(0, 500))
}

function post(path: string, body: unknown, signal: AbortSignal): Promise<Response> {
  return fetch(`/api${path}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Accept: path === '/chat/stream' ? 'text/event-stream' : 'application/json' },
    body: JSON.stringify(body), signal,
  }).then(checked)
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    signal.throwIfAborted()
    const abort = () => { clearTimeout(timer); reject(signal.reason) }
    const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve() }, milliseconds)
    signal.addEventListener('abort', abort, { once: true })
  })
}

export const api: FlowApi = {
  async catalogs(signal) {
    const [agents, vendors] = await Promise.all([
      fetch('/api/agents', { signal }).then(checked).then(response => response.json()).then(value => agentsSchema.parse(value)),
      fetch('/api/vendors', { signal }).then(checked).then(response => response.json()).then(value => vendorsSchema.parse(value)),
    ])
    return { agents: agents.agents, defaultAgent: agents.default, vendors: vendors.vendors }
  },
  async stream(request, onEvent, signal) {
    const response = await post('/chat/stream', request, signal)
    if (!response.body || !response.headers.get('content-type')?.includes('text/event-stream'))
      throw new Error('The service did not return an event stream.')
    await readFlowStream(response.body, onEvent)
  },
  async control(request, signal, starting = false) {
    const waits = [100, 200, 400, 800]
    for (let attempt = 0; ; attempt++) {
      try { await post('/chat/control', request, signal); return }
      catch (error) {
        if (!starting || !(error instanceof ApiError) || error.status !== 404 || attempt >= waits.length) throw error
        await delay(waits[attempt], signal)
      }
    }
  },
  async reset(conversationId, signal) { await post('/chat/reset', { conversationId }, signal) },
}