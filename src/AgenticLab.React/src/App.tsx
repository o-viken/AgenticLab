import { useEffect, useState } from 'react'
import { ArrowDownLeft, ArrowUpRight, CircleAlert, LoaderCircle, RotateCcw, Waypoints } from 'lucide-react'
import { api } from './api/client'
import { availableVendors, type Catalogs } from './api/contracts'
import { useFlowRun } from './flow/useFlowRun'
import { phaseLabels, type RunIdentity } from './flow/runReducer'
import { Conversation } from './components/Conversation'
import { LiveFlow } from './components/LiveFlow'
import { RunControls } from './components/RunControls'
import { TraceList } from './components/TraceList'
import styles from './App.module.css'

export default function App() {
  const [catalogs, setCatalogs] = useState<Catalogs | null>(null)
  const [catalogError, setCatalogError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [selection, setSelection] = useState({ vendor: '', agent: '' })
  const [draft, setDraft] = useState('')
  const run = useFlowRun()

  useEffect(() => {
    const controller = new AbortController()
    void api.catalogs(controller.signal).then(value => {
      if (controller.signal.aborted) return
      const vendors = availableVendors(value)
      const vendor = vendors.find(item => item.modes.some(mode => mode.agent === value.defaultAgent)) ?? vendors[0]
      setCatalogs(value)
      setSelection({ vendor: vendor?.key ?? '', agent: vendor?.modes.find(mode => mode.agent === value.defaultAgent)?.agent ?? vendor?.modes[0].agent ?? '' })
    }).catch(error => {
      if (!controller.signal.aborted) setCatalogError(error instanceof Error ? error.message : 'Could not load agents.')
    })
    return () => controller.abort()
  }, [reload])

  const vendors = catalogs ? availableVendors(catalogs) : []
  const vendor = vendors.find(item => item.key === selection.vendor)
  const agent = catalogs?.agents.find(item => item.name === selection.agent)
  const identity: RunIdentity | null = agent ? {
    agent: agent.name, label: vendor?.modes.find(mode => mode.agent === agent.name)?.label ?? agent.name,
    vendor: vendor?.key ?? null, model: agent.modelId || 'Azure OpenAI', tools: agent.tools,
  } : null
  const locked = run.state.running || run.state.resetting
  const error = catalogError ?? run.state.error

  function send() {
    if (identity && run.send(draft, identity)) setDraft('')
  }

  return (
    <div className={styles.app}>
      <header className={styles.header}>
        <a href="/" className={styles.brand} aria-label="Agentic Lab home">
          <span className={styles.mark}><Waypoints size={24} aria-hidden="true" /></span>
          <h1>Agentic AI</h1>
        </a>
        <div className={styles.headerActions}>
          <span className={styles.edition}>Flow workspace <span>React</span></span>
          <a className={`icon-button ${styles.repositoryLink}`} href="https://github.com/o-viken/the-series"
            target="_blank" rel="noopener noreferrer" title="View Agentic Lab on GitHub (opens in a new tab)"
            aria-label="View Agentic Lab on GitHub (opens in a new tab)">
            <span className={styles.repositoryIcon} aria-hidden="true" />
          </a>
        </div>
      </header>
      <div className={styles.selectionBar}>
        <div className={styles.selectors}>
          <label>Host
            <select aria-label="Host" value={selection.vendor} disabled={locked || !vendors.length}
              onChange={event => {
                const chosen = vendors.find(item => item.key === event.target.value)!
                setSelection({ vendor: chosen.key, agent: chosen.modes[0].agent })
              }}>
              {!vendors.length && <option value="">{catalogError ? 'Unavailable' : catalogs ? 'No supported hosts' : 'Loading...'}</option>}
              {vendors.map(item => <option key={item.key} value={item.key}>{item.displayName}</option>)}
            </select>
          </label>
          <span className={styles.selectDivider} aria-hidden="true" />
          <label>Agent
            <select aria-label="Agent" value={selection.agent} disabled={locked || !vendor}
              onChange={event => setSelection({ ...selection, agent: event.target.value })}>
              {!vendor && <option value="">{catalogs ? 'No supported agents' : 'Loading...'}</option>}
              {vendor?.modes.map(mode => <option key={mode.agent} value={mode.agent}>{mode.label}</option>)}
            </select>
          </label>
        </div>
        <div className={styles.connection}><span className={catalogs ? styles.online : ''} />{catalogs ? 'API connected' : catalogError ? 'API unavailable' : 'Connecting'}</div>
      </div>
      {error && <div className={styles.error} role="alert">
        <CircleAlert size={17} aria-hidden="true" /><span>{error}</span>
        {catalogError && <button type="button" onClick={() => { setCatalogError(null); setReload(value => value + 1) }}><RotateCcw size={14} />Retry</button>}
      </div>}
      <main className={styles.workspace}>
        <Conversation run={run} draft={draft} onDraft={setDraft} onSend={send} identity={identity} loading={!catalogs && !catalogError} />
        <aside className={styles.observation} aria-label="Live execution">
          <div className={styles.flowHead}>
            <div><span className={styles.sectionNumber}>02</span><h2>Live flow</h2></div>
            <span className={styles.status} role="status">
              {run.state.running && !run.state.paused && !run.state.manual && !run.state.question
                ? <LoaderCircle size={13} className="spin" aria-hidden="true" /> : <span className={styles.statusDot} />}
              {phaseLabels[run.state.phase]}
            </span>
          </div>
          <RunControls run={run} />
          <LiveFlow state={run.state} identity={run.state.identity ?? identity} />
          <div className={styles.flowMeta}>
            <span><ArrowUpRight size={13} />{run.state.turn} model {run.state.turn === 1 ? 'turn' : 'turns'}</span>
            <span><ArrowDownLeft size={13} />{run.state.eventCount} {run.state.eventCount === 1 ? 'event' : 'events'}</span>
          </div>
          <TraceList state={run.state} />
        </aside>
      </main>
    </div>
  )
}
