import { StrictMode, type ReactNode } from 'react'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import type { FlowApi } from '../api/client'
import type { FlowEvent } from '../api/contracts'
import { useFlowRun } from './useFlowRun'

afterEach(cleanup)
const identity = { agent: 'Test', label: 'Test agent', vendor: 'default', model: 'model', tools: [] }

it('submits once under StrictMode, steps before data, and keeps conversation identity across turns', async () => {
  let receive: (event: FlowEvent) => void = () => {}
  let complete: () => void = () => {}
  const transport: FlowApi = {
    catalogs: vi.fn(), reset: vi.fn().mockResolvedValue(undefined), control: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn((_request, onEvent) => new Promise<void>(resolve => { receive = onEvent; complete = resolve })),
  }
  const { result } = renderHook(() => useFlowRun(transport), { wrapper: ({ children }: { children: ReactNode }) => <StrictMode>{children}</StrictMode> })
  act(() => { result.current.setManual(true); result.current.send('first', identity); result.current.send('duplicate', identity) })
  expect(transport.stream).toHaveBeenCalledOnce()
  await act(async () => { expect(await result.current.reset()).toBe(false) })
  expect(transport.reset).not.toHaveBeenCalled()
  await act(async () => result.current.next())
  expect(transport.control).toHaveBeenCalledWith(expect.objectContaining({ action: 'next' }), expect.any(AbortSignal), true)
  await act(async () => { receive({ sequence: 1, kind: 'final', label: 'Answer', turn: 1, data: 'done' }); complete() })
  act(() => { result.current.send('second', identity) })
  const requests = vi.mocked(transport.stream).mock.calls.map(call => call[0])
  expect(requests[0].conversationId).toBe(requests[1].conversationId)
  expect(requests[0].sessionId).not.toBe(requests[1].sessionId)
  act(() => result.current.stop())
  await act(async () => { expect(await result.current.reset()).toBe(true) })
  expect(transport.reset).toHaveBeenCalledWith(requests[0].conversationId, expect.any(AbortSignal))
  expect(result.current.state.messages).toEqual([])
  expect(result.current.state.trace).toEqual([])
  expect(result.current.state.manual).toBe(true)
  act(() => { result.current.send('fresh', { ...identity, vendor: 'chatgpt' }) })
  expect(vi.mocked(transport.stream).mock.calls[2][0].conversationId).not.toBe(requests[0].conversationId)
})

it('cancels on unmount and retains messages when a reset fails', async () => {
  const transport: FlowApi = {
    catalogs: vi.fn(), control: vi.fn().mockResolvedValue(undefined), reset: vi.fn().mockRejectedValue(new Error('Reset rejected')),
    stream: vi.fn(async (_request, onEvent) => { onEvent({ sequence: 1, kind: 'final', label: 'Answer', turn: 1, data: 'keep this' }) }),
  }
  const { result, unmount } = renderHook(() => useFlowRun(transport))
  act(() => { result.current.send('hello', identity) })
  await waitFor(() => expect(result.current.state.phase).toBe('completed'))
  await act(async () => { expect(await result.current.reset()).toBe(false) })
  expect(result.current.state.error).toBe('Reset rejected')
  expect(result.current.state.messages[1].text).toBe('keep this')
  vi.mocked(transport.stream).mockImplementation(() => new Promise(() => {}))
  act(() => { result.current.send('next', identity) })
  expect(vi.mocked(transport.stream).mock.calls[1][0].conversationId).toBe(vi.mocked(transport.stream).mock.calls[0][0].conversationId)
  const signal = vi.mocked(transport.stream).mock.calls[1][2]
  unmount()
  expect(signal.aborted).toBe(true)
})

it('locks sends and duplicate resets while pending and rejects a stale reset after unmount', async () => {
  let completeReset: () => void = () => {}
  const transport: FlowApi = {
    catalogs: vi.fn(), control: vi.fn(),
    reset: vi.fn(() => new Promise<void>(resolve => { completeReset = resolve })),
    stream: vi.fn(async (_request, onEvent) => { onEvent({ sequence: 1, kind: 'final', label: 'Answer', turn: 1, data: 'done' }) }),
  }
  const { result, unmount } = renderHook(() => useFlowRun(transport))
  act(() => { result.current.send('hello', identity) })
  await waitFor(() => expect(result.current.state.phase).toBe('completed'))
  let pending!: Promise<boolean>
  act(() => { pending = result.current.reset() })
  expect(result.current.state.resetting).toBe(true)
  act(() => { expect(result.current.send('blocked', identity)).toBe(false) })
  await act(async () => { expect(await result.current.reset()).toBe(false) })
  expect(transport.reset).toHaveBeenCalledOnce()
  unmount()
  expect(vi.mocked(transport.reset).mock.calls[0][1]?.aborted).toBe(true)
  completeReset()
  expect(await pending).toBe(false)
})