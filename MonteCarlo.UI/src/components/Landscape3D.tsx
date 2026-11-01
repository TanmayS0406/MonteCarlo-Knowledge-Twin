import { useEffect, useMemo, useState } from 'react'
import { Canvas } from '@react-three/fiber'
import { Html, OrbitControls } from '@react-three/drei'
import * as THREE from 'three'
import type { Landscape, LandscapeCell } from '../api'
import { fmtShort, pct } from '../api'

// Each grid point is a full Monte Carlo forecast run in .NET (SimulationService.LandscapeAsync).
// Height = P80 finish day, colour = probability of hitting the deadline, red plane = the deadline.

const GAP = 2.4
const HEIGHT = 11

/** Red (0%) to amber (50%) to green (100%). */
export function onTimeColor(p: number) {
  const red = new THREE.Color('#ef4444'), amber = new THREE.Color('#f59e0b'), green = new THREE.Color('#22c55e')
  return p < 0.5 ? red.lerp(amber, p / 0.5) : amber.lerp(green, (p - 0.5) / 0.5)
}

const label = (text: string, color = '#94a3b8', weight = 500) => (
  <div style={{ font: `${weight} 11px Segoe UI, sans-serif`, color, whiteSpace: 'nowrap' }}>{text}</div>
)

function Surface({ data, onHover, hovered }: { data: Landscape; onHover: (c: LandscapeCell | null) => void; hovered: LandscapeCell | null }) {
  const nx = data.x.values.length
  const nz = data.z.values.length
  const maxDay = Math.max(1, ...data.cells.map((c) => c.p80Days), data.deadlineDay ?? 0)
  const hs = HEIGHT / maxDay
  const cell = (xi: number, zi: number) => data.cells.find((c) => c.xi === xi && c.zi === zi)!

  const geometry = useMemo(() => {
    const pos = new Float32Array(nx * nz * 3)
    const col = new Float32Array(nx * nz * 3)
    for (let zi = 0; zi < nz; zi++)
      for (let xi = 0; xi < nx; xi++) {
        const c = data.cells.find((q) => q.xi === xi && q.zi === zi)!
        const k = zi * nx + xi
        pos.set([xi * GAP, c.p80Days * hs, zi * GAP], k * 3)
        const color = onTimeColor(c.probabilityOnTime)
        col.set([color.r, color.g, color.b], k * 3)
      }
    const idx: number[] = []
    for (let zi = 0; zi < nz - 1; zi++)
      for (let xi = 0; xi < nx - 1; xi++) {
        const a = zi * nx + xi, b = a + 1, c = a + nx, d = c + 1
        idx.push(a, c, b, b, c, d)
      }
    const g = new THREE.BufferGeometry()
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3))
    g.setAttribute('color', new THREE.BufferAttribute(col, 3))
    g.setIndex(idx)
    g.computeVertexNormals()
    return g
  }, [data, nx, nz, hs])
  useEffect(() => () => geometry.dispose(), [geometry])

  const w = (nx - 1) * GAP
  const dpt = (nz - 1) * GAP
  const base = cell(data.x.baselineIndex, data.z.baselineIndex)

  return (
    <group position={[-w / 2, 0, -dpt / 2]}>
      <mesh geometry={geometry} castShadow receiveShadow>
        <meshStandardMaterial vertexColors side={THREE.DoubleSide} roughness={0.6} metalness={0.05} />
      </mesh>
      <mesh geometry={geometry}>
        <meshBasicMaterial color="#0f172a" wireframe transparent opacity={0.35} />
      </mesh>

      {/* drop lines + pickable points */}
      {data.cells.map((c) => {
        const y = c.p80Days * hs
        const isBase = c === base
        const isHover = hovered === c
        return (
          <group key={`${c.xi}-${c.zi}`} position={[c.xi * GAP, 0, c.zi * GAP]}>
            <mesh position={[0, y / 2, 0]}>
              <cylinderGeometry args={[0.02, 0.02, y, 4]} />
              <meshBasicMaterial color="#334155" transparent opacity={0.5} />
            </mesh>
            <mesh position={[0, y, 0]}
              onPointerOver={(e) => { e.stopPropagation(); onHover(c) }}
              onPointerOut={() => onHover(null)}>
              <sphereGeometry args={[isBase ? 0.32 : isHover ? 0.26 : 0.16, 16, 16]} />
              <meshStandardMaterial color={isBase ? '#ffffff' : onTimeColor(c.probabilityOnTime)} emissive={isBase || isHover ? '#ffffff' : '#000000'} emissiveIntensity={isBase ? 0.6 : 0.4} />
            </mesh>
            {isBase && <Html position={[0, y + 0.8, 0]} center distanceFactor={18} style={{ pointerEvents: 'none' }}>{label("Today's plan", '#ffffff', 700)}</Html>}
          </group>
        )
      })}

      {/* deadline plane */}
      {data.deadlineDay && (
        <>
          <mesh position={[w / 2, data.deadlineDay * hs, dpt / 2]} rotation={[-Math.PI / 2, 0, 0]}>
            <planeGeometry args={[w + 2, dpt + 2]} />
            <meshBasicMaterial color="#ef4444" transparent opacity={0.16} side={THREE.DoubleSide} depthWrite={false} />
          </mesh>
          <Html position={[w + 1.6, data.deadlineDay * hs, -1]} distanceFactor={18} style={{ pointerEvents: 'none' }}>
            {label(`Deadline · day ${data.deadlineDay}${data.deadline ? ` (${fmtShort(data.deadline)})` : ''}`, '#fca5a5', 700)}
          </Html>
        </>
      )}

      {/* axes */}
      {data.x.labels.map((l, i) => (
        <Html key={`x${i}`} position={[i * GAP, 0, dpt + 1.2]} center distanceFactor={20} style={{ pointerEvents: 'none' }}>{label(l)}</Html>
      ))}
      <Html position={[w / 2, 0, dpt + 2.6]} center distanceFactor={20} style={{ pointerEvents: 'none' }}>{label(`${data.x.label} →`, '#e2e8f0', 700)}</Html>
      {data.z.labels.map((l, i) => (
        <Html key={`z${i}`} position={[-1.3, 0, i * GAP]} center distanceFactor={20} style={{ pointerEvents: 'none' }}>{label(l)}</Html>
      ))}
      <Html position={[-3.2, 0, dpt / 2]} center distanceFactor={20} style={{ pointerEvents: 'none' }}>{label(`${data.z.label} →`, '#e2e8f0', 700)}</Html>
      <Html position={[-1.3, HEIGHT + 0.6, -0.6]} center distanceFactor={20} style={{ pointerEvents: 'none' }}>{label('↑ P80 finish (working days)', '#e2e8f0', 700)}</Html>

      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[w / 2, -0.01, dpt / 2]} receiveShadow>
        <planeGeometry args={[w + 8, dpt + 8]} />
        <meshStandardMaterial color="#111a2b" />
      </mesh>
      <gridHelper args={[w + 8, 16, '#1e293b', '#16213a']} position={[w / 2, 0, dpt / 2]} />
    </group>
  )
}

export default function Landscape3D({ data }: { data: Landscape }) {
  const [hovered, setHovered] = useState<LandscapeCell | null>(null)
  const base = data.cells.find((c) => c.xi === data.x.baselineIndex && c.zi === data.z.baselineIndex)
  return (
    <div className="canvas-wrap">
      <Canvas shadows camera={{ position: [16, 17, 20], fov: 45 }}>
        <color attach="background" args={['#0b1220']} />
        <ambientLight intensity={0.55} />
        <directionalLight position={[12, 25, 14]} intensity={1.2} castShadow />
        <hemisphereLight args={['#93c5fd', '#0f172a', 0.35]} />
        <Surface data={data} onHover={setHovered} hovered={hovered} />
        <OrbitControls target={[0, 3, 0]} maxPolarAngle={Math.PI / 2.05} enableDamping />
      </Canvas>
      <div className="canvas-legend">
        <span><i style={{ background: '#ef4444' }} />0% on time</span>
        <span><i style={{ background: '#f59e0b' }} />50%</span>
        <span><i style={{ background: '#22c55e' }} />100%</span>
        <span><i style={{ background: '#fff' }} />today's plan</span>
      </div>
      <div className="canvas-hud">
        {hovered ? (
          <>
            <div style={{ fontWeight: 700 }}>{data.x.label}: {data.x.labels[hovered.xi]} · {data.z.label}: {data.z.labels[hovered.zi]}</div>
            <div>P50 {hovered.p50Days}d · P80 {hovered.p80Days}d ({fmtShort(hovered.p80)}) · P95 {hovered.p95Days}d</div>
            <div>On time: <b>{pct(hovered.probabilityOnTime)}</b>{base && <> · vs plan {hovered.p80Days - base.p80Days >= 0 ? '+' : ''}{hovered.p80Days - base.p80Days}d</>}</div>
          </>
        ) : (
          <>
            <div style={{ fontWeight: 700 }}>{data.cells.length} forecasts × {data.iterations.toLocaleString()} runs</div>
            <div>computed in .NET in {data.elapsedMs.toLocaleString()} ms</div>
            <div className="small">Point at the surface for details</div>
          </>
        )}
      </div>
    </div>
  )
}
