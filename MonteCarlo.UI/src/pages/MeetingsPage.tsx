import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, fmtDateTime } from '../api'
import { ErrorBox, Loading, useLoad } from '../components/common'

export default function MeetingsPage() {
  const nav = useNavigate()
  const { data, error, reload } = useLoad(() => api.meetings())
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [optOut, setOptOut] = useState(false)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  async function submit() {
    setBusy(true); setErr(null)
    try {
      const m = await api.ingest({ title: title || 'Meeting', content, optOut })
      nav(`/meetings/${m.meeting.id}`)
    } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }

  async function onFile(f?: File) {
    if (!f) return
    setContent(await f.text())
    if (!title) setTitle(f.name.replace(/\.(vtt|txt|docx?)$/i, ''))
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Meetings</h1>
          <p>Transcripts from Teams (via Graph webhook) or uploaded by hand. Every extracted item links back to the moment it was said.</p>
        </div>
      </div>

      <div className="grid g2">
        <div className="card stack">
          <h2>Add a transcript</h2>
          <ErrorBox error={err} />
          <label className="field">Title<input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Sprint call" /></label>
          <label className="field">
            Transcript (Teams .vtt, or lines like “[00:01:10] Name: text”)
            <textarea rows={12} value={content} onChange={(e) => setContent(e.target.value)} placeholder="[00:00:05] Rohan Mehta: I'll fix the GSTN timeout by Friday." />
          </label>
          <div className="spread">
            <div className="row">
              <label className="btn sm">Upload file<input type="file" accept=".vtt,.txt" hidden onChange={(e) => onFile(e.target.files?.[0])} /></label>
              <button className="btn sm" onClick={async () => { setContent(await api.sampleTranscript()); setTitle('Sample sprint call') }}>Load sample</button>
              <label className="row small muted" style={{ gap: 6 }}>
                <input type="checkbox" checked={optOut} onChange={(e) => setOptOut(e.target.checked)} /> Opt out (store only, no AI)
              </label>
            </div>
            <button className="btn primary" disabled={busy || !content.trim()} onClick={submit}>{busy ? 'Extracting…' : 'Ingest & extract'}</button>
          </div>
        </div>

        <div className="card">
          <h2>Captured meetings</h2>
          <ErrorBox error={error} />
          {!data ? <Loading /> : data.length === 0 ? <div className="empty">No meetings yet.</div> : (
            <table>
              <thead><tr><th>Meeting</th><th>Decisions</th><th>Actions</th><th>Blockers</th><th></th></tr></thead>
              <tbody>
                {data.map((m) => (
                  <tr key={m.id}>
                    <td>
                      <Link to={`/meetings/${m.id}`}><b>{m.title}</b></Link>
                      <div className="muted small">
                        {fmtDateTime(m.startedAt)} · {m.source} · {m.turns} turns
                        {m.optedOut && <> · <span className="badge warn">opted out</span></>}
                        {m.rawDeleted && <> · <span className="badge">raw deleted</span></>}
                      </div>
                    </td>
                    <td>{m.decisions}</td><td>{m.actions}</td><td>{m.blockers}</td>
                    <td><button className="btn sm danger" onClick={async () => { if (confirm('Delete this meeting and its extracted items?')) { await api.deleteMeeting(m.id); reload() } }}>Delete</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </>
  )
}
