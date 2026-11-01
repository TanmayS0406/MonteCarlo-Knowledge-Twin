import { useState } from 'react'
import { api, fmtDateTime, ROLES, STATUSES, statusLabel, type Person, type Suggestion, type SuggestionOutcome } from '../api'
import { Confidence, ErrorBox, EvidenceQuote, Loading, StatusBadge, useLoad } from '../components/common'

const typeLabel: Record<string, string> = {
  CreateTicket: 'New ticket', UpdateStatus: 'Status change', CloseTicket: 'Close ticket', MoveRelease: 'Scope change',
  AssignOwner: 'Owner', AddEstimate: 'Estimate', StaleTicket: 'Stale ticket', LinkConversation: 'Link to ticket',
}

export default function ReviewPage({ onChange }: { onChange: () => void }) {
  const [tab, setTab] = useState<SuggestionOutcome | 'History'>('Pending')
  const list = useLoad(() => (tab === 'Pending' ? api.suggestions('Pending') : api.suggestions()), [tab])
  const people = useLoad(() => api.people())
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  async function reconcile() {
    setBusy(true)
    try {
      const r = await api.reconcile()
      setMsg(r.created === 0 ? 'Talk, tickets and code agree. No new suggestions.' : `${r.created} new suggestion(s).`)
      list.reload(); onChange()
    } finally { setBusy(false) }
  }

  const items = (list.data ?? []).filter((s) => (tab === 'Pending' ? s.outcome === 'Pending' : s.outcome !== 'Pending'))

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Review</h1>
          <p>Differences between what was said, the board and the code. Nothing changes until someone taps Accept.</p>
        </div>
        <button className="btn" disabled={busy} onClick={reconcile}>{busy ? 'Reconciling…' : 'Run reconciliation'}</button>
      </div>
      {msg && <div className="card small" style={{ marginBottom: 12 }}>{msg}</div>}

      <div className="tabs">
        <button className={tab === 'Pending' ? 'active' : ''} onClick={() => setTab('Pending')}>Pending</button>
        <button className={tab === 'History' ? 'active' : ''} onClick={() => setTab('History')}>History</button>
      </div>

      <ErrorBox error={list.error} />
      {!list.data ? <Loading /> : items.length === 0 ? <div className="empty">{tab === 'Pending' ? 'All caught up.' : 'No reviewed suggestions yet.'}</div> : (
        <div className="grid g2">
          {items.map((s) => (
            <SuggestionCard key={s.id} s={s} people={people.data ?? []} onDone={() => { list.reload(); onChange() }} />
          ))}
        </div>
      )}
    </>
  )
}

function SuggestionCard({ s, people, onDone }: { s: Suggestion; people: Person[]; onDone: () => void }) {
  const [editing, setEditing] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [edit, setEdit] = useState({
    proposedTitle: s.proposedTitle ?? '',
    proposedStatus: s.proposedStatus ?? '',
    proposedOwnerId: s.proposedOwnerId ?? '',
    proposedDueDate: s.proposedDueDate ?? '',
    proposedRelease: s.proposedRelease ?? '',
    proposedEstimateDays: s.proposedEstimateDays?.toString() ?? '',
    proposedRole: s.proposedRole ?? '',
  })

  async function act(kind: 'accept' | 'reject') {
    setBusy(true); setErr(null)
    try {
      if (kind === 'reject') {
        const note = prompt('Why is this wrong? (helps tune extraction)') ?? undefined
        await api.reject(s.id, note)
      } else {
        const body: Record<string, unknown> = {}
        if (editing) {
          for (const [k, v] of Object.entries(edit)) if (v !== '') body[k] = k === 'proposedEstimateDays' ? Number(v) : v
        }
        await api.accept(s.id, body)
      }
      onDone()
    } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }

  const set = (k: keyof typeof edit) => (e: { target: { value: string } }) => setEdit({ ...edit, [k]: e.target.value })
  const pending = s.outcome === 'Pending'

  return (
    <div className="sugg">
      <div className="spread">
        <span className="badge info">{typeLabel[s.type] ?? s.type}</span>
        <span className="row" style={{ gap: 6 }}><Confidence value={s.confidence} />{!pending && <span className={`badge ${s.outcome === 'Rejected' ? 'bad' : 'ok'}`}>{s.outcome}</span>}</span>
      </div>
      <div className="title">{s.title}</div>
      <div className="muted">{s.detail}</div>
      {s.workItemRef && (
        <div className="small row">
          <b>{s.workItemRef}</b> {s.workItemTitle} <StatusBadge status={s.currentStatus} />
          {s.proposedStatus && <>→ <StatusBadge status={s.proposedStatus} /></>}
          {s.proposedRelease && s.type === 'MoveRelease' && <>→ <span className="badge">{s.proposedRelease}</span></>}
        </div>
      )}
      <EvidenceQuote evidence={s.evidence} />

      {editing && pending && (
        <div className="edit">
          {s.type === 'CreateTicket' && <label className="field">Title<input value={edit.proposedTitle} onChange={set('proposedTitle')} /></label>}
          {['UpdateStatus', 'CloseTicket', 'StaleTicket'].includes(s.type) && (
            <label className="field">Status<select value={edit.proposedStatus} onChange={set('proposedStatus')}>
              {STATUSES.map((x) => <option key={x} value={x}>{statusLabel(x)}</option>)}
            </select></label>
          )}
          {['CreateTicket', 'AssignOwner', 'LinkConversation'].includes(s.type) && (
            <label className="field">Owner<select value={edit.proposedOwnerId} onChange={set('proposedOwnerId')}>
              <option value="">—</option>{people.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select></label>
          )}
          {s.type === 'CreateTicket' && <>
            <label className="field">Due<input type="date" value={edit.proposedDueDate} onChange={set('proposedDueDate')} /></label>
            <label className="field">Role<select value={edit.proposedRole} onChange={set('proposedRole')}>
              <option value="">—</option>{ROLES.map((r) => <option key={r}>{r}</option>)}
            </select></label>
          </>}
          {['CreateTicket', 'AddEstimate'].includes(s.type) && <label className="field">Estimate (days)<input type="number" step="0.5" value={edit.proposedEstimateDays} onChange={set('proposedEstimateDays')} /></label>}
          {['CreateTicket', 'MoveRelease'].includes(s.type) && <label className="field">Release<input value={edit.proposedRelease} onChange={set('proposedRelease')} /></label>}
        </div>
      )}

      <ErrorBox error={err} />
      {pending ? (
        <div className="row">
          <button className="btn ok" disabled={busy} onClick={() => act('accept')}>{editing ? 'Save & accept' : 'Accept'}</button>
          <button className="btn" disabled={busy} onClick={() => setEditing(!editing)}>{editing ? 'Cancel edit' : 'Edit'}</button>
          <button className="btn danger" disabled={busy} onClick={() => act('reject')}>Reject</button>
          {s.proposedOwnerName && <span className="muted small">Owner: {s.proposedOwnerName}</span>}
        </div>
      ) : (
        <div className="muted small">{s.outcome} by {s.reviewedBy} · {fmtDateTime(s.reviewedAt)}{s.reviewNote && <> · “{s.reviewNote}”</>}</div>
      )}
    </div>
  )
}
