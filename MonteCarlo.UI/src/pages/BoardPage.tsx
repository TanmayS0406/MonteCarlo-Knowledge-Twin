import { useState } from 'react'
import { ago, api, fmtDateTime, ROLES, STATUSES, statusLabel, type CodeEventType, type Person, type WorkItem, type WorkItemStatus } from '../api'
import { ErrorBox, EvidenceQuote, Loading, StatusBadge, useLoad } from '../components/common'

const COLUMNS: WorkItemStatus[] = ['Todo', 'InProgress', 'Blocked', 'InReview', 'Done', 'Backlog']

export default function BoardPage() {
  const items = useLoad(() => api.workItems())
  const people = useLoad(() => api.people())
  const code = useLoad(() => api.codeEvents())
  const [release, setRelease] = useState('all')
  const [selected, setSelected] = useState<WorkItem | 'new' | null>(null)

  const releases = Array.from(new Set((items.data ?? []).map((w) => w.release))).sort()
  const visible = (items.data ?? []).filter((w) => release === 'all' || w.release === release)

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Board</h1>
          <p>Tickets kept honest by conversations and code. Amber edge = stale.</p>
        </div>
        <div className="row">
          <select value={release} onChange={(e) => setRelease(e.target.value)}>
            <option value="all">All releases</option>
            {releases.map((r) => <option key={r}>{r}</option>)}
          </select>
          <button className="btn primary" onClick={() => setSelected('new')}>New ticket</button>
        </div>
      </div>
      <ErrorBox error={items.error} />
      {!items.data ? <Loading /> : (
        <div className="board">
          {COLUMNS.map((col) => {
            const list = visible.filter((w) => w.status === col || (col === 'Done' && w.status === 'Cancelled'))
            return (
              <div className="column" key={col}>
                <h3>{statusLabel(col)} <span className="muted">{list.length}</span></h3>
                {list.map((w) => (
                  <div key={w.id} className={`ticket ${w.isStale ? 'stale' : ''}`} onClick={() => setSelected(w)}>
                    <div className="spread small"><b>{w.externalRef}</b><span className="muted">P{w.priority} · {w.release}</span></div>
                    <div className="title">{w.title}</div>
                    <div className="spread small muted">
                      <span>{w.ownerName ?? 'No owner'}</span>
                      <span>{w.estimateDays ? `${w.estimateDays}d` : 'no est.'}</span>
                    </div>
                    <div className="row small" style={{ marginTop: 6, gap: 4 }}>
                      <span className="badge">{w.role}</span>
                      {w.status === 'Cancelled' && <StatusBadge status="Cancelled" />}
                      {w.pendingSuggestions > 0 && <span className="badge info">{w.pendingSuggestions} suggestion{w.pendingSuggestions > 1 ? 's' : ''}</span>}
                      {w.qualityScore < 60 && <span className="badge warn" title={w.qualityIssues.join(', ')}>Q {w.qualityScore}</span>}
                    </div>
                  </div>
                ))}
              </div>
            )
          })}
        </div>
      )}

      <div className="grid g2 mt">
        <CodeActivity events={code.data} reload={() => { code.reload(); items.reload() }} />
      </div>

      {selected && (
        <TicketEditor
          item={selected === 'new' ? null : selected}
          all={items.data ?? []}
          people={people.data ?? []}
          onClose={() => setSelected(null)}
          onSaved={() => { setSelected(null); items.reload() }}
        />
      )}
    </>
  )
}

function CodeActivity({ events, reload }: { events: import('../api').CodeEvent[] | null; reload: () => void }) {
  const [form, setForm] = useState({ type: 'PullRequestMerged' as CodeEventType, title: '', author: '', branch: '' })
  const [busy, setBusy] = useState(false)
  return (
    <>
      <div className="card">
        <h2>Code activity</h2>
        <p className="muted small" style={{ marginTop: -6 }}>From GitHub webhooks (/api/webhooks/github) or added here. Ticket keys in titles and branches link events to tickets.</p>
        {!events ? <Loading /> : events.length === 0 ? <div className="empty">No code events yet.</div> : (
          <table>
            <tbody>
              {events.slice(0, 12).map((e) => (
                <tr key={e.id}>
                  <td><span className="badge">{e.type}</span></td>
                  <td>{e.title}<div className="muted small">{e.author} · {ago(e.occurredAt)}</div></td>
                  <td>{e.ticketRefs.map((r) => <span key={r} className="badge info">{r}</span>)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      <div className="card stack">
        <h2>Simulate a code event</h2>
        <label className="field">Type<select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as CodeEventType })}>
          {['Commit', 'PullRequestOpened', 'PullRequestMerged', 'BuildFailed', 'BuildSucceeded', 'Deployed'].map((t) => <option key={t}>{t}</option>)}
        </select></label>
        <label className="field">Title (include a ticket key, e.g. GST-143)<input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} /></label>
        <div className="grid g2">
          <label className="field">Author<input value={form.author} onChange={(e) => setForm({ ...form, author: e.target.value })} /></label>
          <label className="field">Branch<input value={form.branch} onChange={(e) => setForm({ ...form, branch: e.target.value })} /></label>
        </div>
        <button className="btn primary" disabled={busy || !form.title} onClick={async () => {
          setBusy(true)
          try { await api.addCodeEvent(form); setForm({ ...form, title: '' }); reload() } finally { setBusy(false) }
        }}>Add event & reconcile</button>
      </div>
    </>
  )
}

function TicketEditor({ item, all, people, onClose, onSaved }: { item: WorkItem | null; all: WorkItem[]; people: Person[]; onClose: () => void; onSaved: () => void }) {
  const [f, setF] = useState({
    externalRef: item?.externalRef ?? '',
    title: item?.title ?? '',
    description: item?.description ?? '',
    acceptanceCriteria: item?.acceptanceCriteria ?? '',
    status: item?.status ?? 'Todo',
    role: item?.role ?? 'Backend',
    estimateDays: item?.estimateDays?.toString() ?? '',
    ownerId: item?.ownerId ?? '',
    release: item?.release ?? 'v1',
    priority: item?.priority ?? 3,
    dependsOn: item?.dependsOn ?? [],
  })
  const [err, setErr] = useState<string | null>(null)
  const detail = useLoad(() => (item ? api.workItem(item.id) : Promise.resolve(null)), [item?.id])

  async function save() {
    setErr(null)
    const body = { ...f, estimateDays: f.estimateDays === '' ? null : Number(f.estimateDays), ownerId: f.ownerId || null, externalRef: f.externalRef || null }
    try {
      if (item) await api.updateWorkItem(item.id, body)
      else await api.createWorkItem(body)
      onSaved()
    } catch (e) { setErr((e as Error).message) }
  }

  return (
    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,.4)', display: 'flex', justifyContent: 'flex-end', zIndex: 50 }} onClick={onClose}>
      <div className="card stack" style={{ width: 'min(560px, 100%)', height: '100%', overflow: 'auto', borderRadius: 0 }} onClick={(e) => e.stopPropagation()}>
        <div className="spread"><h2 style={{ margin: 0 }}>{item ? `${item.externalRef} ${item.title}` : 'New ticket'}</h2><button className="btn sm" onClick={onClose}>Close</button></div>
        <ErrorBox error={err} />
        {item && item.qualityIssues.length > 0 && <div className="small"><span className="badge warn">Quality {item.qualityScore}</span> {item.qualityIssues.join(' · ')}</div>}
        <div className="grid g2">
          <label className="field">Key (blank = auto)<input value={f.externalRef} disabled={!!item} onChange={(e) => setF({ ...f, externalRef: e.target.value })} /></label>
          <label className="field">Release<input value={f.release} onChange={(e) => setF({ ...f, release: e.target.value })} /></label>
        </div>
        <label className="field">Title<input value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></label>
        <label className="field">Description<textarea rows={3} value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></label>
        <label className="field">Acceptance criteria<textarea rows={2} value={f.acceptanceCriteria} onChange={(e) => setF({ ...f, acceptanceCriteria: e.target.value })} /></label>
        <div className="grid g3">
          <label className="field">Status<select value={f.status} onChange={(e) => setF({ ...f, status: e.target.value as WorkItemStatus })}>{STATUSES.map((s) => <option key={s} value={s}>{statusLabel(s)}</option>)}</select></label>
          <label className="field">Role<select value={f.role} onChange={(e) => setF({ ...f, role: e.target.value as WorkItem['role'] })}>{ROLES.map((r) => <option key={r}>{r}</option>)}</select></label>
          <label className="field">Priority<select value={f.priority} onChange={(e) => setF({ ...f, priority: Number(e.target.value) })}>{[1, 2, 3, 4, 5].map((p) => <option key={p} value={p}>P{p}</option>)}</select></label>
        </div>
        <div className="grid g2">
          <label className="field">Owner<select value={f.ownerId} onChange={(e) => setF({ ...f, ownerId: e.target.value })}><option value="">—</option>{people.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
          <label className="field">Estimate (days)<input type="number" step="0.5" value={f.estimateDays} onChange={(e) => setF({ ...f, estimateDays: e.target.value })} /></label>
        </div>
        <label className="field">Depends on
          <select multiple size={6} value={f.dependsOn} onChange={(e) => setF({ ...f, dependsOn: Array.from(e.target.selectedOptions).map((o) => o.value) })}>
            {all.filter((w) => w.id !== item?.id).map((w) => <option key={w.id} value={w.id}>{w.externalRef} {w.title}</option>)}
          </select>
        </label>
        <div className="row">
          <button className="btn primary" onClick={save}>Save</button>
          {item && <button className="btn danger" onClick={async () => { if (confirm('Delete ticket?')) { await api.deleteWorkItem(item.id); onSaved() } }}>Delete</button>}
        </div>

        {detail.data && detail.data.timeline.length > 0 && (
          <div className="stack mt">
            <h3>Why it looks like this</h3>
            {detail.data.timeline.map((t, i) => (
              <div key={i} className="stack" style={{ gap: 4 }}>
                <div className="small"><span className="badge">{t.kind}</span> <b>{t.text}</b> <span className="muted">{t.source} · {fmtDateTime(t.at)}</span></div>
                <EvidenceQuote evidence={t.evidence} />
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
