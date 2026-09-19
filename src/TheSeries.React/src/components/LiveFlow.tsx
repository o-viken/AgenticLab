import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Cpu, UserRound, Workflow, Wrench, type LucideIcon } from 'lucide-react'
import type { FlowNode, RunIdentity, RunState } from '../flow/runReducer'
import styles from './LiveFlow.module.css'

export function LiveFlow({ state, identity }: { state: RunState; identity: RunIdentity | null }) {
  function node(key: FlowNode, title: string, detail: string, Icon: LucideIcon) {
    return <div className={`${styles.node} ${styles[key]} ${state.node === key ? styles.active : ''}`} data-node={key} data-active={state.node === key}>
      <Icon size={19} aria-hidden="true" /><span className={styles.nodeTitle}>{title}</span><span className={styles.nodeDetail} title={detail}>{detail}</span>
    </div>
  }
  return <div className={styles.graph} aria-label="User, agent host, model and tools flow">
    <div className={styles.userRow}>{node('user', 'User', 'You', UserRound)}</div>
    <div className={styles.userLink} aria-hidden="true"><ArrowDown size={16} /><span>Input / reply</span><ArrowUp size={16} /></div>
    <div className={styles.agentGroup}>
      <span className={styles.groupTitle}>Agent</span>
      <div className={styles.agentNodes}>
        {node('host', 'Agent host', identity?.label ?? 'No agent selected', Workflow)}
        <div className={styles.modelLink} aria-hidden="true"><ArrowRight size={23} /><ArrowLeft size={23} /></div>
        {node('model', 'Model', identity?.model ?? 'Azure OpenAI', Cpu)}
      </div>
    </div>
    <div className={styles.toolLink} aria-hidden="true"><ArrowDown size={16} /><span>Call / result</span><ArrowUp size={16} /></div>
    <div className={styles.toolRow}>
      {node('tools', 'Tools', identity?.tools.length ? `${identity.tools.length} available` : 'No tools', Wrench)}
      <div className={styles.toolActivity}>
        {state.tool && <><span className={state.tool.phase === 'Returned' ? styles.returned : styles.requested}>{state.tool.phase}</span>
          <strong title={state.tool.name}>{state.tool.name}</strong><p title={state.tool.preview}>{state.tool.preview}</p></>}
      </div>
    </div>
  </div>
}