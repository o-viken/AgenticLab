import { z } from 'zod'

export const agentSchema = z.object({
  name: z.string(), description: z.string(), tools: z.array(z.string()),
  requiresWorkspace: z.boolean().default(false), modelId: z.string().nullish(),
  exampleId: z.string().nullish(), requiresExampleUi: z.boolean().optional(),
})
export const agentsSchema = z.object({ agents: z.array(agentSchema), default: z.string() })
export const vendorSchema = z.object({
  key: z.string(), displayName: z.string(),
  modes: z.array(z.object({ agent: z.string(), label: z.string() })),
})
export const vendorsSchema = z.object({ vendors: z.array(vendorSchema) })
export const flowEventSchema = z.object({
  sequence: z.number().int().nonnegative(), kind: z.string(), label: z.string(),
  turn: z.number().int().default(0), detail: z.string().nullish(), data: z.string().nullish(),
  callId: z.string().nullish(),
  toolCall: z.object({ name: z.string(), arguments: z.unknown() }).nullish(),
})
const breakpointSchema = z.object({ id: z.string(), paused: z.boolean(), manual: z.boolean() })

export type Agent = z.infer<typeof agentSchema>
export type Vendor = z.infer<typeof vendorSchema>
export type FlowEvent = z.infer<typeof flowEventSchema>
export type Catalogs = { agents: Agent[]; vendors: Vendor[]; defaultAgent: string }
export type Breakpoint = z.infer<typeof breakpointSchema>

export type ChatRequest = {
  message: string; agent: string; vendor: string | null
  sessionId: string; conversationId: string; manual: boolean; stepDelayMs: number
}
export type ControlRequest = {
  sessionId: string; action?: 'next' | 'pause' | 'resume' | 'stop' | 'answer'
  manual?: boolean; answer?: string; breakpointId?: string
}

export function readBreakpoint(data: string | null | undefined): Breakpoint {
  const value = JSON.parse(data ?? '{}')
  return breakpointSchema.parse({ id: value.id ?? value.Id, paused: value.paused ?? value.Paused, manual: value.manual ?? value.Manual })
}

export function availableVendors(catalogs: Catalogs): Vendor[] {
  const supported = new Set(catalogs.agents.filter(agent => !agent.requiresWorkspace && !agent.requiresExampleUi).map(agent => agent.name))
  return catalogs.vendors.map(vendor => ({ ...vendor, modes: vendor.modes.filter(mode => supported.has(mode.agent)) }))
    .filter(vendor => vendor.modes.length > 0)
}