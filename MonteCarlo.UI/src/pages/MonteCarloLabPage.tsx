import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { CartesianGrid, Legend, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, fmtDate, fmtShort, pct, type Landscape, type LandscapeAxisName, type MonteCarloTraceResult, type ScenarioChange, type SimModel } from '../api'
import { ErrorBox, Loading, useLoad } from '../components/common'
import MonteCarlo3D, { LATE, ON_TIME, UNFINISHED } from '../components/MonteCarlo3D'
import Landscape3D from '../components/Landscape3D'

const SPEEDS: [string, number][] = [['Slow', 1.5], ['Normal', 6], ['Fast', 40], ['Very fast', 250]]
const AXES: LandscapeAxisName[] = ['Capacity', 'Scope', 'Hires', 'Absence']

function presets(m: SimModel): { label: string; changes: ScenarioChange[] }[] {
  return [
    { label: 'Baseline (no changes)', changes: [] },
    {
      label: 'Two backend developers leave', changes: m.people.filter((p) => p.role === 'Backend').slice(0, 2)
        .map((p, i) => ({ type: 'PersonLeaves', personId: p.id, day: 5 + i * 5, knowledgeLoss: 0.3 })),
    },
    { label: 'Client scope +20%', changes: [{ type: 'ScopeChange', effortMultiplier: 1.2, addTasks: 0, effortDaysEach: 0 }] },
    { label: 'Hire a backend contractor', changes: [{ type: 'NewHire', name: 'Contractor', role: 'Backend', day: 0, capacity: 1, rampUpDays: 10 }] },
    { label: '15% more meetings', changes: [{ type: 'MeetingLoad', capacityDelta: -0.15 }] },
  ]
}

export default function MonteCarloLabPage() {
  const model = useLoad(() => api.simModel(), [])
  const [params, setParams] = useSearchParams()
  const tab = params.get('tab') === 'landscape' ? 'landscape' : 'runs'
  const setTab = (t: 'runs' | 'landscape') => setParams(t === 'runs' ? {} : { tab: t }, { replace: true })
  if (model.error) return <ErrorBox error={model.error} />
  if (!model.data) return <Loading what="Loading project model" />
  const m = model.data
  return (
    <>
      <div className="page-head">
        <div>
          <h1>3D Monte Carlo</h1>
          <p>
            The .NET engine plays the project forward ({m.tasks.length} open tasks, {m.people.length} people) thousands of times with random estimate
            errors (calibrated: actual ≈ {m.calibration.meanRatio.toFixed(2)}× estimate) and random absences. Watch every run, then explore the risk landscape.
          </p>
        </div>
      </div>
      <div className="tabs">
        <button className={tab === 'runs' ? 'active' : ''} onClick={() => setTab('runs')}>Run by run</button>
        <button className={tab === 'landscape' ? 'active' : ''} onClick={() => setTab('landscape')}>Risk landscape</button>
      </div>
      {tab === 'runs' ? <RunByRun m={m} /> : <RiskLandscape />}
    </>
  )
}

// ---- Run by run -------------------------------------------------------------------------------------

function RunByRun({ m }: { m: SimModel }) {
  const options = useMemo(() => presets(m), [m])
  const [preset, setPreset] = useState(0)
  const [iterations, setIterations] = useState(1000)
  const [paths, setPaths] = useState(150)
  const [seed, setSeed] = useState(42)
  const [absence, setAbsence] = useState(0.03)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [res, setRes] = useState<MonteCarloTraceResult | null>(null)

  const pos = useRef(0)
  const [k, setK] = useState(0)
  const [playing, setPlaying] = useState(true)
  const [speed, setSpeed] = useState(6)

  async function run() {
    setBusy(true); setErr(null)
    try {
      const r = await api.monteCarlo({ changes: options[preset].changes, iterations, pathCount: paths, seed, absenceRate: absence })
      pos.current = 0; setK(0); setRes(r); setPlaying(true)
    } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { run() }, [])

  const t = res?.trace
  const total = t?.samples.length ?? 0
  // Percentiles exactly as the engine computed them after this many iterations.
  const conv = useMemo(() => {
    if (!t || k === 0) return null
    let best = t.convergence[0]
    for (const c of t.convergence) if (c.iterations <= k) best = c
    return best
  }, [t, k])
  const current = t && k > 0 ? t.samples[Math.min(k, total) - 1] : null
  const shown = t ? t.samples.slice(0, k) : []
  const late = shown.filter((s) => !s.onTime).length

  return (
    <div className="stack">
      <div className="card">
        <div className="row" style={{ alignItems: 'flex-end', flexWrap: 'wrap', gap: 12 }}>
          <label className="field" style={{ minWidth: 230 }}>Scenario
            <select value={preset} onChange={(e) => setPreset(Number(e.target.value))}>{options.map((o, i) => <option key={o.label} value={i}>{o.label}</option>)}</select>
          </label>
          <label className="field">Iterations
            <select value={iterations} onChange={(e) => setIterations(Number(e.target.value))}>{[200, 500, 1000, 2000, 5000].map((n) => <option key={n} value={n}>{n.toLocaleString()}</option>)}</select>
          </label>
          <label className="field">Paths drawn
            <select value={paths} onChange={(e) => setPaths(Number(e.target.value))}>{[50, 150, 300, 500].map((n) => <option key={n} value={n}>{n}</option>)}</select>
          </label>
          <label className="field">Absence / day<input type="number" min={0} max={0.5} step={0.01} value={absence} onChange={(e) => setAbsence(Number(e.target.value))} style={{ width: 90 }} /></label>
          <label className="field">Seed<input type="number" value={seed} onChange={(e) => setSeed(Number(e.target.value))} style={{ width: 90 }} /></label>
          <button className="btn primary" disabled={busy} onClick={run}>{busy ? 'Simulating…' : 'Run Monte Carlo'}</button>
          <button className="btn" disabled={busy} onClick={() => { setSeed(Math.floor(Math.random() * 100000)); }}>New seed</button>
        </div>
        <ErrorBox error={err} />
        {res && res.changeDescriptions.length > 0 && <div className="small muted mt">{res.changeDescriptions.join(' · ')}</div>}
      </div>

      {!t ? <div className="card"><Loading what={busy ? 'Simulating in .NET' : 'Waiting for a run'} /></div> : (
        <>
          <div className="card">
            <div className="canvas-wrap" style={{ height: 560 }}>
              <MonteCarlo3D trace={t} pos={pos} playing={playing} speed={speed} conv={conv} onProgress={setK} />
              <div className="canvas-legend">
                <span><i style={{ background: ON_TIME }} />On time</span>
                <span><i style={{ background: LATE }} />Late</span>
                {t.summary.unfinishedRuns > 0 && <span><i style={{ background: UNFINISHED }} />Never finished</span>}
                <span><i style={{ background: '#fde68a' }} />Current run</span>
              </div>
              <div className="canvas-hud">
                <div style={{ fontSize: 18, fontWeight: 700 }}>Run {Math.min(k + 1, total).toLocaleString()} / {total.toLocaleString()}</div>
                {current && (
                  <div>
                    #{current.iteration + 1}: {current.finished ? <>finishes day {current.finishDay}</> : 'never finishes'} ·{' '}
                    <span style={{ color: current.onTime ? '#86efac' : '#fca5a5' }}>{current.onTime ? 'on time' : 'late'}</span>
                    <br />work ×{current.estimateRatio.toFixed(2)} of plan · {current.absentDays} absent person-days
                    {current.lastTask >= 0 && <><br />last to finish: {t.summary.tasks[current.lastTask]?.key}</>}
                  </div>
                )}
                {conv && (
                  <div style={{ marginTop: 6 }}>
                    P50 <b>{conv.p50Days}d</b> · P80 <b>{conv.p80Days}d</b> · P95 <b>{conv.p95Days}d</b>
                    <br />on time so far: <b>{pct(conv.onTime)}</b> ({late.toLocaleString()} late)
                  </div>
                )}
                <div className="small" style={{ opacity: 0.7, marginTop: 4 }}>computed in .NET in {res!.elapsedMs} ms</div>
              </div>
            </div>
            <div className="row mt" style={{ gap: 8, flexWrap: 'wrap' }}>
              <button className="btn sm" onClick={() => { if (pos.current >= total) pos.current = 0; setPlaying(!playing) }}>{playing && k < total ? 'Pause' : 'Play'}</button>
              <button className="btn sm" onClick={() => { pos.current = 0; setPlaying(true) }}>Restart</button>
              <button className="btn sm" onClick={() => { pos.current = total; setPlaying(false) }}>Skip to end</button>
              {SPEEDS.map(([l, v]) => <button key={l} className={`btn sm ${speed === v ? 'primary' : ''}`} onClick={() => setSpeed(v)}>{l}</button>)}
              <input type="range" min={0} max={total} value={k} onChange={(e) => { setPlaying(false); pos.current = Number(e.target.value) }} style={{ flex: 1, minWidth: 160 }} />
            </div>
            <div className="small muted mt">
              Back: each line is one run's burn-down of remaining work (first {t.samples.filter((s) => s.burndown).length} runs). Front: every run drops a ball on its finish day,
              so the piles become the forecast distribution. Drag to orbit, scroll to zoom.
            </div>
          </div>

          <div className="grid g4">
            <Kpi label="P50 finish" value={fmtShort(t.summary.p50)} sub={`${t.summary.p50Days} working days`} />
            <Kpi label="P80 finish" value={fmtShort(t.summary.p80)} sub={`${t.summary.p80Days} working days`} />
            <Kpi label="P95 finish" value={fmtShort(t.summary.p95)} sub={`${t.summary.p95Days} working days`} />
            <Kpi label={`On time${t.deadline ? ` (${fmtShort(t.deadline)})` : ''}`} value={pct(t.summary.probabilityOnTime)} sub={`plan: ${t.plannedWorkDays} work-days, start ${fmtDate(t.startDate)}`} />
          </div>

          <div className="grid g2">
            <div className="card">
              <h2>Convergence</h2>
              <p className="small muted">Percentiles after each batch of iterations. When the lines go flat, more runs won't change the answer.</p>
              <ResponsiveContainer width="100%" height={240}>
                <LineChart data={t.convergence} margin={{ top: 8, right: 12, bottom: 0, left: -12 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
                  <XAxis dataKey="iterations" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} domain={['dataMin - 2', 'dataMax + 2']} />
                  <Tooltip />
                  <Legend />
                  {t.deadlineDay && <ReferenceLine y={t.deadlineDay} stroke="var(--bad)" strokeDasharray="4 4" />}
                  <ReferenceLine x={conv?.iterations} stroke="var(--muted)" />
                  <Line dataKey="p50Days" name="P50" stroke="#38bdf8" dot={false} isAnimationActive={false} />
                  <Line dataKey="p80Days" name="P80" stroke="#eab308" dot={false} isAnimationActive={false} />
                  <Line dataKey="p95Days" name="P95" stroke="#f97316" dot={false} isAnimationActive={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
            <div className="card">
              <h2>What drives the date</h2>
              <p className="small muted">Criticality index: share of runs in which the task sat on that run's critical chain.</p>
              <div className="stack" style={{ gap: 6 }}>
                {t.drivers.slice(0, 8).map((d) => (
                  <div key={d.id} className="small">
                    <div className="spread"><span><b>{d.key}</b> {d.title}</span><span>{pct(d.driverShare)}</span></div>
                    <div style={{ height: 6, background: 'var(--panel-2)', borderRadius: 3 }}>
                      <div style={{ width: `${d.driverShare * 100}%`, height: 6, borderRadius: 3, background: d.driverShare > 0.5 ? 'var(--bad)' : 'var(--accent)' }} />
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  )
}

function Kpi({ label, value, sub }: { label: string; value: string; sub: string }) {
  return (
    <div className="card">
      <div className="small muted">{label}</div>
      <div style={{ fontSize: 22, fontWeight: 700 }}>{value}</div>
      <div className="small muted">{sub}</div>
    </div>
  )
}

// ---- Risk landscape ---------------------------------------------------------------------------------

function RiskLandscape() {
  const [xAxis, setX] = useState<LandscapeAxisName>('Capacity')
  const [zAxis, setZ] = useState<LandscapeAxisName>('Scope')
  const [steps, setSteps] = useState(7)
  const [iterations, setIterations] = useState(300)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [data, setData] = useState<Landscape | null>(null)

  async function run(x = xAxis, z = zAxis) {
    setBusy(true); setErr(null)
    try { setData(await api.landscape({ xAxis: x, zAxis: z, steps, iterations, seed: 42, absenceRate: 0.03 })) }
    catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { run() }, [])

  const best = data ? [...data.cells].filter((c) => c.probabilityOnTime >= 0.8)
    .sort((a, b) => Math.abs(a.xi - data.x.baselineIndex) + Math.abs(a.zi - data.z.baselineIndex) - (Math.abs(b.xi - data.x.baselineIndex) + Math.abs(b.zi - data.z.baselineIndex)))[0] : null

  return (
    <div className="stack">
      <div className="card">
        <div className="row" style={{ alignItems: 'flex-end', flexWrap: 'wrap', gap: 12 }}>
          <label className="field">Across (X)
            <select value={xAxis} onChange={(e) => setX(e.target.value as LandscapeAxisName)}>{AXES.map((a) => <option key={a} disabled={a === zAxis}>{a}</option>)}</select>
          </label>
          <label className="field">Depth (Z)
            <select value={zAxis} onChange={(e) => setZ(e.target.value as LandscapeAxisName)}>{AXES.map((a) => <option key={a} disabled={a === xAxis}>{a}</option>)}</select>
          </label>
          <label className="field">Grid
            <select value={steps} onChange={(e) => setSteps(Number(e.target.value))}>{[5, 7, 9].map((n) => <option key={n} value={n}>{n} × {n}</option>)}</select>
          </label>
          <label className="field">Runs per point
            <select value={iterations} onChange={(e) => setIterations(Number(e.target.value))}>{[200, 300, 500, 1000].map((n) => <option key={n} value={n}>{n}</option>)}</select>
          </label>
          <button className="btn primary" disabled={busy} onClick={() => run()}>{busy ? 'Simulating…' : 'Build landscape'}</button>
        </div>
        <ErrorBox error={err} />
        <div className="small muted mt">
          Every point on the surface is a full Monte Carlo forecast. Capacity = share of today's focus time, Scope = effort vs today's estimates,
          Hires = extra people in the busiest role (10-day ramp-up), Absence = chance someone is out on a given day.
        </div>
      </div>
      {!data ? <div className="card"><Loading what={busy ? 'Simulating in .NET' : 'Waiting'} /></div> : (
        <div className="card">
          <Landscape3D data={data} />
          <div className="small mt">
            {best
              ? <>Closest point to today's plan with at least 80% on-time: <b>{data.x.label} {data.x.labels[best.xi]}</b>, <b>{data.z.label} {data.z.labels[best.zi]}</b> → P80 {fmtShort(best.p80)}, {pct(best.probabilityOnTime)} on time.</>
              : <>No point on this grid reaches 80% on time. Try other levers or a later deadline.</>}
          </div>
        </div>
      )}
    </div>
  )
}
