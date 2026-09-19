import { describe, expect, it } from 'vitest'
import { initialState, runReducer, type RunIdentity } from './runReducer'
import type { FlowEvent } from '../api/contracts'

const identity: RunIdentity = { agent: 'Test', label: 'Test', vendor: 'default', model: 'model', tools: ['Search'] }
const started = () => runReducer(initialState, { type: 'start', sessionId: 'run', message: 'hello', identity })
const event = (sequence: number, kind: string, data?: string): FlowEvent => ({ sequence, kind, label: kind, turn: 1, data })

describe('run state', () => {
  it('enables the first manual step before receiving an event', () => {
    const state = runReducer({ ...initialState, manual: true }, { type: 'start', sessionId: 'run', message: 'hello', identity })
    expect(state.phase).toBe('awaiting-step')
    expect(state.stepPending).toBe(false)
  })

  it('does not get stuck if the event arrives before the control acknowledgement', () => {
    let state = runReducer(started(), { type: 'mode', manual: true })
    state = runReducer(state, { type: 'control-start' })
    state = runReducer(state, { type: 'event', sessionId: 'run', event: event(1, 'received') })
    state = runReducer(state, { type: 'control-ok', sessionId: 'run', command: { sessionId: 'run', action: 'next' }, eventCount: 0 })
    expect(state.stepPending).toBe(false)
    expect(state.phase).toBe('awaiting-step')
  })

  it('pairs repeated tool calls by call ID and only renders final once', () => {
    let state = started()
    state = runReducer(state, { type: 'event', sessionId: 'run', event: { ...event(1, 'tool-call'), callId: 'one', toolCall: { name: 'Search', arguments: {} } } })
    state = runReducer(state, { type: 'event', sessionId: 'run', event: { ...event(2, 'tool-call'), callId: 'two', toolCall: { name: 'Calculate', arguments: {} } } })
    state = runReducer(state, { type: 'event', sessionId: 'run', event: { ...event(3, 'tool-result', 'result'), callId: 'one' } })
    expect(state.tool?.name).toBe('Search')
    state = runReducer(state, { type: 'event', sessionId: 'run', event: event(4, 'llm-response', 'not the final answer') })
    expect(state.messages[1].text).toBe('')
    state = runReducer(state, { type: 'event', sessionId: 'run', event: event(5, 'final', 'answer') })
    state = runReducer(state, { type: 'event', sessionId: 'run', event: event(5, 'final', 'answer') })
    expect(state.messages).toHaveLength(2)
    expect(state.messages[1].text).toBe('answer')
    expect(state.phase).toBe('completed')
    expect(runReducer(state, { type: 'end', sessionId: 'run' }).phase).toBe('completed')
  })

  it('rejects late events, identifies interruption and bounds the lightweight trace', () => {
    let state = started()
    expect(runReducer(state, { type: 'event', sessionId: 'old', event: event(1, 'final') })).toBe(state)
    for (let sequence = 1; sequence <= 250; sequence++) state = runReducer(state, { type: 'event', sessionId: 'run', event: event(sequence, 'future-kind', 'payload') })
    expect(state.trace).toHaveLength(200)
    expect(state.trace[0]).not.toHaveProperty('data')
    expect(runReducer(state, { type: 'end', sessionId: 'run' }).phase).toBe('interrupted')
    state = runReducer(state, { type: 'stop' })
    expect(runReducer(state, { type: 'event', sessionId: 'run', event: event(251, 'final') }).phase).toBe('stopped')
  })

  it('handles questions and PascalCase breakpoint notices without inventing progress', () => {
    let state = runReducer(started(), { type: 'event', sessionId: 'run', event: event(1, 'ask-question', 'Which one?') })
    expect(state.phase).toBe('awaiting-answer')
    state = runReducer(state, { type: 'control-ok', sessionId: 'run', eventCount: 1, command: { sessionId: 'run', action: 'answer', answer: 'First' } })
    state = runReducer(state, { type: 'event', sessionId: 'run', event: event(2, 'breakpoint', '{"Id":"pause","Paused":true,"Manual":false}') })
    expect(state.phase).toBe('paused')
    expect(state.breakpoint?.id).toBe('pause')
  })
})