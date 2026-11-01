import { useEffect, useState } from 'react'
import { api, fmtDateTime, type Project, type Settings } from '../api'
import { ErrorBox, Loading, useLoad } from '../components/common'

export default function SettingsPage() {
  const settings = useLoad(() => api.settings())
  const projects = useLoad(() => api.projects())
  const audit = useLoad(() => api.audit())
  const [s, setS] = useState<Settings | null>(null)
  const [p, setP] = useState<Project | null>(null)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => { if (settings.data) setS(settings.data) }, [settings.data])
  useEffect(() => { if (projects.data?.[0]) setP(projects.data[0]) }, [projects.data])

  if (!s) return <Loading />

  async function saveSettings() {
    setErr(null)
    try { setS(await api.updateSettings(s!)); setMsg('Settings saved.'); audit.reload() } catch (e) { setErr((e as Error).message) }
  }
  async function saveProject() {
    if (!p) return
    setErr(null)
    try { setP(await api.updateProject(p.id, p)); setMsg('Project saved.'); audit.reload() } catch (e) { setErr((e as Error).message) }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Privacy & settings</h1>
          <p>Consent and minimal data by default. Raw transcripts are deleted after the retention period; extracted facts and short quotes stay.</p>
        </div>
      </div>
      <ErrorBox error={err} />
      {msg && <div className="card small" style={{ marginBottom: 12 }}>{msg}</div>}

      <div className="grid g2">
        <div className="card stack">
          <h2>Capture & retention</h2>
          <label className="field">Team name<input value={s.teamName} onChange={(e) => setS({ ...s, teamName: e.target.value })} /></label>
          <div className="grid g2">
            <label className="field">Delete raw transcripts after (days)<input type="number" min={1} value={s.retentionDays} onChange={(e) => setS({ ...s, retentionDays: Number(e.target.value) })} /></label>
            <label className="field">Ticket is stale after (days)<input type="number" min={1} value={s.staleAfterDays} onChange={(e) => setS({ ...s, staleAfterDays: Number(e.target.value) })} /></label>
          </div>
          <div className="grid g2">
            <label className="field">Data region<select value={s.dataRegion} onChange={(e) => setS({ ...s, dataRegion: e.target.value })}>
              {['Central India', 'South India', 'West Europe', 'East US'].map((r) => <option key={r}>{r}</option>)}
            </select></label>
            <label className="field">Min. extraction confidence<input type="number" min={0} max={1} step={0.05} value={s.minConfidence} onChange={(e) => setS({ ...s, minConfidence: Number(e.target.value) })} /></label>
          </div>
          <label className="row small"><input type="checkbox" checked={s.captureEnabledByDefault} onChange={(e) => setS({ ...s, captureEnabledByDefault: e.target.checked })} /> Capture Teams meetings by default (meetings can still opt out)</label>
          <div className="row">
            <button className="btn primary" onClick={saveSettings}>Save</button>
            <button className="btn" onClick={async () => { const r = await api.purge(); setMsg(`Retention run: raw transcripts deleted for ${r.purged} meeting(s).`); audit.reload() }}>Run retention now</button>
          </div>
        </div>

        <div className="stack">
          {p && (
            <div className="card stack">
              <h2>Project</h2>
              <div className="grid g2">
                <label className="field">Key<input value={p.key} onChange={(e) => setP({ ...p, key: e.target.value })} /></label>
                <label className="field">Current release<input value={p.currentRelease} onChange={(e) => setP({ ...p, currentRelease: e.target.value })} /></label>
              </div>
              <label className="field">Name<input value={p.name} onChange={(e) => setP({ ...p, name: e.target.value })} /></label>
              <div className="grid g2">
                <label className="field">Start<input type="date" value={p.startDate} onChange={(e) => setP({ ...p, startDate: e.target.value })} /></label>
                <label className="field">Deadline<input type="date" value={p.deadline} onChange={(e) => setP({ ...p, deadline: e.target.value })} /></label>
              </div>
              <div><button className="btn primary" onClick={saveProject}>Save project</button></div>
            </div>
          )}
          <div className="card">
            <h2>Connections</h2>
            <table><tbody>
              <tr><td>AI extraction</td><td><b>{s.extractor}</b></td></tr>
              <tr><td>Ticket system</td><td><b>{s.ticketSystem}</b></td></tr>
              <tr><td>Code activity</td><td><b>{s.codeSource}</b></td></tr>
              <tr><td>Teams (Graph)</td><td>{s.graphConfigured ? <span className="badge ok">Connected</span> : <span className="badge">Not configured. Using manual upload</span>}</td></tr>
            </tbody></table>
            <div className="muted small mt">Configure in MonteCarlo.Backend/src/MonteCarlo.Api/appsettings.json (Llm, Jira, GitHub, Graph sections).</div>
          </div>
        </div>
      </div>

      <div className="card mt">
        <h2>Audit log</h2>
        <p className="muted small" style={{ marginTop: -6 }}>Who changed what, including every change the AI proposed and a human accepted.</p>
        {!audit.data ? <Loading /> : (
          <table>
            <thead><tr><th>When</th><th>Actor</th><th>Action</th><th>Details</th></tr></thead>
            <tbody>
              {audit.data.map((a, i) => (
                <tr key={i}><td className="small muted">{fmtDateTime(a.at)}</td><td>{a.actor}</td><td><span className="badge">{a.action}</span></td><td className="small">{a.details}</td></tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </>
  )
}
