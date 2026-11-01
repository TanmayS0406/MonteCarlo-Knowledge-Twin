import { useEffect, useRef, useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, fmtDate, fmtShort, pct, ROLES, type ScenarioChange, type SimModel, type SimResult } from '../api'
import { ErrorBox, Loading, useLoad } from '../components/common'
import Sim3D, { ROLE_COLOR, STATE_COLOR } from '../components/Sim3D'

const TYPES: { value: ScenarioChange['type']; label: string }[] = [
  { value: 'PersonLeaves', label: 'Person leaves' },
  { value: 'NewHire', label: 'New hire joins' },
  { value: 'ScopeChange', label: 'Scope change' },
  { value: 'RemoveTask', label: 'Drop a task' },
  { value: 'PrioritySwap', label: 'Priority swap' },
  { value: 'MeetingLoad', label: 'Meeting load change' },
  { value: 'ClientDelay', label: 'Client delay' },
]

function defaults(type: ScenarioChange['type'], m: SimModel): ScenarioChange {
  const firstDev = m.people.find((p) => p.role === 'Backend') ?? m.people[0]
  const firstTask = m.tasks[0]
  switch (type) {
    case 'PersonLeaves': return { type, personId: firstDev?.id, day: 5, knowledgeLoss: 0.3 }
    case 'NewHire': return { type, name: 'New backend dev', role: 'Backend', day: 5, capacity: 1, rampUpDays: 15 }
    case 'ScopeChange': return { type, role: 'Backend', addTasks: 2, effortDaysEach: 4, effortMultiplier: 1 }
    case 'RemoveTask': return { type, taskId: firstTask?.id }
    case 'PrioritySwap': return { type, taskId: firstTask?.id, newPriority: 1 }
    case 'MeetingLoad': return { type, capacityDelta: -0.1 }
    case 'ClientDelay': return { type, taskId: firstTask?.id, untilDay: 10 }
  }
}

export default function SimulatorPage() {
  const model = useLoad(() => api.simModel())
  const runs = useLoad(() => api.simRuns())
  const [changes, setChanges] = useState<ScenarioChange[]>([])
  const [name, setName] = useState('')
  const [iterations, setIterations] = useState(1000)
  const [absence, setAbsence] = useState(0.03)
  const [mitigate, setMitigate] = useState(true)
  const [result, setResult] = useState<SimResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [frame, setFrame] = useState(0)
  const [playing, setPlaying] = useState(false)
  const timer = useRef<number | null>(null)

  // Open the latest run, or run a baseline the first time.
  useEffect(() => {
    if (!runs.data || result) return
    if (runs.data.length > 0) api.simRun(runs.data[0].id).then(show).catch(() => undefined)
    else if (model.data) run([])
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runs.data, model.data])

  useEffect(() => {
    if (!playing || !result) return
    timer.current = window.setInterval(() => {
      setFrame((f) => {
        if (f >= result.comparison.replay.frames.length - 1) { setPlaying(false); return f }
        return f + 1
      })
    }, 280)
    return () => { if (timer.current) window.clearInterval(timer.current) }
  }, [playing, result])

  function show(r: SimResult) {
    setResult(r)
    setFrame(0)
    setPlaying(true)
  }

  async function run(list = changes) {
    setBusy(true); setErr(null)
    try {
      const r = await api.runSim({ name: name || undefined, changes: list, iterations, absenceRate: absence, autoMitigate: mitigate })
      show(r)
      runs.reload()
    } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }

  const m = model.data
  if (model.error) return <ErrorBox error={model.error} />
  if (!m) return <Loading what="Loading project model" />

  const update = (i: number, patch: Partial<ScenarioChange>) => setChanges(changes.map((c, k) => (k === i ? { ...c, ...patch } : c)))
  const presets: { label: string; build: () => ScenarioChange[] }[] = [
    {
      label: 'Two developers leave', build: () => m.people.filter((p) => p.role === 'Backend').slice(0, 2)
        .map((p, i) => ({ type: 'PersonLeaves', personId: p.id, day: 5 + i * 5, knowledgeLoss: 0.3 }) as ScenarioChange),
    },
    { label: 'Client scope +20%', build: () => [{ type: 'ScopeChange', effortMultiplier: 1.2, addTasks: 0, effortDaysEach: 0 }] },
    { label: 'Hire a contractor', build: () => [{ type: 'NewHire', name: 'Contractor', role: 'Backend', day: 0, capacity: 1, rampUpDays: 10 }] },
    { label: 'More meetings', build: () => [{ type: 'MeetingLoad', capacityDelta: -0.15 }] },
  ]

  return (
    <>
      <div className="page-head">
        <div>
          <h1>What-if simulator</h1>
          <p>
            {m.projectName} · {m.tasks.length} open tasks in {m.release} · {m.people.length} people · deadline {fmtDate(m.deadline)} ·
            history calibration: actual = {m.calibration.meanRatio.toFixed(2)}× estimate ({m.calibration.samples} done tickets)
          </p>
        </div>
      </div>

      <div className="sim-layout">
        {/* ---- Scenario builder ---- */}
        <div className="stack">
          <div className="card stack">
            <h2>Scenario</h2>
            <label className="field">Name<input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Rohan leaves mid-sprint" /></label>
            <div className="row small">{presets.map((p) => <button key={p.label} className="btn sm" onClick={() => { setChanges(p.build()); setName(p.label) }}>{p.label}</button>)}</div>

            {changes.length === 0 && <div className="muted small">No changes: running gives the baseline forecast.</div>}
            {changes.map((c, i) => (
              <div className="change" key={i}>
                <div className="spread">
                  <select value={c.type} onChange={(e) => update(i, defaults(e.target.value as ScenarioChange['type'], m))}>
                    {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                  </select>
                  <button className="btn sm danger" onClick={() => setChanges(changes.filter((_, k) => k !== i))}>Remove</button>
                </div>
                <ChangeFields c={c} m={m} onChange={(p) => update(i, p)} />
              </div>
            ))}
            <button className="btn" onClick={() => setChanges([...changes, defaults('PersonLeaves', m)])}>+ Add change</button>

            <div className="grid g2">
              <label className="field">Runs<input type="number" min={100} max={20000} step={100} value={iterations} onChange={(e) => setIterations(Number(e.target.value))} /></label>
              <label className="field">Daily absence<input type="number" min={0} max={0.5} step={0.01} value={absence} onChange={(e) => setAbsence(Number(e.target.value))} /></label>
            </div>
            <label className="row small"><input type="checkbox" checked={mitigate} onChange={(e) => setMitigate(e.target.checked)} /> Test a mitigation if the scenario hurts</label>
            <ErrorBox error={err} />
            <button className="btn primary" disabled={busy} onClick={() => run()}>{busy ? 'Simulating…' : `Run ${iterations.toLocaleString()} simulations`}</button>
          </div>

          <div className="card">
            <h2>Past runs</h2>
            {!runs.data ? <Loading /> : runs.data.length === 0 ? <div className="muted">None yet.</div> : (
              <div className="stack" style={{ gap: 6 }}>
                {runs.data.map((r) => (
                  <div key={r.id} className="spread small">
                    <a href="#" onClick={(e) => { e.preventDefault(); api.simRun(r.id).then(show) }}>
                      <b>{r.name}</b> <span className="muted">P80 {fmtShort(r.scenarioP80)} · {pct(r.scenarioOnTime)}</span>
                    </a>
                    <button className="btn sm" onClick={async () => { await api.deleteSimRun(r.id); runs.reload() }}>×</button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* ---- Results ---- */}
        <div className="stack">
          {!result ? <div className="card"><Loading what={busy ? 'Simulating' : 'Waiting for a run'} /></div> : <Results r={result} frame={frame} setFrame={setFrame} playing={playing} setPlaying={setPlaying} />}
        </div>
      </div>
    </>
  )
}

function ChangeFields({ c, m, onChange }: { c: ScenarioChange; m: SimModel; onChange: (p: Partial<ScenarioChange>) => void }) {
  const num = (k: keyof ScenarioChange) => (e: { target: { value: string } }) => onChange({ [k]: Number(e.target.value) })
  const personSel = (allowAll = false) => (
    <label className="field">Person<select value={c.personId ?? ''} onChange={(e) => onChange({ personId: e.target.value || undefined })}>
      {allowAll && <option value="">Everyone</option>}
      {m.people.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.role})</option>)}
    </select></label>
  )
  const taskSel = (
    <label className="field">Task<select value={c.taskId ?? ''} onChange={(e) => onChange({ taskId: e.target.value })}>
      {m.tasks.map((t) => <option key={t.id} value={t.id}>{t.key} {t.title}</option>)}
    </select></label>
  )
  switch (c.type) {
    case 'PersonLeaves': return <>{personSel()}<div className="grid g2">
      <label className="field">On working day<input type="number" min={0} value={c.day ?? 0} onChange={num('day')} /></label>
      <label className="field">Knowledge lost<input type="number" min={0} max={1} step={0.1} value={c.knowledgeLoss ?? 0.3} onChange={num('knowledgeLoss')} /></label></div></>
    case 'NewHire': return <>
      <div className="grid g2">
        <label className="field">Name<input value={c.name ?? ''} onChange={(e) => onChange({ name: e.target.value })} /></label>
        <label className="field">Role<select value={c.role} onChange={(e) => onChange({ role: e.target.value as ScenarioChange['role'] })}>{ROLES.map((r) => <option key={r}>{r}</option>)}</select></label>
      </div>
      <div className="grid g3">
        <label className="field">Joins day<input type="number" min={0} value={c.day ?? 0} onChange={num('day')} /></label>
        <label className="field">Ramp-up days<input type="number" min={0} value={c.rampUpDays ?? 15} onChange={num('rampUpDays')} /></label>
        <label className="field">Capacity<input type="number" min={0.1} max={1} step={0.1} value={c.capacity ?? 1} onChange={num('capacity')} /></label>
      </div></>
    case 'ScopeChange': return <>
      <label className="field">Role affected<select value={c.role ?? ''} onChange={(e) => onChange({ role: (e.target.value || undefined) as ScenarioChange['role'] })}>
        <option value="">All roles</option>{ROLES.map((r) => <option key={r}>{r}</option>)}</select></label>
      <div className="grid g3">
        <label className="field">Add tasks<input type="number" min={0} value={c.addTasks ?? 0} onChange={num('addTasks')} /></label>
        <label className="field">Days each<input type="number" min={0} step={0.5} value={c.effortDaysEach ?? 3} onChange={num('effortDaysEach')} /></label>
        <label className="field">Effort ×<input type="number" min={0.1} step={0.1} value={c.effortMultiplier ?? 1} onChange={num('effortMultiplier')} /></label>
      </div></>
    case 'RemoveTask': return taskSel
    case 'PrioritySwap': return <>{taskSel}<label className="field">New priority<select value={c.newPriority ?? 1} onChange={num('newPriority')}>{[1, 2, 3, 4, 5].map((p) => <option key={p} value={p}>P{p}</option>)}</select></label></>
    case 'MeetingLoad': return <>{personSel(true)}<label className="field">Capacity change (−0.1 = 10% more meetings)<input type="number" step={0.05} min={-0.9} max={0.9} value={c.capacityDelta ?? -0.1} onChange={num('capacityDelta')} /></label></>
    case 'ClientDelay': return <>{taskSel}<label className="field">Blocked until day<input type="number" min={0} value={c.untilDay ?? 10} onChange={num('untilDay')} /></label></>
  }
}

function Delta({ days }: { days: number }) {
  if (days === 0) return <span className="muted">±0 days</span>
  return <span className={days > 0 ? 'delta-up' : 'delta-down'}>{days > 0 ? '+' : ''}{days} days</span>
}

function Results({ r, frame, setFrame, playing, setPlaying }: { r: SimResult; frame: number; setFrame: (f: number) => void; playing: boolean; setPlaying: (p: boolean) => void }) {
  const c = r.comparison
  const frames = c.replay.frames
  const f = frames[Math.min(frame, frames.length - 1)]
  const done = f ? f.taskState.filter((s) => s === 3).length : 0
  const blocked = f ? f.taskState.filter((s) => s === 4).length : 0
  const deadlineBin = c.deadline ? c.histogram.reduce((best, b) => (Math.abs(b.day - (c.deadlineDay ?? 0)) < Math.abs(best.day - (c.deadlineDay ?? 0)) ? b : best), c.histogram[0]) : null

  return (
    <>
      <div className="card">
        <div className="spread"><h2 style={{ margin: 0 }}>{r.name}</h2><span className="muted small">{r.iterations.toLocaleString()} runs · seed {r.seed}</span></div>
        {r.changeDescriptions.length > 0 && <ul className="small" style={{ margin: '8px 0', paddingLeft: 18 }}>{r.changeDescriptions.map((d, i) => <li key={i}>{d}</li>)}</ul>}
        <div className="kpi-compare mt">
          <span className="h"></span><span className="h">Baseline</span><span className="h">Scenario</span>
          <span>P50 finish</span><b>{fmtShort(c.baseline.p50)}</b><span><b>{fmtShort(c.scenario.p50)}</b> <Delta days={c.deltaP50Days} /></span>
          <span>P80 finish</span><b>{fmtShort(c.baseline.p80)}</b><span><b>{fmtShort(c.scenario.p80)}</b> <Delta days={c.deltaP80Days} /></span>
          <span>P95 finish</span><b>{fmtShort(c.baseline.p95)}</b><b>{fmtShort(c.scenario.p95)}</b>
          <span>On time{c.deadline ? ` (${fmtShort(c.deadline)})` : ''}</span><b>{pct(c.baseline.probabilityOnTime)}</b><b>{pct(c.scenario.probabilityOnTime)}</b>
        </div>
        <div className="quote mt" style={{ borderLeftColor: 'var(--accent)' }}>{r.narrative}</div>
        {r.mitigation && (
          <div className="small mt"><span className={`badge ${r.mitigation.recoveredDays > 0 ? 'ok' : 'warn'}`}>Mitigation</span> {r.mitigation.description}: recovers <b>{r.mitigation.recoveredDays}</b> of {r.mitigation.lostDays} lost days, P80 {fmtShort(r.mitigation.p80)}, {pct(r.mitigation.probabilityOnTime)} on time.</div>
        )}
        {c.removedTasks.length > 0 && <div className="small muted mt">Removed from scope: {c.removedTasks.map((t) => t.key).join(', ')}</div>}
      </div>

      <div className="card">
        <div className="spread" style={{ marginBottom: 10 }}>
          <h2 style={{ margin: 0 }}>3D replay of a typical run</h2>
          <span className="muted small">Run #{c.replay.iteration + 1} finishes in {c.replay.finishDay} working days (closest to P50)</span>
        </div>
        <div className="canvas-wrap">
          <Sim3D comparison={c} frameIndex={frame} />
          <div className="canvas-legend">
            {['Waiting', 'Ready', 'Active', 'Done', 'Blocked'].map((l, i) => <span key={l}><i style={{ background: STATE_COLOR[i] }} />{l}</span>)}
            <span><i style={{ background: '#fde047' }} />Critical path</span>
          </div>
          <div className="canvas-hud">
            <div style={{ fontSize: 18, fontWeight: 700 }}>Day {f ? f.day + 1 : 0}</div>
            <div>{f ? fmtDate(f.date) : ''}</div>
            <div>{done}/{c.replay.tasks.length} done{blocked > 0 && <> · <span style={{ color: '#f87171' }}>{blocked} blocked</span></>}</div>
          </div>
        </div>
        <div className="row mt">
          <button className="btn sm" onClick={() => { if (frame >= frames.length - 1) setFrame(0); setPlaying(!playing) }}>{playing ? 'Pause' : 'Play'}</button>
          <input type="range" min={0} max={Math.max(0, frames.length - 1)} value={frame} onChange={(e) => { setPlaying(false); setFrame(Number(e.target.value)) }} style={{ flex: 1 }} />
        </div>
        <div className="row small mt" style={{ gap: 12 }}>
          {Array.from(new Set(c.replay.people.map((p) => p.role))).map((role) => <span key={role}><i style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 5, background: ROLE_COLOR[role], marginRight: 4 }} />{role}</span>)}
        </div>
      </div>

      <div className="card">
        <h2>Finish date distribution</h2>
        <div style={{ height: 260 }}>
          <ResponsiveContainer>
            <BarChart data={c.histogram.map((b) => ({ ...b, label: fmtShort(b.date) }))} barGap={0} margin={{ left: -10, right: 10 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" vertical={false} />
              <XAxis dataKey="label" tick={{ fontSize: 11, fill: 'var(--muted)' }} minTickGap={20} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted)' }} />
              <Tooltip contentStyle={{ background: 'var(--panel)', border: '1px solid var(--border)' }} />
              <Legend />
              <Bar dataKey="baseline" name="Baseline" fill="var(--baseline)" opacity={0.55} />
              <Bar dataKey="scenario" name="Scenario" fill="var(--scenario)" />
              {deadlineBin && <ReferenceLine x={fmtShort(deadlineBin.date)} stroke="var(--bad)" strokeDasharray="4 4" label={{ value: 'Deadline', fill: 'var(--bad)', fontSize: 11, position: 'top' }} />}
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="card">
        <h2>Per-task forecast (scenario)</h2>
        <table>
          <thead><tr><th>Task</th><th>P50 day</th><th>P80 day</th><th>P80 date</th></tr></thead>
          <tbody>
            {[...c.scenario.tasks].sort((a, b) => a.p80Day - b.p80Day).map((t) => (
              <tr key={t.id}>
                <td><b>{t.key}</b> {t.title} {t.onCriticalPath && <span className="badge warn">critical</span>}</td>
                <td>{t.p50Day}</td><td>{t.p80Day}</td><td>{fmtShort(t.p80Date)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}
