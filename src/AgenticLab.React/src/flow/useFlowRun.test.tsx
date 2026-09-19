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
  await act(async () => result.current.next())
  expect(transport.control).toHaveBeenCalledWith(expect.objectContaining({ action: 'next' }), expect.any(AbortSignal), true)
  await act(async () => { receive({ sequence: 1, kind: 'final', label: 'Answer', turn: 1, data: 'done' }); complete() })
  act(() => { result.current.send('second', identity) })
  const requests = vi.mocked(transport.stream).mock.calls.map(call => call[0])
  expect(requests[0].conversationId).toBe(requests[1].conversationId)
  expect(requests[0].sessionId).not.toBe(requests[1].sessionId)
  act(() => result.current.stop())
  await act(async () => result.current.reset())
  expect(transport.reset).toHaveBeenCalledWith(requests[0].conversationId, expect.any(AbortSignal))
  expect(result.current.state.messages).toEqual([])
})

it('cancels on unmount and retains messages when a reset fails', async () => {
  const transport: FlowApi = {
    catalogs: vi.fn(), control: vi.fn().mockResolvedValue(undefined), reset: vi.fn().mockRejectedValue(new Error('Reset rejected')),
    stream: vi.fn(async (_request, onEvent) => { onEvent({ sequence: 1, kind: 'final', label: 'Answer', turn: 1, data: 'keep this' }) }),
  }
  const { result, unmount } = renderHook(() => useFlowRun(transport))
  act(() => { result.current.send('hello', identity) })
  await waitFor(() => expect(result.current.state.phase).toBe('completed'))
  await act(async () => result.current.reset())
  expect(result.current.state.error).toBe('Reset rejected')
  expect(result.current.state.messages[1].text).toBe('keep this')
  vi.mocked(transport.stream).mockImplementation(() => new Promise(() => {}))
  act(() => { result.current.send('next', identity) })
  const signal = vi.mocked(transport.stream).mock.calls[1][2]
  unmount()
  expect(signal.aborted).toBe(true)
})