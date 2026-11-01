import { useState } from 'react'
import { api, fmtDate, type SearchResult } from '../api'
import { ErrorBox, EvidenceQuote } from '../components/common'

const examples = ['Why did we drop Excel export?', 'vendor follow-up emails', 'API Setu', 'GSTN timeout', 'who owns the mismatch report?']

export default function SearchPage() {
  const [q, setQ] = useState('')
  const [res, setRes] = useState<SearchResult | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function run(query = q) {
    if (!query.trim()) return
    setQ(query); setBusy(true); setErr(null)
    try { setRes(await api.search(query)) } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Why was this decided?</h1>
          <p>Search every decision, action, blocker and transcript line. Answers cite their source.</p>
        </div>
      </div>
      <div className="card stack">
        <form className="row" onSubmit={(e) => { e.preventDefault(); run() }}>
          <input style={{ flex: 1, fontSize: 15, padding: '10px 12px' }} value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ask about a decision, a ticket or a person…" autoFocus />
          <button className="btn primary" disabled={busy}>{busy ? 'Searching…' : 'Search'}</button>
        </form>
        <div className="row small">{examples.map((x) => <button key={x} className="btn sm" onClick={() => run(x)}>{x}</button>)}</div>
      </div>
      <ErrorBox error={err} />
      {res && (
        <>
          <div className="card mt" style={{ borderColor: 'var(--accent)' }}>
            <h3>Answer</h3>
            <div>{res.answer}</div>
          </div>
          <div className="stack mt">
            {res.hits.map((h) => (
              <div key={`${h.kind}-${h.id}`} className="card stack" style={{ gap: 6 }}>
                <div className="spread">
                  <div className="row" style={{ gap: 6 }}><span className="badge info">{h.kind}</span>{h.workItemRef && <span className="badge">{h.workItemRef}</span>}</div>
                  <span className="muted small">{h.who} · {fmtDate(h.at)} · score {h.score.toFixed(2)}</span>
                </div>
                <b>{h.text}</b>
                {h.kind !== 'Transcript' && <EvidenceQuote evidence={h.evidence} />}
                {h.kind === 'Transcript' && h.evidence && <EvidenceQuote evidence={{ ...h.evidence, quotedText: h.evidence.meetingTitle ?? '' }} />}
              </div>
            ))}
            {res.hits.length === 0 && <div className="empty">No matches.</div>}
          </div>
        </>
      )}
    </>
  )
}
