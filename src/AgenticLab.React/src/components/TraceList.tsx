import { useEffect, useRef } from 'react'
import { ArrowDownLeft, ArrowUpRight, Check, Circle, CircleAlert, Wrench } from 'lucide-react'
import type { RunState } from '../flow/runReducer'
import styles from './TraceList.module.css'

export function TraceList({ state }: { state: RunState }) {
  const list = useRef<HTMLOListElement>(null)
  const following = useRef(true)
  useEffect(() => {
    if (list.current && following.current) list.current.scrollTop = list.current.scrollHeight
  }, [state.eventCount])
  return <section className={styles.activity} aria-label="Run activity">
    <header><h3>Activity</h3><span>{state.eventCount > state.trace.length ? `Latest ${state.trace.length} of ${state.eventCount}` : String(state.eventCount).padStart(2, '0')}</span></header>
    {!state.trace.length && <p className={styles.empty}>No events yet.</p>}
    <ol ref={list} onScroll={() => {
      const element = list.current!
      following.current = element.scrollHeight - element.scrollTop - element.clientHeight < 40
    }}>
      {state.trace.map(entry => {
        const Icon = entry.kind === 'final' ? Check : entry.kind === 'error' ? CircleAlert : entry.kind === 'tool-call' || entry.kind === 'tool-result' ? Wrench
          : entry.kind === 'llm-request' ? ArrowUpRight : entry.kind === 'llm-response' ? ArrowDownLeft : Circle
        return <li key={entry.sequence}><span className={styles.sequence}>{String(entry.sequence).padStart(2, '0')}</span><Icon size={14} aria-hidden="true" />
          <span title={entry.label}>{entry.label}</span>{entry.turn > 0 && <span className={styles.turn}>T{entry.turn}</span>}</li>
      })}
    </ol>
  </section>
}