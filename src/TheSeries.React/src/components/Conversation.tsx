import { useEffect, useRef, useState } from 'react'
import { ArrowDown, ArrowUp, ArrowUpRight, Check, LoaderCircle, MessageSquare, MessageSquarePlus } from 'lucide-react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { FlowRun } from '../flow/useFlowRun'
import { phaseLabels, type RunIdentity } from '../flow/runReducer'
import styles from './Conversation.module.css'

type Props = { run: FlowRun; draft: string; onDraft: (text: string) => void; onSend: () => void; identity: RunIdentity | null; loading: boolean }
const prompts = ['What is an AI agent?', 'How does an agent decide to use a tool?', 'Compare reasoning with taking an action.']

export function Conversation({ run, draft, onDraft, onSend, identity, loading }: Props) {
  const log = useRef<HTMLDivElement>(null)
  const stick = useRef(true)
  const [offBottom, setOffBottom] = useState(false)
  const [answer, setAnswer] = useState('')
  const { state } = run
  useEffect(() => {
    if (stick.current && log.current) log.current.scrollTop = log.current.scrollHeight
  }, [state.messages, state.phase])

  return <section className={styles.conversation} aria-label="Conversation">
    <header className={styles.heading}>
      <div><span>01</span><h2>Conversation</h2></div>
      <button type="button" className="icon-button" title="New conversation" aria-label="New conversation"
        disabled={state.running || state.resetting || !state.messages.length} onClick={() => void run.reset()}>
        {state.resetting ? <LoaderCircle size={19} className="spin" /> : <MessageSquarePlus size={19} />}
      </button>
    </header>
    <div className={styles.log} ref={log} role="log" aria-live="polite" aria-label="Messages" onScroll={() => {
      const element = log.current!
      stick.current = element.scrollHeight - element.scrollTop - element.clientHeight < 48
      setOffBottom(!stick.current)
    }}>
      {state.messages.length === 0 && <div className={styles.empty}>
        <MessageSquare size={32} strokeWidth={1.2} aria-hidden="true" />
        <h3>{loading ? 'Connecting to your agents' : identity ? 'A new conversation' : 'No agents available'}</h3>
        {identity && <div className={styles.suggestions}>{prompts.map(prompt =>
          <button type="button" key={prompt} onClick={() => onDraft(prompt)}>{prompt}<ArrowUpRight size={15} aria-hidden="true" /></button>
        )}</div>}
      </div>}
      {state.messages.map(message => <article className={`${styles.message} ${message.role === 'user' ? styles.user : styles.assistant}`} key={message.id}>
        <div className={styles.messageHead}><span>{message.label}</span>
          {message.status === 'complete' && message.role === 'assistant' && <Check size={13} aria-label="Complete" />}
        </div>
        {message.text
          ? <div className={styles.markdown}><Markdown remarkPlugins={[remarkGfm]} skipHtml components={{ a: props => <a {...props} target="_blank" rel="noopener noreferrer" /> }}>{message.text}</Markdown></div>
          : <div className={styles.pending}>{message.status === 'pending' ? <><span className={styles.pulse} />{phaseLabels[state.phase]}</> : message.status === 'stopped' ? 'Run stopped.' : 'No final answer.'}</div>}
      </article>)}
    </div>
    {offBottom && <button type="button" className={styles.latest} onClick={() => {
      stick.current = true
      log.current?.scrollTo({ top: log.current.scrollHeight, behavior: 'instant' })
    }}><ArrowDown size={14} />Latest message</button>}
    {state.question && <form className={styles.question} onSubmit={event => { event.preventDefault(); run.answer(answer) }}>
      <label htmlFor="agent-answer">{state.question}</label>
      <div><input id="agent-answer" value={answer} onChange={event => setAnswer(event.target.value)} disabled={state.controlPending} autoComplete="off" />
        <button type="submit" className="icon-button" title="Send answer" aria-label="Send answer" disabled={!answer.trim() || state.controlPending}><ArrowUp size={18} /></button></div>
    </form>}
    <form className={styles.composer} onSubmit={event => { event.preventDefault(); onSend() }}>
      <textarea aria-label="Message" placeholder={identity ? `Message ${identity.label}...` : 'Message...'} value={draft} maxLength={32000}
        onChange={event => onDraft(event.target.value)} rows={3} onKeyDown={event => {
          if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); onSend() }
        }} />
      <div className={styles.composerBottom}>
        <span className={styles.composerAgent}><span />{identity?.label ?? 'No agent selected'}</span>
        <button type="submit" className={styles.send} title="Send message" aria-label="Send message"
          disabled={!identity || !draft.trim() || state.running || state.resetting}><ArrowUp size={17} aria-hidden="true" />Send</button>
      </div>
    </form>
  </section>
}