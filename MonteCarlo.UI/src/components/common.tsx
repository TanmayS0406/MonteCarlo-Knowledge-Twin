import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { Evidence, WorkItemStatus } from '../api'
import { statusLabel } from '../api'

/** Loads data on mount and exposes reload. */
export function useLoad<T>(loader: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const load = useCallback(loader, deps)
  const reload = useCallback(async () => {
    setLoading(true)
    try { setData(await load()); setError(null) } catch (e) { setError((e as Error).message) } finally { setLoading(false) }
  }, [load])
  useEffect(() => { reload() }, [reload])
  return { data, error, loading, reload, setData }
}

export function ErrorBox({ error }: { error?: string | null }) {
  return error ? <div className="error">{error}</div> : null
}

export function Loading({ what = 'Loading' }: { what?: string }) {
  return <div className="empty">{what}…</div>
}

const statusTone: Record<WorkItemStatus, string> = {
  Backlog: '', Todo: '', InProgress: 'info', Blocked: 'bad', InReview: 'warn', Done: 'ok', Cancelled: '',
}

export function StatusBadge({ status }: { status?: WorkItemStatus }) {
  if (!status) return null
  return <span className={`badge ${statusTone[status]}`}>{statusLabel(status)}</span>
}

export function Confidence({ value }: { value: number }) {
  const tone = value >= 0.8 ? 'ok' : value >= 0.6 ? 'info' : 'warn'
  return <span className={`badge ${tone}`} title="Extraction / rule confidence">{Math.round(value * 100)}% sure</span>
}

/** The quoted line a fact came from, with a link back to the exact moment in the meeting. */
export function EvidenceQuote({ evidence }: { evidence?: Evidence }) {
  if (!evidence) return null
  return (
    <div className="quote">
      “{evidence.quotedText}”
      <div className="meta">
        {evidence.speakerName && <>{evidence.speakerName} · </>}
        {evidence.meetingId
          ? <Link to={`/meetings/${evidence.meetingId}#t${evidence.offsetSeconds ?? 0}`}>{evidence.meetingTitle ?? 'Meeting'} at {evidence.timestamp}</Link>
          : evidence.sourceType}
      </div>
    </div>
  )
}

export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: string; tone?: 'ok' | 'warn' | 'bad' }) {
  return (
    <div className="card stat">
      <div className="label">{label}</div>
      <div className="value" style={tone ? { color: `var(--${tone})` } : undefined}>{value}</div>
      {hint && <div className="hint">{hint}</div>}
    </div>
  )
}
