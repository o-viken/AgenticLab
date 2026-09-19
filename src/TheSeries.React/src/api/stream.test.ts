import { afterEach, describe, expect, it, vi } from 'vitest'
import { readFlowStream } from './stream'
import { api } from './client'
import { availableVendors, type FlowEvent } from './contracts'

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers() })

describe('POST event stream', () => {
  it('decodes split UTF-8, CRLF, multiline data and multiple events without losing data', async () => {
    const source = ': heartbeat\r\nevent: flow\r\ndata: {"sequence":1,"kind":"received",\r\ndata: "label":"caf\u00e9"}\r\n\r\n'
      + 'event: flow\ndata: {"sequence":2,"kind":"final","label":"Answer","data":"hello"}\n\n'
    const bytes = new TextEncoder().encode(source)
    const events: FlowEvent[] = []
    await readFlowStream(new ReadableStream({ start(controller) {
      for (const byte of bytes) controller.enqueue(new Uint8Array([byte]))
      controller.close()
    } }), event => events.push(event))
    expect(events.map(event => event.sequence)).toEqual([1, 2])
    expect(events[0].label).toBe('caf\u00e9')
    expect(events[1].data).toBe('hello')
  })

  it('rejects malformed outer events and cancels the body', async () => {
    const cancel = vi.fn()
    const body = new ReadableStream({ start(controller) {
      controller.enqueue(new TextEncoder().encode('data: {"kind":"missing sequence"}\n\n'))
    }, cancel })
    await expect(readFlowStream(body, vi.fn())).rejects.toThrow()
    expect(cancel).toHaveBeenCalledOnce()
  })

  it('does not invent a completed event at an incomplete EOF', async () => {
    const event = vi.fn()
    await readFlowStream(new ReadableStream({ start(controller) {
      controller.enqueue(new TextEncoder().encode('data: {"sequence":1,"kind":"final","label":"cut off"}'))
      controller.close()
    } }), event)
    expect(event).not.toHaveBeenCalled()
  })

  it('retries only a definite startup control 404, never a chat or ambiguous control failure', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('', { status: 404 })).mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const waiting = api.control({ sessionId: 'run', action: 'next' }, new AbortController().signal, true)
    await vi.runAllTimersAsync()
    await waiting
    expect(fetchMock).toHaveBeenCalledTimes(2)
    fetchMock.mockReset().mockRejectedValue(new TypeError('network unavailable'))
    await expect(api.control({ sessionId: 'run', action: 'next' }, new AbortController().signal, true)).rejects.toThrow('network')
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('offers only vendor modes backed by non-workspace agents', () => {
    const vendors = availableVendors({ defaultAgent: 'chat', agents: [
      { name: 'chat', description: '', tools: [], requiresWorkspace: false },
      { name: 'coder', description: '', tools: [], requiresWorkspace: true },
    ], vendors: [{ key: 'test', displayName: 'Test', modes: [{ agent: 'chat', label: 'Chat' }, { agent: 'coder', label: 'Code' }] }] })
    expect(vendors[0].modes).toEqual([{ agent: 'chat', label: 'Chat' }])
  })
})