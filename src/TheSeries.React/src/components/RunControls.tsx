import { Pause, Play, Square, StepForward } from 'lucide-react'
import type { FlowRun } from '../flow/useFlowRun'
import styles from './RunControls.module.css'

export function RunControls({ run }: { run: FlowRun }) {
  const { state } = run
  const unavailable = !state.running || state.controlPending || !!state.question
  return <div className={styles.controls}>
    <div className={styles.modes} role="group" aria-label="Execution mode">
      <button type="button" aria-pressed={!state.manual} disabled={state.controlPending || state.resetting || !!state.question} onClick={() => run.setManual(false)}>Auto</button>
      <button type="button" aria-pressed={state.manual} disabled={state.controlPending || state.resetting || !!state.question} onClick={() => run.setManual(true)}>Manual</button>
    </div>
    <div className={styles.transport}>
      <button type="button" className="icon-button" title="Next step" aria-label="Next step"
        disabled={unavailable || (!state.manual && !state.breakpoint) || state.stepPending} onClick={run.next}><StepForward size={18} /></button>
      <button type="button" className="icon-button" title={state.paused ? 'Resume' : 'Pause'} aria-label={state.paused ? 'Resume' : 'Pause'}
        disabled={unavailable || (state.manual && !state.breakpoint)} onClick={run.togglePause}>{state.paused ? <Play size={17} /> : <Pause size={17} />}</button>
      <button type="button" className="icon-button" title="Stop run" aria-label="Stop run" disabled={!state.running} onClick={run.stop}><Square size={15} /></button>
    </div>
  </div>
}