import { readBreakpoint, type Breakpoint, type ControlRequest, type FlowEvent } from '../api/contracts'

export type RunIdentity = { agent: string; label: string; vendor: string | null; model: string; tools: string[] }
export type Phase = 'idle' | 'starting' | 'running' | 'awaiting-step' | 'paused' | 'awaiting-answer' | 'completed' | 'stopped' | 'interrupted' | 'failed'
export type Message = { id: string; role: 'user' | 'assistant'; text: string; label: string; status: 'complete' | 'pending' | 'stopped' | 'error' }
export type TraceEntry = Pick<FlowEvent, 'sequence' | 'kind' | 'label' | 'turn'>
export type FlowNode = 'user' | 'host' | 'model' | 'tools'
export type RunState = {
  sessionId: string | null; identity: RunIdentity | null; running: boolean; phase: Phase
  manual: boolean; paused: boolean; controlPending: boolean; stepPending: boolean; resetting: boolean
  messages: Message[]; trace: TraceEntry[]; eventCount: number; lastSequence: number; turn: number
  node: FlowNode | null; question: string | null; breakpoint: Breakpoint | null; error: string | null
  calls: { id: string; name: string }[]
  tool: { name: string; phase: 'Requested' | 'Returned'; preview: string } | null
}
export const initialState: RunState = {
  sessionId: null, identity: null, running: false, phase: 'idle', manual: false, paused: false,
  controlPending: false, stepPending: false, resetting: false, messages: [], trace: [], eventCount: 0,
  lastSequence: 0, turn: 0, node: null, question: null, breakpoint: null, error: null, calls: [], tool: null,
}
export type RunAction =
  | { type: 'start'; sessionId: string; message: string; identity: RunIdentity }
  | { type: 'event'; sessionId: string; event: FlowEvent }
  | { type: 'end'; sessionId: string; error?: string }
  | { type: 'stop' }
  | { type: 'mode'; manual: boolean }
  | { type: 'control-start' }
  | { type: 'control-ok'; sessionId: string; command: ControlRequest; eventCount: number }
  | { type: 'control-error'; sessionId: string; error: string }
  | { type: 'reset-start' }
  | { type: 'reset' }
  | { type: 'reset-error'; error: string }

function progress(state: RunState): RunState {
  if (!state.running) return state
  const phase = state.question ? 'awaiting-answer' : state.paused || state.breakpoint ? 'paused'
    : state.manual && !state.stepPending ? 'awaiting-step' : state.eventCount ? 'running' : 'starting'
  return { ...state, phase }
}

function finish(state: RunState, phase: Phase, text?: string, error?: string): RunState {
  return { ...state, running: false, phase, controlPending: false, stepPending: false, paused: false,
    question: null, breakpoint: null, error: error ?? null,
    messages: state.messages.map(message => message.id === state.sessionId
      ? { ...message, text: text ?? message.text, status: phase === 'completed' ? 'complete' : phase === 'stopped' ? 'stopped' : 'error' }
      : message) }
}

export function runReducer(state: RunState, action: RunAction): RunState {
  switch (action.type) {
    case 'start':
      return progress({ ...initialState, manual: state.manual, running: true, sessionId: action.sessionId,
        identity: action.identity, messages: [...state.messages.slice(-38),
          { id: `${action.sessionId}-user`, role: 'user', text: action.message, label: 'You', status: 'complete' },
          { id: action.sessionId, role: 'assistant', text: '', label: action.identity.label, status: 'pending' }] })
    case 'mode': return progress({ ...state, manual: action.manual })
    case 'stop': return state.running ? finish(state, 'stopped') : state
    case 'end':
      if (action.sessionId !== state.sessionId || !state.running) return state
      return finish(state, state.eventCount ? 'interrupted' : 'failed', undefined,
        action.error ?? 'The stream ended before a final answer arrived.')
    case 'reset-start': return { ...state, resetting: true, error: null }
    case 'reset': return { ...initialState, manual: state.manual }
    case 'reset-error': return { ...state, resetting: false, error: action.error }
    case 'control-start': return { ...state, controlPending: true, error: null }
    case 'control-error':
      return action.sessionId === state.sessionId && state.running
        ? progress({ ...state, controlPending: false, stepPending: false, error: action.error }) : state
    case 'control-ok': {
      if (action.sessionId !== state.sessionId || !state.running) return state
      const command = action.command
      return progress({ ...state, controlPending: false,
        manual: command.manual ?? (command.breakpointId ? command.action === 'next' : state.manual),
        paused: command.action === 'pause' ? true : command.action === 'resume' || command.breakpointId ? false : state.paused,
        breakpoint: command.breakpointId ? null : state.breakpoint,
        question: command.action === 'answer' ? null : state.question,
        stepPending: command.action === 'next' && state.eventCount === action.eventCount })
    }
    case 'event': {
      if (action.sessionId !== state.sessionId || !state.running || action.event.sequence <= state.lastSequence) return state
      const event = action.event
      const nodes: Record<string, FlowNode> = {
        received: 'host', 'llm-request': 'model', 'llm-response': 'host', 'tool-call': 'tools',
        'tool-result': 'host', 'ask-question': 'user', final: 'user', error: 'host',
      }
      let next: RunState = { ...state, stepPending: false, lastSequence: event.sequence,
        eventCount: state.eventCount + 1, turn: Math.max(state.turn, event.turn), node: nodes[event.kind] ?? state.node,
        trace: [...state.trace.slice(-199), { sequence: event.sequence, kind: event.kind, label: event.label.slice(0, 200), turn: event.turn }] }
      if (event.kind === 'tool-call') {
        const name = event.toolCall?.name ?? 'Tool'
        next = { ...next, tool: { name, phase: 'Requested', preview: (event.detail ?? '').slice(0, 240) },
          calls: event.callId ? [...state.calls.slice(-99), { id: event.callId, name }] : state.calls }
      }
      if (event.kind === 'tool-result') next.tool = {
        name: state.calls.find(call => call.id === event.callId)?.name ?? 'Tool', phase: 'Returned',
        preview: (event.data ?? event.detail ?? '').slice(0, 240),
      }
      if (event.kind === 'ask-question') next.question = event.data ?? event.detail ?? 'Your answer is needed.'
      if (event.kind === 'breakpoint') {
        const notice = readBreakpoint(event.data)
        next.breakpoint = notice.paused ? notice : null
        next.manual = notice.manual
        next.paused = notice.paused
      }
      if (event.kind === 'final') return finish(next, 'completed', event.data ?? event.detail ?? '')
      if (event.kind === 'error') return finish(next, 'failed', undefined, event.detail ?? event.label)
      return progress(next)
    }
  }
}

export const phaseLabels: Record<Phase, string> = {
  idle: 'Ready', starting: 'Connecting', running: 'Running', 'awaiting-step': 'Waiting for Next',
  paused: 'Paused', 'awaiting-answer': 'Waiting for you', completed: 'Complete', stopped: 'Stopped',
  interrupted: 'Interrupted', failed: 'Failed',
}