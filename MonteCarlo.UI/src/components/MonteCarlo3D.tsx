import { useEffect, useMemo, useRef, type MutableRefObject } from 'react'
import { Canvas, useFrame } from '@react-three/fiber'
import { Html, OrbitControls } from '@react-three/drei'
import * as THREE from 'three'
import type { ConvergencePoint, MonteCarloTrace } from '../api'

// Every number here comes from the .NET engine (MonteCarloEngine.Explore). This file only draws it:
//  - back: one burn-down path per iteration (remaining work vs working day), drawn in iteration order
//  - front: when a run finishes, a ball drops on its finish day; the piles become the probability distribution
//  - red wall: the deadline. Poles: P50 / P80 / P95 as they converge with more iterations.

export const ON_TIME = '#22c55e'
export const LATE = '#ef4444'
export const UNFINISHED = '#a855f7'
const WIDTH = 40 // world units along the day axis
const PATH_HEIGHT = 12
const PATH_DEPTH = 14
const DROP_HEIGHT = 16
const DROP_SECONDS = 0.55

export interface Geometry {
  sx: number; sy: number; maxDay: number; d: number; cols: number
  laneZ: (i: number) => number
  ballPos: THREE.Vector3[]
}

function useGeometry(trace: MonteCarloTrace): Geometry {
  return useMemo(() => {
    const finished = trace.samples.filter((s) => s.finished).map((s) => s.finishDay)
    const maxDay = Math.max(5, ...(finished.length ? finished : [10]), trace.deadlineDay ?? 0) + 3
    const sx = WIDTH / maxDay
    const maxWork = Math.max(1, ...trace.samples.filter((s) => s.burndown).map((s) => s.burndown![0]))
    const sy = PATH_HEIGHT / maxWork
    const paths = Math.max(1, trace.samples.filter((s) => s.burndown).length)
    const laneZ = (i: number) => -2.5 - (i / paths) * PATH_DEPTH

    // Pile layout: each finish day is a column `cols` balls deep; balls stack upwards in arrival order.
    const cols = 4
    const counts = new Map<number, number>()
    trace.distribution.forEach((b) => counts.set(b.day, b.count))
    const peak = Math.max(1, ...trace.distribution.map((b) => b.count))
    const d = Math.min(sx * 0.95, 0.9, 11 / Math.ceil(peak / cols))
    const slot = new Map<number, number>()
    const ballPos = trace.samples.map((s) => {
      const day = s.finished ? s.finishDay : maxDay - 1
      const k = slot.get(day) ?? 0
      slot.set(day, k + 1)
      const level = Math.floor(k / cols)
      return new THREE.Vector3(day * sx, d / 2 + level * d, 0.6 + (k % cols) * d)
    })
    return { sx, sy, maxDay, d, cols, laneZ, ballPos }
  }, [trace])
}

const colorOf = (s: { finished: boolean; onTime: boolean }) => (!s.finished ? UNFINISHED : s.onTime ? ON_TIME : LATE)

/** All burn-down paths in one geometry; revealing iterations in order is just a draw range. */
function Paths({ trace, g, pos }: { trace: MonteCarloTrace; g: Geometry; pos: MutableRefObject<number> }) {
  const { geometry, offsets, lengths, withPath } = useMemo(() => {
    const withPath = trace.samples.filter((s) => s.burndown && s.burndown.length > 1)
    const total = withPath.reduce((n, s) => n + (s.burndown!.length - 1) * 2, 0)
    const positions = new Float32Array(total * 3)
    const colors = new Float32Array(total * 3)
    const offsets: number[] = []
    const lengths: number[] = []
    let v = 0
    const c = new THREE.Color()
    withPath.forEach((s, i) => {
      offsets.push(v)
      lengths.push(s.burndown!.length)
      c.set(colorOf(s)).multiplyScalar(0.8)
      const z = g.laneZ(i)
      for (let j = 0; j < s.burndown!.length - 1; j++) {
        for (const k of [j, j + 1]) {
          positions.set([k * g.sx, s.burndown![k] * g.sy, z], v * 3)
          colors.set([c.r, c.g, c.b], v * 3)
          v++
        }
      }
    })
    offsets.push(v)
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    geometry.setDrawRange(0, 0)
    return { geometry, offsets, lengths, withPath }
  }, [trace, g])
  useEffect(() => () => geometry.dispose(), [geometry])

  // Bright "current run" line and a runner at its tip.
  const head = useMemo(() => {
    const max = Math.max(2, ...lengths)
    const gg = new THREE.BufferGeometry()
    gg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(max * 3), 3))
    gg.setDrawRange(0, 0)
    return gg
  }, [lengths])
  const headLine = useMemo(() => new THREE.Line(head, new THREE.LineBasicMaterial({ color: '#fde68a' })), [head])
  const runner = useRef<THREE.Mesh>(null)

  useFrame(() => {
    const r = pos.current
    const n = lengths.length
    const i = Math.floor(r)
    if (i >= n) {
      geometry.setDrawRange(0, offsets[n])
      head.setDrawRange(0, 0)
      if (runner.current) runner.current.visible = false
      return
    }
    const frac = r - i
    const steps = Math.floor(frac * (lengths[i] - 1))
    geometry.setDrawRange(0, offsets[i] + steps * 2)

    const s = withPath[i]
    const attr = head.getAttribute('position') as THREE.BufferAttribute
    const z = g.laneZ(i)
    const upto = Math.min(lengths[i] - 1, steps + 1)
    for (let k = 0; k <= upto; k++) attr.setXYZ(k, k * g.sx, s.burndown![k] * g.sy, z)
    attr.needsUpdate = true
    head.setDrawRange(0, upto + 1)
    if (runner.current) {
      runner.current.visible = true
      runner.current.position.set(upto * g.sx, s.burndown![upto] * g.sy, z)
    }
  })

  return (
    <>
      <lineSegments geometry={geometry}>
        <lineBasicMaterial vertexColors transparent opacity={0.4} depthWrite={false} />
      </lineSegments>
      <primitive object={headLine} />
      <mesh ref={runner}>
        <sphereGeometry args={[0.28, 16, 16]} />
        <meshStandardMaterial color="#fde68a" emissive="#f59e0b" emissiveIntensity={1.2} />
      </mesh>
    </>
  )
}

/** One ball per iteration, dropped onto its finish day when that run completes. */
function Balls({ trace, g, pos }: { trace: MonteCarloTrace; g: Geometry; pos: MutableRefObject<number> }) {
  const mesh = useRef<THREE.InstancedMesh>(null)
  const n = trace.samples.length
  const state = useRef({ shown: 0, start: new Float32Array(n).fill(-1), falling: new Set<number>() })
  const tmp = useMemo(() => new THREE.Object3D(), [])
  const paths = useMemo(() => trace.samples.filter((s) => s.burndown).length, [trace])

  useEffect(() => {
    const m = mesh.current
    if (!m) return
    const c = new THREE.Color()
    trace.samples.forEach((s, i) => {
      m.setColorAt(i, c.set(colorOf(s)))
      tmp.position.copy(g.ballPos[i]); tmp.scale.setScalar(0); tmp.updateMatrix()
      m.setMatrixAt(i, tmp.matrix)
    })
    m.instanceMatrix.needsUpdate = true
    if (m.instanceColor) m.instanceColor.needsUpdate = true
    state.current = { shown: 0, start: new Float32Array(n).fill(-1), falling: new Set() }
  }, [trace, g, n, tmp])

  useFrame(({ clock }) => {
    const m = mesh.current
    if (!m) return
    const st = state.current
    // A run with a path lands when its path has been fully drawn; the rest land as they are revealed.
    const target = Math.min(n, Math.floor(pos.current))
    const now = clock.elapsedTime
    let dirty = false
    if (target > st.shown) {
      const instant = target - st.shown > 400
      for (let i = st.shown; i < target; i++) {
        if (instant) { place(m, i, g.ballPos[i], 1); st.start[i] = -2 } else { st.start[i] = now; st.falling.add(i) }
      }
      st.shown = target; dirty = true
    } else if (target < st.shown) {
      for (let i = target; i < st.shown; i++) { place(m, i, g.ballPos[i], 0); st.falling.delete(i); st.start[i] = -1 }
      st.shown = target; dirty = true
    }
    for (const i of st.falling) {
      const t = Math.min(1, (now - st.start[i]) / (i < paths ? DROP_SECONDS : DROP_SECONDS * 0.6))
      const p = g.ballPos[i]
      tmp.position.set(p.x, DROP_HEIGHT - (DROP_HEIGHT - p.y) * t * t, p.z)
      tmp.scale.setScalar(g.d * 0.92); tmp.updateMatrix()
      m.setMatrixAt(i, tmp.matrix)
      if (t >= 1) st.falling.delete(i)
      dirty = true
    }
    if (dirty) m.instanceMatrix.needsUpdate = true
  })

  function place(m: THREE.InstancedMesh, i: number, p: THREE.Vector3, scale: number) {
    tmp.position.copy(p); tmp.scale.setScalar(scale * g.d * 0.92); tmp.updateMatrix()
    m.setMatrixAt(i, tmp.matrix)
  }

  return (
    <instancedMesh ref={mesh} args={[undefined, undefined, n]} castShadow>
      <sphereGeometry args={[0.5, 10, 10]} />
      <meshStandardMaterial roughness={0.4} metalness={0.1} />
    </instancedMesh>
  )
}

/** Percentile marker that slides as the .NET convergence series updates. */
function Pole({ day, label, color, g, height }: { day: number; label: string; color: string; g: Geometry; height: number }) {
  const ref = useRef<THREE.Group>(null)
  useFrame((_, dt) => {
    if (ref.current) ref.current.position.x += (day * g.sx - ref.current.position.x) * Math.min(1, dt * 6)
  })
  return (
    <group ref={ref} position={[day * g.sx, 0, 0.3]}>
      <mesh position={[0, height / 2, 0]}>
        <cylinderGeometry args={[0.05, 0.05, height, 8]} />
        <meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.8} />
      </mesh>
      <Html position={[0, height + 0.5, 0]} center distanceFactor={22} style={{ pointerEvents: 'none' }}>
        <div style={{ font: '700 12px Segoe UI, sans-serif', color, background: 'rgba(15,23,42,.85)', padding: '1px 6px', borderRadius: 4, whiteSpace: 'nowrap' }}>{label}</div>
      </Html>
    </group>
  )
}

function Scene({ trace, pos, conv }: { trace: MonteCarloTrace; pos: MutableRefObject<number>; conv: ConvergencePoint | null }) {
  const g = useGeometry(trace)
  const span = g.maxDay * g.sx
  const center = new THREE.Vector3(span / 2, 3, -PATH_DEPTH / 2 + 1)
  const ticks = useMemo(() => {
    const step = g.maxDay > 60 ? 10 : 5
    const out: number[] = []
    for (let d = 0; d <= g.maxDay; d += step) out.push(d)
    return out
  }, [g.maxDay])
  const deadlineX = trace.deadlineDay ? (trace.deadlineDay + 0.5) * g.sx : null
  const pileTop = Math.max(...g.ballPos.map((p) => p.y)) + g.d

  return (
    <>
      <color attach="background" args={['#0b1220']} />
      <ambientLight intensity={0.55} />
      <directionalLight position={[span * 0.3, 30, 20]} intensity={1.2} castShadow />
      <hemisphereLight args={['#93c5fd', '#0f172a', 0.35]} />

      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[span / 2, 0, -PATH_DEPTH / 2 + 2]} receiveShadow>
        <planeGeometry args={[span + 8, PATH_DEPTH + 10]} />
        <meshStandardMaterial color="#111a2b" />
      </mesh>
      <gridHelper args={[span + 8, Math.round(g.maxDay / 2), '#1e293b', '#16213a']} position={[span / 2, 0.01, -PATH_DEPTH / 2 + 2]} />

      {/* day axis */}
      {ticks.map((d) => (
        <Html key={d} position={[d * g.sx, 0.05, 4.2]} center distanceFactor={26} style={{ pointerEvents: 'none' }}>
          <div style={{ font: '500 11px Segoe UI, sans-serif', color: '#94a3b8', whiteSpace: 'nowrap' }}>day {d}</div>
        </Html>
      ))}
      <Html position={[-1.5, PATH_HEIGHT * 0.55, -PATH_DEPTH / 2]} center distanceFactor={26} style={{ pointerEvents: 'none' }}>
        <div style={{ font: '600 11px Segoe UI, sans-serif', color: '#94a3b8', whiteSpace: 'nowrap' }}>remaining work ↑</div>
      </Html>
      <Html position={[-1.5, 0.3, -PATH_DEPTH]} center distanceFactor={26} style={{ pointerEvents: 'none' }}>
        <div style={{ font: '600 11px Segoe UI, sans-serif', color: '#94a3b8', whiteSpace: 'nowrap' }}>later runs ↗</div>
      </Html>

      {deadlineX !== null && (
        <group position={[deadlineX, 0, -PATH_DEPTH / 2 + 1.5]}>
          <mesh position={[0, PATH_HEIGHT / 2 + 1, 0]} rotation={[0, Math.PI / 2, 0]}>
            <planeGeometry args={[PATH_DEPTH + 7, PATH_HEIGHT + 2]} />
            <meshBasicMaterial color={LATE} transparent opacity={0.1} side={THREE.DoubleSide} depthWrite={false} />
          </mesh>
          <Html position={[0, PATH_HEIGHT + 2.6, PATH_DEPTH / 2 + 2]} center distanceFactor={22} style={{ pointerEvents: 'none' }}>
            <div style={{ font: '700 12px Segoe UI, sans-serif', color: '#fca5a5', whiteSpace: 'nowrap' }}>Deadline · day {trace.deadlineDay}</div>
          </Html>
        </group>
      )}

      <Paths trace={trace} g={g} pos={pos} />
      <Balls trace={trace} g={g} pos={pos} />

      {conv && (
        <>
          <Pole day={conv.p50Days} label={`P50 · ${conv.p50Days}d`} color="#38bdf8" g={g} height={pileTop + 1} />
          <Pole day={conv.p80Days} label={`P80 · ${conv.p80Days}d`} color="#facc15" g={g} height={pileTop + 2.4} />
          <Pole day={conv.p95Days} label={`P95 · ${conv.p95Days}d`} color="#fb923c" g={g} height={pileTop + 3.8} />
        </>
      )}

      <OrbitControls target={center} maxPolarAngle={Math.PI / 2.05} enableDamping />
    </>
  )
}

/** Advances the shared play head; lives inside the Canvas so it runs on the render loop. */
function Clock({ pos, playing, speed, total, onProgress }: { pos: MutableRefObject<number>; playing: boolean; speed: number; total: number; onProgress: (k: number) => void }) {
  const last = useRef(-1)
  useFrame((_, dt) => {
    if (playing && pos.current < total) pos.current = Math.min(total, pos.current + dt * speed)
    const k = Math.floor(pos.current)
    if (k !== last.current) { last.current = k; onProgress(k) }
  })
  return null
}

export default function MonteCarlo3D({ trace, pos, playing, speed, conv, onProgress }: {
  trace: MonteCarloTrace; pos: MutableRefObject<number>; playing: boolean; speed: number
  conv: ConvergencePoint | null; onProgress: (k: number) => void
}) {
  return (
    <Canvas shadows camera={{ position: [6, 20, 26], fov: 45 }}>
      <Scene trace={trace} pos={pos} conv={conv} />
      <Clock pos={pos} playing={playing} speed={speed} total={trace.samples.length} onProgress={onProgress} />
    </Canvas>
  )
}
