import { useMemo, useRef } from 'react'
import { Canvas, useFrame } from '@react-three/fiber'
import { Html, OrbitControls } from '@react-three/drei'
import * as THREE from 'three'
import type { Comparison, WorkRole } from '../api'

// Metaphor: tasks are buildings that rise as work completes; people are figures moving
// between them; blocked tasks are red; the critical path is a lit road.

const STATE_COLOR = ['#475569', '#7dd3fc', '#3b82f6', '#22c55e', '#ef4444'] // waiting, ready, active, done, blocked
const ROLE_COLOR: Record<WorkRole, string> = {
  Backend: '#f97316', Frontend: '#a855f7', FullStack: '#eab308', QA: '#14b8a6', DevOps: '#ec4899', Design: '#84cc16', Product: '#e2e8f0',
}
const SPACING_X = 4.2
const SPACING_Z = 3.4

interface Layout { pos: THREE.Vector3[]; height: number[]; center: THREE.Vector3; size: number }

function useLayout(replay: Comparison['replay']): Layout {
  return useMemo(() => {
    const n = replay.tasks.length
    const depth = new Array<number>(n).fill(-1)
    const visit = (i: number, guard = 0): number => {
      if (depth[i] >= 0) return depth[i]
      if (guard > n) return 0
      const d = replay.tasks[i].dependsOn.length === 0 ? 0 : Math.max(...replay.tasks[i].dependsOn.map((x) => visit(x, guard + 1) + 1))
      depth[i] = d
      return d
    }
    for (let i = 0; i < n; i++) visit(i)
    const perLevel = new Map<number, number[]>()
    depth.forEach((d, i) => perLevel.set(d, [...(perLevel.get(d) ?? []), i]))
    const pos: THREE.Vector3[] = new Array(n)
    perLevel.forEach((ids, d) => {
      // critical-path tasks go in the middle row so the lit road is straight-ish
      ids.sort((a, b) => Number(replay.tasks[b].onCriticalPath) - Number(replay.tasks[a].onCriticalPath))
      ids.forEach((id, k) => {
        const offset = k === 0 ? 0 : (k % 2 === 1 ? 1 : -1) * Math.ceil(k / 2)
        pos[id] = new THREE.Vector3(d * SPACING_X, 0, offset * SPACING_Z)
      })
    })
    const maxEffort = Math.max(1, ...replay.tasks.map((t) => t.effortDays))
    const height = replay.tasks.map((t) => 0.8 + (t.effortDays / maxEffort) * 4.5)
    const box = new THREE.Box3().setFromPoints(pos.length ? pos : [new THREE.Vector3()])
    const center = box.getCenter(new THREE.Vector3())
    const size = Math.max(10, box.getSize(new THREE.Vector3()).length())
    return { pos, height, center, size }
  }, [replay])
}

function Building({ p, h, progress, state, label, critical, added }: { p: THREE.Vector3; h: number; progress: number; state: number; label: string; critical: boolean; added: boolean }) {
  const ref = useRef<THREE.Mesh>(null)
  const target = Math.max(0.04, progress)
  useFrame((_, dt) => {
    if (!ref.current) return
    const s = ref.current.scale.y + (target - ref.current.scale.y) * Math.min(1, dt * 8)
    ref.current.scale.y = s
    ref.current.position.y = (h * s) / 2
  })
  const color = STATE_COLOR[state] ?? STATE_COLOR[0]
  return (
    <group position={[p.x, 0, p.z]}>
      {/* foundation */}
      <mesh position={[0, 0.03, 0]} receiveShadow>
        <boxGeometry args={[2.1, 0.06, 2.1]} />
        <meshStandardMaterial color={critical ? '#facc15' : '#1e293b'} emissive={critical ? '#a16207' : '#000000'} emissiveIntensity={critical ? 0.6 : 0} />
      </mesh>
      {/* planned height ghost */}
      <mesh position={[0, h / 2, 0]}>
        <boxGeometry args={[1.6, h, 1.6]} />
        <meshBasicMaterial color={added ? '#f59e0b' : '#94a3b8'} wireframe transparent opacity={0.25} />
      </mesh>
      {/* built part */}
      <mesh ref={ref} castShadow scale={[1, 0.04, 1]}>
        <boxGeometry args={[1.6, h, 1.6]} />
        <meshStandardMaterial color={color} emissive={state === 2 ? '#1d4ed8' : state === 4 ? '#7f1d1d' : '#000000'} emissiveIntensity={state === 2 || state === 4 ? 0.45 : 0} roughness={0.55} metalness={0.1} />
      </mesh>
      <Html position={[0, h + 0.6, 0]} center distanceFactor={14} style={{ pointerEvents: 'none' }}>
        <div style={{ font: '600 11px Segoe UI, sans-serif', color: '#e2e8f0', background: 'rgba(15,23,42,.75)', padding: '1px 5px', borderRadius: 4, whiteSpace: 'nowrap' }}>
          {label} {Math.round(progress * 100)}%
        </div>
      </Html>
    </group>
  )
}

function Figure({ target, color, faded, name }: { target: THREE.Vector3; color: string; faded: boolean; name: string }) {
  const ref = useRef<THREE.Group>(null)
  useFrame((state, dt) => {
    if (!ref.current) return
    ref.current.position.lerp(target, Math.min(1, dt * 4))
    ref.current.position.y = Math.abs(Math.sin(state.clock.elapsedTime * 6 + target.x)) * 0.08
  })
  return (
    <group ref={ref} position={target.clone()}>
      <mesh position={[0, 0.45, 0]} castShadow>
        <cylinderGeometry args={[0.22, 0.28, 0.7, 12]} />
        <meshStandardMaterial color={color} transparent opacity={faded ? 0.3 : 1} />
      </mesh>
      <mesh position={[0, 0.98, 0]} castShadow>
        <sphereGeometry args={[0.2, 16, 16]} />
        <meshStandardMaterial color="#f1d3b3" transparent opacity={faded ? 0.3 : 1} />
      </mesh>
      <Html position={[0, 1.45, 0]} center distanceFactor={14} style={{ pointerEvents: 'none' }}>
        <div style={{ font: '500 10px Segoe UI, sans-serif', color, whiteSpace: 'nowrap', textShadow: '0 0 3px #000' }}>{name.split(' ')[0]}{faded ? ' (away)' : ''}</div>
      </Html>
    </group>
  )
}

function Roads({ layout, replay }: { layout: Layout; replay: Comparison['replay'] }) {
  const segments = useMemo(() => {
    const out: { a: THREE.Vector3; b: THREE.Vector3; critical: boolean }[] = []
    replay.tasks.forEach((t, i) => t.dependsOn.forEach((d) => {
      out.push({ a: layout.pos[d], b: layout.pos[i], critical: t.onCriticalPath && replay.tasks[d].onCriticalPath })
    }))
    return out
  }, [layout, replay])

  return (
    <>
      {segments.map((s, i) => {
        const mid = s.a.clone().add(s.b).multiplyScalar(0.5)
        const len = s.a.distanceTo(s.b)
        const angle = Math.atan2(s.b.z - s.a.z, s.b.x - s.a.x)
        return (
          <mesh key={i} position={[mid.x, 0.02, mid.z]} rotation={[-Math.PI / 2, 0, -angle]}>
            <planeGeometry args={[len, s.critical ? 0.55 : 0.18]} />
            <meshStandardMaterial color={s.critical ? '#fde047' : '#334155'} emissive={s.critical ? '#facc15' : '#000000'} emissiveIntensity={s.critical ? 1.1 : 0} />
          </mesh>
        )
      })}
    </>
  )
}

export default function Sim3D({ comparison, frameIndex }: { comparison: Comparison; frameIndex: number }) {
  const { replay } = comparison
  const layout = useLayout(replay)
  const frame = replay.frames[Math.min(frameIndex, replay.frames.length - 1)]

  // Idle plaza in front of the city.
  const plaza = (k: number) => new THREE.Vector3(layout.center.x - 3 + (k % 6) * 1.1, 0, layout.center.z + layout.size * 0.42 + Math.floor(k / 6) * 1.2)

  const people = replay.people.map((p, k) => {
    const code = frame ? frame.personTask[k] : -1
    if (code === -3) return null
    let target: THREE.Vector3
    if (code >= 0 && layout.pos[code]) {
      const angle = (k / Math.max(1, replay.people.length)) * Math.PI * 2
      target = layout.pos[code].clone().add(new THREE.Vector3(Math.cos(angle) * 1.4, 0, Math.sin(angle) * 1.4))
    } else {
      target = plaza(k)
    }
    return <Figure key={p.id} target={target} color={ROLE_COLOR[p.role] ?? '#fff'} faded={code === -2} name={p.name} />
  })

  const cam = new THREE.Vector3(layout.center.x - layout.size * 0.15, layout.size * 0.65, layout.center.z + layout.size * 0.9)

  return (
    <Canvas shadows camera={{ position: cam.toArray(), fov: 45 }}>
      <color attach="background" args={['#0b1220']} />
      <fog attach="fog" args={['#0b1220', layout.size * 1.2, layout.size * 3.5]} />
      <ambientLight intensity={0.45} />
      <directionalLight position={[layout.center.x + 15, 25, layout.center.z + 10]} intensity={1.3} castShadow shadow-mapSize={[1024, 1024]} />
      <hemisphereLight args={['#93c5fd', '#0f172a', 0.35]} />

      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[layout.center.x, 0, layout.center.z]} receiveShadow>
        <planeGeometry args={[layout.size * 3, layout.size * 3]} />
        <meshStandardMaterial color="#111a2b" />
      </mesh>
      <gridHelper args={[layout.size * 3, Math.round(layout.size), '#1e293b', '#16213a']} position={[layout.center.x, 0.01, layout.center.z]} />

      <Roads layout={layout} replay={replay} />
      {replay.tasks.map((t, i) => (
        <Building key={t.id} p={layout.pos[i]} h={layout.height[i]} progress={frame?.progress[i] ?? 0} state={frame?.taskState[i] ?? 0}
          label={t.key} critical={t.onCriticalPath} added={t.addedByScenario} />
      ))}
      {people}

      <OrbitControls target={layout.center} maxPolarAngle={Math.PI / 2.1} enableDamping />
    </Canvas>
  )
}

export { STATE_COLOR, ROLE_COLOR }
