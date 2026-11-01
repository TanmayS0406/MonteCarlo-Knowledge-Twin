import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { api, fmtDate, fmtDateTime, type Evidence } from '../api'
import { Confidence, ErrorBox, Loading, useLoad } from '../components/common'

export default function MeetingDetailPage() {
  const { id = '' } = useParams()
  const loc = useLocation()
  const { data, error, reload, setData } = useLoad(() => api.meeting(id), [id])
  const [focus, setFocus] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)

  // Deep link: /meetings/:id#t123 scrolls to the turn at 123 seconds.
  useEffect(() => {
    const m = /^#t(\d+)/.exec(loc.hash)
    if (m && data) jump(Number(m[1]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loc.hash, data?.meeting.id])

  function jump(seconds?: number) {
    if (seconds === undefined || !data) return
    const turn = [...data.turns].reverse().find((t) => t.offsetSeconds <= seconds) ?? data.turns[0]
    if (!turn) return
    setFocus(turn.sequence)
    document.getElementById(`turn-${turn.sequence}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }

  if (error) return <ErrorBox error={error} />
  if (!data) return <Loading />
  const { meeting, turns, decisions, actions, blockers, suggestions, extractions } = data

  const Ev = ({ e }: { e?: Evidence }) => e ? (
    <div className="quote" style={{ cursor: 'pointer' }} onClick={() => jump(e.offsetSeconds)}>
      “{e.quotedText}”<div className="meta">{e.speakerName} at {e.timestamp} · <Confidence value={e.confidence} /></div>
    </div>
  ) : null

  return (
    <>
      <div className="page-head">
        <div>
          <div className="small"><Link to="/meetings">← Meetings</Link></div>
          <h1>{meeting.title}</h1>
          <p>{fmtDateTime(meeting.startedAt)} · {meeting.source} · {turns.length} turns
            {extractions[0] && <> · extracted by {extractions[0].extractor} ({extractions[0].promptVersion}, {extractions[0].durationMs} ms)</>}</p>
        </div>
        <div className="row">
          <button className="btn" disabled={busy} onClick={async () => { setBusy(true); try { setData(await api.reprocess(id)) } finally { setBusy(false) } }}>
            {busy ? 'Re-extracting…' : 'Re-run extraction'}
          </button>
          <Link className="btn primary" to="/review">Review suggestions</Link>
        </div>
      </div>

      {meeting.optedOut && <div className="error">This meeting was opted out: the transcript is stored but no AI extraction ran. Use “Re-run extraction” to process it.</div>}

      <div className="grid g2">
        <div className="stack">
          <div className="card">
            <h2>Decisions ({decisions.length})</h2>
            {decisions.length === 0 ? <div className="muted">None found.</div> : decisions.map((d) => (
              <div key={d.id} className="stack" style={{ gap: 4, marginBottom: 12 }}>
                <div className="spread"><b>{d.statement}</b>{d.affectedWorkItemRef && <span className="badge info">{d.affectedWorkItemRef}</span>}</div>
                {d.rationale && <div className="small muted">Why: {d.rationale}</div>}
                <Ev e={d.evidence} />
              </div>
            ))}
          </div>

          <div className="card">
            <h2>Action items ({actions.length})</h2>
            {actions.length === 0 ? <div className="muted">None found.</div> : actions.map((a) => (
              <div key={a.id} className="stack" style={{ gap: 4, marginBottom: 12 }}>
                <div className="spread">
                  <b>{a.description}</b>
                  <span className="row" style={{ gap: 4 }}>
                    {a.linkedWorkItemRef ? <span className="badge info">{a.linkedWorkItemRef}</span> : <span className="badge warn">no ticket</span>}
                    <span className="badge">{a.state}</span>
                  </span>
                </div>
                <div className="small muted">Owner: {a.ownerName || 'unassigned'}{a.dueDate && <> · due {fmtDate(a.dueDate)}</>}</div>
                <Ev e={a.evidence} />
              </div>
            ))}
          </div>

          <div className="card">
            <h2>Blockers ({blockers.length})</h2>
            {blockers.length === 0 ? <div className="muted">None found.</div> : blockers.map((b) => (
              <div key={b.id} className="stack" style={{ gap: 4, marginBottom: 12 }}>
                <div className="spread"><b>{b.description}</b>{b.blockingWorkItemRef && <span className="badge bad">{b.blockingWorkItemRef}</span>}</div>
                <Ev e={b.evidence} />
              </div>
            ))}
          </div>

          {suggestions.length > 0 && (
            <div className="card">
              <h2>Suggestions from this meeting</h2>
              {suggestions.map((s) => (
                <div key={s.id} className="spread" style={{ padding: '4px 0' }}>
                  <span>{s.title}</span><span className={`badge ${s.outcome === 'Pending' ? 'info' : s.outcome === 'Rejected' ? 'bad' : 'ok'}`}>{s.outcome}</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="card" style={{ alignSelf: 'start', position: 'sticky', top: 16, maxHeight: 'calc(100vh - 40px)', overflow: 'auto' }}>
          <h2>Transcript</h2>
          {meeting.rawDeleted && <div className="muted">Raw transcript deleted by the retention policy. Extracted facts and quotes are kept.</div>}
          {turns.map((t) => (
            <div key={t.sequence} id={`turn-${t.sequence}`} className={`turn ${focus === t.sequence ? 'hit' : ''}`}>
              <span className="ts">{t.timestamp}</span>
              <span className="who">{t.speakerName}{!t.personId && <span className="muted small" title="Speaker not mapped to a team member"> ?</span>}</span>
              <span className="txt">{t.text}</span>
            </div>
          ))}
        </div>
      </div>
      <div className="mt"><button className="btn sm" onClick={reload}>Refresh</button></div>
    </>
  )
}
