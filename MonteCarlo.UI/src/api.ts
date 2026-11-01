// Typed client for the MonteCarlo .NET API (proxied at /api by Vite).

export type WorkItemStatus = 'Backlog' | 'Todo' | 'InProgress' | 'Blocked' | 'InReview' | 'Done' | 'Cancelled'
export type WorkRole = 'Backend' | 'Frontend' | 'FullStack' | 'QA' | 'DevOps' | 'Design' | 'Product'
export type SuggestionOutcome = 'Pending' | 'Accepted' | 'Edited' | 'Rejected'
export type CodeEventType = 'Commit' | 'PullRequestOpened' | 'PullRequestMerged' | 'BuildFailed' | 'BuildSucceeded' | 'Deployed'

export const STATUSES: WorkItemStatus[] = ['Backlog', 'Todo', 'InProgress', 'Blocked', 'InReview', 'Done', 'Cancelled']
export const ROLES: WorkRole[] = ['Backend', 'Frontend', 'FullStack', 'QA', 'DevOps', 'Design', 'Product']

export interface Project { id: string; key: string; name: string; description: string; startDate: string; deadline: string; currentRelease: string }

export interface Person {
  id: string; name: string; email: string; externalUserId?: string; role: WorkRole; skills: string; capacity: number
  startDate: string; leaveDate?: string; openItems: number; active: boolean
}

export interface WorkItem {
  id: string; externalRef: string; title: string; description: string; acceptanceCriteria: string; status: WorkItemStatus; role: WorkRole
  estimateDays?: number; actualDays?: number; ownerId?: string; ownerName?: string; release: string; priority: number
  lastActivityAt: string; startedAt?: string; completedAt?: string; dependsOn: string[]; dependsOnRefs: string[]
  qualityScore: number; qualityIssues: string[]; isStale: boolean; pendingSuggestions: number
}

export interface Evidence {
  id: string; sourceType: string; sourceId: string; meetingId?: string; meetingTitle?: string; offsetSeconds?: number
  timestamp: string; speakerName: string; quotedText: string; confidence: number
}

export interface Decision { id: string; statement: string; decidedByName: string; decidedAt: string; rationale: string; affectedWorkItemId?: string; affectedWorkItemRef?: string; meetingId?: string; evidence?: Evidence }
export interface ActionItem { id: string; description: string; ownerId?: string; ownerName: string; dueDate?: string; linkedWorkItemId?: string; linkedWorkItemRef?: string; state: string; meetingId?: string; evidence?: Evidence }
export interface Blocker { id: string; description: string; blockingWorkItemId?: string; blockingWorkItemRef?: string; raisedByName: string; raisedAt: string; resolvedAt?: string; meetingId?: string; evidence?: Evidence }

export interface Suggestion {
  id: string; type: string; title: string; detail: string; confidence: number; outcome: SuggestionOutcome; createdAt: string
  workItemId?: string; workItemRef?: string; workItemTitle?: string; currentStatus?: WorkItemStatus
  proposedTitle?: string; proposedStatus?: WorkItemStatus; proposedOwnerId?: string; proposedOwnerName?: string; proposedDueDate?: string
  proposedRelease?: string; proposedEstimateDays?: number; proposedRole?: WorkRole
  reviewedBy?: string; reviewedAt?: string; reviewNote?: string; evidence?: Evidence
}

export interface MeetingSummary { id: string; title: string; startedAt: string; source: string; processed: boolean; optedOut: boolean; rawDeleted: boolean; turns: number; decisions: number; actions: number; blockers: number }
export interface Turn { sequence: number; offsetSeconds: number; timestamp: string; speakerName: string; personId?: string; text: string }
export interface MeetingDetail {
  meeting: MeetingSummary; turns: Turn[]; decisions: Decision[]; actions: ActionItem[]; blockers: Blocker[]; suggestions: Suggestion[]
  extractions: { extractor: string; model: string; promptVersion: string; inputChars: number; durationMs: number; at: string }[]
}

export interface CodeEvent { id: string; type: CodeEventType; externalId: string; title: string; author: string; branch: string; ticketRefs: string[]; occurredAt: string; url: string }

export interface SearchHit { kind: string; id: string; text: string; score: number; at: string; who?: string; workItemRef?: string; evidence?: Evidence }
export interface SearchResult { query: string; answer: string; hits: SearchHit[] }

export interface RunSummary {
  id: string; name: string; createdAt: string; iterations: number
  baselineP50: string; baselineP80: string; baselineP95: string; scenarioP50: string; scenarioP80: string; scenarioP95: string
  baselineOnTime: number; scenarioOnTime: number; narrative: string
}

export interface Dashboard {
  project: Project; openItems: number; doneItems: number; blockedItems: number; pendingSuggestions: number
  ticketFreshness: number; suggestionPrecision?: number; meetingsProcessed: number; decisionsCaptured: number; actionItemsCaptured: number; openBlockers: number
  statusCounts: { status: WorkItemStatus; count: number }[]; staleItems: WorkItem[]; lowQualityItems: WorkItem[]
  recentDecisions: Decision[]; blockers: Blocker[]; latestForecast?: RunSummary
}

export interface Digest { weekOf: string; text: string; stale: WorkItem[]; pending: Suggestion[]; decisions: Decision[] }

export interface SimPerson { id: string; name: string; role: WorkRole; capacity: number; startDay: number; leaveDay?: number }
export interface SimTask { id: string; key: string; title: string; role: WorkRole; estimateDays: number; remainingFraction: number; dependsOn: string[]; priority: number; ownerId?: string; blockedUntilDay: number }
export interface RoleCalibration { meanRatio: number; logSigma: number; samples: number }
export interface Calibration { meanRatio: number; logSigma: number; samples: number; byRole: Record<string, RoleCalibration> }
export interface SimModel { projectId: string; projectName: string; startDate: string; deadline: string; release: string; people: SimPerson[]; tasks: SimTask[]; calibration: Calibration }

export interface ScenarioChange {
  type: 'PersonLeaves' | 'NewHire' | 'ScopeChange' | 'RemoveTask' | 'PrioritySwap' | 'MeetingLoad' | 'ClientDelay'
  personId?: string; taskId?: string; day?: number; knowledgeLoss?: number; name?: string; role?: WorkRole; capacity?: number
  rampUpDays?: number; addTasks?: number; effortDaysEach?: number; effortMultiplier?: number; newPriority?: number; capacityDelta?: number; untilDay?: number
}

export interface TaskForecast { id: string; key: string; title: string; p50Day: number; p80Day: number; p80Date: string; onCriticalPath: boolean }
export interface ForecastSummary { p50Days: number; p80Days: number; p95Days: number; p50: string; p80: string; p95: string; probabilityOnTime: number; meanDays: number; unfinishedRuns: number; tasks: TaskForecast[] }
export interface DayFrame { day: number; date: string; progress: number[]; taskState: number[]; personTask: number[] }
export interface ReplayTask { id: string; key: string; title: string; role: WorkRole; effortDays: number; dependsOn: number[]; onCriticalPath: boolean; addedByScenario: boolean }
export interface ReplayPerson { id: string; name: string; role: WorkRole; startDay: number; leaveDay?: number; isNewHire: boolean }
export interface Comparison {
  startDate: string; deadline?: string; deadlineDay?: number; baseline: ForecastSummary; scenario: ForecastSummary
  deltaP50Days: number; deltaP80Days: number; meanPairedDeltaDays: number
  histogram: { day: number; date: string; baseline: number; scenario: number }[]
  bottleneckRole: string; removedTasks: { id: string; key: string; title: string }[]
  replay: { iteration: number; finishDay: number; tasks: ReplayTask[]; people: ReplayPerson[]; frames: DayFrame[] }
}
export interface SimResult {
  runId: string; name: string; createdAt: string; iterations: number; seed: number; changes: ScenarioChange[]; changeDescriptions: string[]
  comparison: Comparison; mitigation?: { description: string; recoveredDays: number; lostDays: number; p80: string; probabilityOnTime: number }
  narrative: string; calibration: Calibration
}

// ---- Monte Carlo trace & risk landscape (computed in .NET, drawn in 3D) ----
export interface IterationSample { iteration: number; finishDay: number; finished: boolean; onTime: boolean; sampledWorkDays: number; estimateRatio: number; absentDays: number; lastTask: number; burndown?: number[] }
export interface DayCount { day: number; date: string; count: number; onTime: number }
export interface ConvergencePoint { iterations: number; p50Days: number; p80Days: number; p95Days: number; meanDays: number; onTime: number }
export interface TaskDriver { id: string; key: string; title: string; role: WorkRole; driverShare: number; p50Day: number; p80Day: number; onCriticalPath: boolean }
export interface MonteCarloTrace {
  startDate: string; deadline?: string; deadlineDay?: number; iterations: number; seed: number; plannedWorkDays: number
  calibrationMeanRatio: number; calibrationLogSigma: number; summary: ForecastSummary
  samples: IterationSample[]; distribution: DayCount[]; convergence: ConvergencePoint[]; drivers: TaskDriver[]
}
export interface MonteCarloTraceResult { name: string; changeDescriptions: string[]; elapsedMs: number; trace: MonteCarloTrace }
export type LandscapeAxisName = 'Capacity' | 'Scope' | 'Hires' | 'Absence'
export interface LandscapeAxis { name: LandscapeAxisName; label: string; values: number[]; labels: string[]; baselineIndex: number }
export interface LandscapeCell { xi: number; zi: number; p50Days: number; p80Days: number; p95Days: number; p80: string; probabilityOnTime: number }
export interface Landscape { startDate: string; deadline?: string; deadlineDay?: number; hireRole: string; iterations: number; x: LandscapeAxis; z: LandscapeAxis; cells: LandscapeCell[]; elapsedMs: number }

export interface Settings { teamName: string; retentionDays: number; captureEnabledByDefault: boolean; dataRegion: string; staleAfterDays: number; minConfidence: number; extractor: string; ticketSystem: string; codeSource: string; graphConfigured: boolean }
export interface AuditEntry { at: string; actor: string; action: string; entityType: string; entityId: string; details: string }

// ---- HTTP ----

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${url}`, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) {
    let msg = `${res.status} ${res.statusText}`
    try { const j = await res.json(); msg = j.detail || j.title || msg } catch { /* not json */ }
    throw new Error(msg)
  }
  if (res.status === 204) return undefined as T
  const type = res.headers.get('content-type') ?? ''
  return (type.includes('json') ? res.json() : res.text()) as Promise<T>
}

const get = <T,>(u: string) => request<T>('GET', u)
const post = <T,>(u: string, b?: unknown) => request<T>('POST', u, b ?? {})
const put = <T,>(u: string, b: unknown) => request<T>('PUT', u, b)
const del = (u: string) => request<void>('DELETE', u)

export const api = {
  projects: () => get<Project[]>('/projects'),
  updateProject: (id: string, p: Partial<Project>) => put<Project>(`/projects/${id}`, p),
  dashboard: () => get<Dashboard>('/dashboard'),
  digest: () => get<Digest>('/digest'),

  people: () => get<Person[]>('/people'),
  createPerson: (p: unknown) => post<Person>('/people', p),
  updatePerson: (id: string, p: unknown) => put<Person>(`/people/${id}`, p),
  deletePerson: (id: string) => del(`/people/${id}`),

  workItems: () => get<WorkItem[]>('/workitems'),
  workItem: (id: string) => get<{ item: WorkItem; timeline: { at: string; kind: string; text: string; source?: string; evidence?: Evidence }[] }>(`/workitems/${id}`),
  createWorkItem: (w: unknown) => post<WorkItem>('/workitems', w),
  updateWorkItem: (id: string, w: unknown) => put<WorkItem>(`/workitems/${id}`, w),
  setStatus: (id: string, status: WorkItemStatus) => post<WorkItem>(`/workitems/${id}/status`, { status, actor: 'board' }),
  deleteWorkItem: (id: string) => del(`/workitems/${id}`),

  meetings: () => get<MeetingSummary[]>('/meetings'),
  meeting: (id: string) => get<MeetingDetail>(`/meetings/${id}`),
  ingest: (r: { title: string; startedAt?: string; content: string; optOut?: boolean }) => post<MeetingDetail>('/meetings', r),
  reprocess: (id: string) => post<MeetingDetail>(`/meetings/${id}/reprocess`),
  deleteMeeting: (id: string) => del(`/meetings/${id}`),
  sampleTranscript: () => get<string>('/meetings/sample'),

  suggestions: (outcome?: SuggestionOutcome) => get<Suggestion[]>(`/suggestions${outcome ? `?outcome=${outcome}` : ''}`),
  accept: (id: string, edits: Record<string, unknown> = {}) => post<Suggestion>(`/suggestions/${id}/accept`, { reviewer: 'You', ...edits }),
  reject: (id: string, note?: string) => post<Suggestion>(`/suggestions/${id}/reject`, { reviewer: 'You', note }),
  reconcile: () => post<{ created: number; suggestions: Suggestion[] }>('/reconcile'),

  codeEvents: () => get<CodeEvent[]>('/code-events'),
  addCodeEvent: (e: unknown) => post<CodeEvent>('/code-events', e),

  search: (q: string) => get<SearchResult>(`/search?q=${encodeURIComponent(q)}`),

  simModel: (allReleases = false) => get<SimModel>(`/simulation/model?allReleases=${allReleases}`),
  runSim: (r: unknown) => post<SimResult>('/simulation/run', r),
  simRuns: () => get<RunSummary[]>('/simulation/runs'),
  simRun: (id: string) => get<SimResult>(`/simulation/runs/${id}`),
  deleteSimRun: (id: string) => del(`/simulation/runs/${id}`),
  monteCarlo: (r: { changes?: ScenarioChange[]; iterations: number; pathCount: number; seed: number; absenceRate: number }) => post<MonteCarloTraceResult>('/simulation/montecarlo', r),
  landscape: (r: { xAxis: LandscapeAxisName; zAxis: LandscapeAxisName; steps: number; iterations: number; seed: number; absenceRate: number }) => post<Landscape>('/simulation/landscape', r),

  settings: () => get<Settings>('/settings'),
  updateSettings: (s: Partial<Settings>) => put<Settings>('/settings', s),
  purge: () => post<{ purged: number }>('/settings/purge'),
  audit: () => get<AuditEntry[]>('/audit?take=100'),
}

// ---- Formatting helpers ----

export const fmtDate = (s?: string) => (s ? new Date(s.length === 10 ? s + 'T00:00:00' : s).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : '—')
export const fmtShort = (s?: string) => (s ? new Date(s.length === 10 ? s + 'T00:00:00' : s).toLocaleDateString(undefined, { day: 'numeric', month: 'short' }) : '—')
export const fmtDateTime = (s?: string) => (s ? new Date(s).toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—')
export const pct = (v?: number) => (v === undefined || v === null ? '—' : `${Math.round(v * 100)}%`)
export const statusLabel = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')
export function ago(s: string) {
  const d = (Date.now() - new Date(s).getTime()) / 86400000
  if (d < 1 / 24) return 'just now'
  if (d < 1) return `${Math.round(d * 24)}h ago`
  if (d < 2) return 'yesterday'
  return `${Math.round(d)} days ago`
}
