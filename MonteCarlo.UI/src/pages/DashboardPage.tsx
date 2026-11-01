import { Link } from 'react-router-dom'
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { ago, api, fmtDate, fmtShort, pct, statusLabel } from '../api'
import { ErrorBox, EvidenceQuote, Loading, Stat, StatusBadge, useLoad } from '../components/common'

const statusColor: Record<string, string> = {
  Backlog: '#94a3b8', Todo: '#64748b', InProgress: '#3b82f6', Blocked: '#ef4444', InReview: '#f59e0b', Done: '#22c55e', Cancelled: '#a1a1aa',
}

export default function DashboardPage() {
  const { data, error } = useLoad(() => api.dashboard())
  const digest = useLoad(() => api.digest())
  if (error) return <ErrorBox error={error} />
  if (!data) return <Loading />
  const d = data

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{d.project.name}</h1>
          <p>{d.project.key} · release {d.project.currentRelease} · deadline {fmtDate(d.project.deadline)}</p>
        </div>
        <div className="row">
          <Link className="btn" to="/meetings">Add meeting transcript</Link>
          <Link className="btn primary" to="/review">Review {d.pendingSuggestions} suggestions</Link>
        </div>
      </div>

      <div className="grid g4">
        <Stat label="Open tickets" value={d.openItems} hint={`${d.doneItems} done · ${d.blockedItems} blocked`} />
        <Stat label="Ticket freshness" value={pct(d.ticketFreshness)} hint="Open tickets updated in the last 7 days" tone={d.ticketFreshness < 0.6 ? 'warn' : 'ok'} />
        <Stat label="Suggestion precision" value={pct(d.suggestionPrecision)} hint="Accepted / (accepted + rejected). Target 80%" />
        <Stat label="Captured" value={d.actionItemsCaptured + d.decisionsCaptured} hint={`${d.decisionsCaptured} decisions · ${d.actionItemsCaptured} actions · ${d.meetingsProcessed} meetings`} />
      </div>

      <div className="grid g2 mt">
        <div className="card">
          <div className="spread"><h2>Forecast</h2><Link to="/simulator">Open simulator →</Link></div>
          {d.latestForecast ? (
            <div className="stack">
              <div className="grid g3">
                <div><div className="muted small">P50</div><b>{fmtShort(d.latestForecast.scenarioP50)}</b></div>
                <div><div className="muted small">P80</div><b>{fmtShort(d.latestForecast.scenarioP80)}</b></div>
                <div><div className="muted small">On time</div><b>{pct(d.latestForecast.scenarioOnTime)}</b></div>
              </div>
              <div className="muted small">{d.latestForecast.name} · {ago(d.latestForecast.createdAt)}</div>
              <div className="small">{d.latestForecast.narrative}</div>
            </div>
          ) : <div className="empty">No forecast yet. <Link to="/simulator">Run the simulator</Link> to get P50/P80 dates.</div>}
        </div>

        <div className="card">
          <h2>Board by status</h2>
          <div style={{ height: 190 }}>
            <ResponsiveContainer>
              <BarChart data={d.statusCounts.map((s) => ({ ...s, label: statusLabel(s.status) }))} margin={{ left: -20, right: 8 }}>
                <XAxis dataKey="label" tick={{ fontSize: 11, fill: 'var(--muted)' }} interval={0} />
                <YAxis allowDecimals={false} tick={{ fontSize: 11, fill: 'var(--muted)' }} />
                <Tooltip cursor={{ fill: 'rgba(127,127,127,.08)' }} contentStyle={{ background: 'var(--panel)', border: '1px solid var(--border)' }} />
                <Bar dataKey="count" radius={[4, 4, 0, 0]}>
                  {d.statusCounts.map((s) => <Cell key={s.status} fill={statusColor[s.status]} />)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      <div className="grid g2 mt">
        <div className="card">
          <h2>Stale tickets</h2>
          {d.staleItems.length === 0 ? <div className="empty">Nothing stale. The board matches reality.</div> : (
            <table>
              <tbody>
                {d.staleItems.map((w) => (
                  <tr key={w.id}>
                    <td><b>{w.externalRef}</b> {w.title}<div className="muted small">{w.ownerName ?? 'No owner'} · last activity {ago(w.lastActivityAt)}</div></td>
                    <td style={{ textAlign: 'right' }}><StatusBadge status={w.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="card">
          <h2>Open blockers</h2>
          {d.blockers.length === 0 ? <div className="empty">No open blockers.</div> : (
            <div className="stack">
              {d.blockers.map((b) => (
                <div key={b.id}>
                  <div><b>{b.blockingWorkItemRef ?? 'Unlinked'}</b> · {b.description}</div>
                  <EvidenceQuote evidence={b.evidence} />
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="grid g2 mt">
        <div className="card">
          <div className="spread"><h2>Recent decisions</h2><Link to="/search">Search history →</Link></div>
          {d.recentDecisions.length === 0 ? <div className="empty">No decisions captured yet.</div> : (
            <div className="stack">
              {d.recentDecisions.map((dec) => (
                <div key={dec.id}>
                  <div className="spread"><b>{dec.statement}</b>{dec.affectedWorkItemRef && <span className="badge info">{dec.affectedWorkItemRef}</span>}</div>
                  <div className="muted small">{dec.decidedByName} · {fmtDate(dec.decidedAt)}</div>
                  <EvidenceQuote evidence={dec.evidence} />
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="card">
          <h2>Ticket quality</h2>
          {d.lowQualityItems.length === 0 ? <div className="empty">All open tickets have owner, estimate and criteria.</div> : (
            <table>
              <thead><tr><th>Ticket</th><th>Issues</th><th>Score</th></tr></thead>
              <tbody>
                {d.lowQualityItems.map((w) => (
                  <tr key={w.id}>
                    <td><b>{w.externalRef}</b> {w.title}</td>
                    <td className="small muted">{w.qualityIssues.join(', ')}</td>
                    <td><span className={`badge ${w.qualityScore < 40 ? 'bad' : 'warn'}`}>{w.qualityScore}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {digest.data && (
        <div className="card mt">
          <h2>Weekly digest</h2>
          <pre className="mono" style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{digest.data.text}</pre>
        </div>
      )}
    </>
  )
}
