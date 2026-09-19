import { createParser } from 'eventsource-parser'
import { flowEventSchema, type FlowEvent } from './contracts'

export async function readFlowStream(body: ReadableStream<Uint8Array>, onEvent: (event: FlowEvent) => void): Promise<void> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  const parser = createParser({
    maxBufferSize: 4 * 1024 * 1024,
    onEvent(message) {
      if (message.event && message.event !== 'flow') return
      onEvent(flowEventSchema.parse(JSON.parse(message.data)))
    },
    onError(error) { throw new Error(`Invalid event stream: ${error.type}`) },
  })
  try {
    while (true) {
      const chunk = await reader.read()
      if (chunk.done) break
      parser.feed(decoder.decode(chunk.value, { stream: true }))
    }
    parser.feed(decoder.decode())
  } finally {
    await reader.cancel().catch(() => {})
    reader.releaseLock()
  }
}