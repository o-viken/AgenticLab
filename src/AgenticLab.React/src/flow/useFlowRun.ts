import { useEffect, useRef, useState } from 'react'
import { api, type FlowApi } from '../api/client'
import type { ControlRequest } from '../api/contracts'
import { initialState, runReducer, type RunAction, type RunIdentity } from './runReducer'

const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'The service could not complete the request.'

export function useFlowRun(transport: FlowApi = api) {
  const [state, setState] = useState(initialState)
  const current = useRef(initialState)
  const generation = useRef(0)
  const conversation = useRef<string | null>(null)
  const streamController = useRef<AbortController | null>(null)
  const controlController = useRef<AbortController | null>(null)
  const resetController = useRef<AbortController | null>(null)

  function update(action: RunAction) {
    current.current = runReducer(current.current, action)
    setState(current.current)
  }

  useEffect(() => () => {
    generation.current++
    streamController.current?.abort()
    controlController.current?.abort()
    resetController.current?.abort()
  }, [])

  function send(message: string, identity: RunIdentity): boolean {
    if (!message.trim() || current.current.running || current.current.resetting) return false
    const controller = new AbortController()
    streamController.current = controller
    conversation.current ??= crypto.randomUUID()
    const version = ++generation.current
    const sessionId = crypto.randomUUID()
    update({ type: 'start', message: message.trim(), identity, sessionId })
    void transport.stream({ message: message.trim(), agent: identity.agent, vendor: identity.vendor,
      sessionId, conversationId: conversation.current, manual: current.current.manual, stepDelayMs: 600 },
    event => { if (version === generation.current) update({ type: 'event', sessionId, event }) }, controller.signal)
      .then(() => { if (version === generation.current) update({ type: 'end', sessionId }) })
      .catch(error => {
        if (version === generation.current && !controller.signal.aborted)
          update({ type: 'end', sessionId, error: errorMessage(error) })
      })
      .finally(() => { if (version === generation.current) streamController.current = null })
    return true
  }

  async function control(command: Omit<ControlRequest, 'sessionId'>) {
    const before = current.current
    if (!before.running || !before.sessionId || before.controlPending) return
    const version = generation.current
    const controller = new AbortController()
    controlController.current = controller
    const request = { ...command, sessionId: before.sessionId }
    update({ type: 'control-start' })
    try {
      await transport.control(request, AbortSignal.any([controller.signal, AbortSignal.timeout(5000)]), before.eventCount === 0)
      if (version === generation.current) update({ type: 'control-ok', sessionId: before.sessionId, command: request, eventCount: before.eventCount })
    } catch (error) {
      if (version === generation.current && !controller.signal.aborted)
        update({ type: 'control-error', sessionId: before.sessionId, error: errorMessage(error) })
    } finally { if (version === generation.current) controlController.current = null }
  }

  function setManual(manual: boolean) {
    if (current.current.resetting) return
    if (current.current.running) void control({ action: current.current.breakpoint && manual ? 'next' : 'resume', manual, breakpointId: current.current.breakpoint?.id })
    else update({ type: 'mode', manual })
  }

  function next() {
    const snapshot = current.current
    if ((!snapshot.manual && !snapshot.breakpoint) || snapshot.stepPending || snapshot.question) return
    void control({ action: 'next', breakpointId: snapshot.breakpoint?.id })
  }

  function togglePause() {
    const snapshot = current.current
    void control({ action: snapshot.paused || snapshot.breakpoint ? 'resume' : 'pause', breakpointId: snapshot.breakpoint?.id })
  }

  function answer(text: string) {
    if (text.trim() && current.current.question) void control({ action: 'answer', answer: text.trim() })
  }

  function stop() {
    const snapshot = current.current
    if (!snapshot.running || !snapshot.sessionId) return
    update({ type: 'stop' })
    generation.current++
    streamController.current?.abort()
    controlController.current?.abort()
    streamController.current = null
    void transport.control({ sessionId: snapshot.sessionId, action: 'stop' }, AbortSignal.timeout(5000)).catch(() => {})
  }

  async function reset() {
    if (current.current.running || current.current.resetting) return
    const version = ++generation.current
    const controller = new AbortController()
    resetController.current = controller
    update({ type: 'reset-start' })
    try {
      if (conversation.current) await transport.reset(conversation.current, AbortSignal.any([controller.signal, AbortSignal.timeout(10000)]))
      if (version !== generation.current) return
      conversation.current = crypto.randomUUID()
      update({ type: 'reset' })
    } catch (error) {
      if (version === generation.current && !controller.signal.aborted) update({ type: 'reset-error', error: errorMessage(error) })
    } finally { if (version === generation.current) resetController.current = null }
  }

  return { state, send, next, setManual, togglePause, stop, answer, reset }
}

export type FlowRun = ReturnType<typeof useFlowRun>