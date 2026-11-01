using MonteCarlo.Domain.Common;

namespace MonteCarlo.Domain.Simulation;

/// <summary>
/// Discrete-day simulation played forward one working day at a time, repeated many times (Monte Carlo)
/// with random estimate errors and absences. Pure C#, no I/O.
/// </summary>
public static class MonteCarloEngine
{
    // ---- Public API -----------------------------------------------------------------------------

    public static SimulationComparison Compare(SimModel model, Scenario scenario, Calibration calibration, SimulationOptions options)
    {
        var start = WorkingCalendar.FirstWorkingDay(model.StartDate);
        var pb = Prepare(model, Scenario.Baseline, calibration);
        var ps = Prepare(model, scenario, calibration);

        var baseRuns = RunMany(pb, calibration, options);
        var scenRuns = RunMany(ps, calibration, options);

        var baseline = Summarize(pb, baseRuns, start, model.Deadline);
        var scen = Summarize(ps, scenRuns, start, model.Deadline);

        // Paired (common random numbers) difference: same luck, so this is the scenario's effect.
        var paired = 0.0;
        for (var i = 0; i < baseRuns.Length; i++) paired += scenRuns[i].FinishDay - baseRuns[i].FinishDay;
        paired /= Math.Max(1, baseRuns.Length);

        // Shared histogram bins.
        var all = baseRuns.Select(r => r.FinishDay).Concat(scenRuns.Select(r => r.FinishDay)).ToArray();
        var min = all.Min();
        var max = all.Max();
        var width = Math.Max(1, (int)Math.Ceiling((max - min + 1) / 30.0));
        var bins = new List<HistogramBin>();
        for (var d = min; d <= max; d += width)
        {
            var lo = d;
            var hi = d + width;
            bins.Add(new HistogramBin(lo, FinishDate(start, lo),
                baseRuns.Count(r => r.FinishDay >= lo && r.FinishDay < hi),
                scenRuns.Count(r => r.FinishDay >= lo && r.FinishDay < hi)));
        }

        // Typical run = the iteration whose finish is closest to the scenario P50.
        var typical = Enumerable.Range(0, scenRuns.Length)
            .OrderBy(i => Math.Abs(scenRuns[i].FinishDay - scen.P50Days)).ThenBy(i => i).First();
        var replayTrace = RunOnce(ps, calibration, options, typical, record: true);
        var replay = new ReplayRun(
            typical,
            replayTrace.FinishDay,
            Enumerable.Range(0, ps.N).Select(i => new ReplayTask(ps.TaskIds[i], ps.Keys[i], ps.Titles[i], ps.Roles[i],
                Math.Round(replayTrace.Effort[i], 2), ps.Deps[i], ps.OnCriticalPath[i], ps.Added[i])).ToList(),
            Enumerable.Range(0, ps.M).Select(k => new ReplayPerson(ps.PersonIds[k], ps.Names[k], ps.PRoles[k], ps.Start[k],
                ps.Leave[k] == int.MaxValue ? null : ps.Leave[k], ps.NewHire[k])).ToList(),
            replayTrace.Frames!);

        int? deadlineDay = model.Deadline is { } dl ? WorkingCalendar.WorkingDaysBetween(start, dl) + 1 : null;
        var removed = model.Tasks.Where(t => !ps.TaskIds.Contains(t.Id))
            .Select(t => new RemovedTask(t.Id, t.Key, t.Title)).ToList();

        return new SimulationComparison(start, model.Deadline, deadlineDay, baseline, scen,
            scen.P50Days - baseline.P50Days, scen.P80Days - baseline.P80Days, Math.Round(paired, 2),
            bins, Bottleneck(ps, calibration).ToString(), removed, replay);
    }

    /// <summary>Forecast a single scenario (no replay). Used for quick mitigation checks.</summary>
    public static ForecastSummary Forecast(SimModel model, Scenario scenario, Calibration calibration, SimulationOptions options)
    {
        var p = Prepare(model, scenario, calibration);
        return Summarize(p, RunMany(p, calibration, options), WorkingCalendar.FirstWorkingDay(model.StartDate), model.Deadline);
    }

    /// <summary>
    /// Full Monte Carlo trace for the 3D view: every iteration's outcome, the burn-down path of the first
    /// <paramref name="pathCount"/> iterations, the exact finish-day distribution, convergence of the
    /// percentiles as iterations accumulate, and which tasks most often decide the finish date.
    /// </summary>
    public static MonteCarloTrace Explore(SimModel model, Scenario scenario, Calibration calibration, SimulationOptions options, int pathCount = 200)
    {
        var start = WorkingCalendar.FirstWorkingDay(model.StartDate);
        var p = Prepare(model, scenario, calibration);
        var count = Math.Max(1, options.Iterations);
        pathCount = Math.Clamp(pathCount, 0, count);
        var runs = new Trace[count];
        Parallel.For(0, count, i => runs[i] = RunOnce(p, calibration, options, i, record: false, burn: i < pathCount));

        var summary = Summarize(p, runs, start, model.Deadline);
        int? deadlineDay = model.Deadline is { } dl ? WorkingCalendar.WorkingDaysBetween(start, dl) + 1 : null;
        bool OnTime(Trace r) => model.Deadline is not { } d || (r.Finished && FinishDate(start, r.FinishDay) <= d);

        var planned = 0.0;
        for (var t = 0; t < p.N; t++) planned += p.Estimate[t] * p.RemainingFraction[t];

        var samples = new IterationSample[count];
        var driverCount = new int[p.N];
        for (var i = 0; i < count; i++)
        {
            var r = runs[i];
            var work = 0.0;
            for (var t = 0; t < p.N; t++) work += r.Effort[t] * p.RemainingFraction[t];
            // The task that finished last is the one that set this run's finish date.
            var last = -1;
            for (var t = 0; t < p.N; t++) if (r.DoneDay[t] >= 0 && (last < 0 || r.DoneDay[t] > r.DoneDay[last])) last = t;
            // Walk back through the dependency that finished latest: this run's own critical chain.
            for (var t = last; t >= 0;)
            {
                driverCount[t]++;
                var next = -1;
                foreach (var d in p.Deps[t]) if (r.DoneDay[d] >= 0 && (next < 0 || r.DoneDay[d] > r.DoneDay[next])) next = d;
                t = next;
            }
            samples[i] = new IterationSample(i, r.FinishDay, r.Finished, OnTime(r), Math.Round(work, 2),
                Math.Round(planned > 0 ? work / planned : 1, 3), r.AbsentDays, last, r.Burn?.ToArray());
        }

        var distribution = runs.GroupBy(r => r.FinishDay).OrderBy(g => g.Key)
            .Select(g => new DayCount(g.Key, FinishDate(start, g.Key), g.Count(), g.Count(OnTime))).ToList();

        // Convergence: percentiles recomputed as iterations are added, in iteration order.
        var convergence = new List<ConvergencePoint>();
        var step = Math.Max(1, count / 50);
        var sorted = new List<int>(count);
        var onTimeSoFar = 0;
        for (var i = 0; i < count; i++)
        {
            var f = runs[i].FinishDay;
            var at = sorted.BinarySearch(f);
            sorted.Insert(at < 0 ? ~at : at, f);
            if (OnTime(runs[i])) onTimeSoFar++;
            if ((i + 1) % step == 0 || i == count - 1)
            {
                var arr = sorted.ToArray();
                convergence.Add(new ConvergencePoint(i + 1, Percentile(arr, 0.5), Percentile(arr, 0.8), Percentile(arr, 0.95),
                    Math.Round(arr.Average(), 2), Math.Round(onTimeSoFar / (double)(i + 1), 4)));
            }
        }

        var drivers = Enumerable.Range(0, p.N)
            .Select(t => new TaskDriver(p.TaskIds[t], p.Keys[t], p.Titles[t], p.Roles[t], Math.Round(driverCount[t] / (double)count, 4),
                summary.Tasks[t].P50Day, summary.Tasks[t].P80Day, p.OnCriticalPath[t]))
            .OrderByDescending(d => d.DriverShare).ThenByDescending(d => d.P80Day).ToList();

        return new MonteCarloTrace(start, model.Deadline, deadlineDay, count, options.Seed, Math.Round(planned, 2),
            calibration.Default.MeanRatio, calibration.Default.LogSigma, summary, samples, distribution, convergence, drivers);
    }

    public static DateOnly FinishDate(DateOnly start, int finishDay) =>
        WorkingCalendar.AddWorkingDays(start, Math.Max(0, finishDay - 1));

    // ---- Preparation ----------------------------------------------------------------------------

    private sealed class Prepared
    {
        public int N, M;
        public DateOnly StartDate;
        public Guid[] TaskIds = [];
        public string[] Keys = [], Titles = [];
        public WorkRole[] Roles = [];
        public double[] Estimate = [], RemainingFraction = [], Score = [];
        public int[][] Deps = [];
        public int[] Priority = [], Owner = [], BlockedUntil = [];
        public ulong[] TaskStream = [];
        public bool[] OnCriticalPath = [], Added = [];

        public Guid[] PersonIds = [];
        public string[] Names = [];
        public WorkRole[] PRoles = [];
        public double[] Capacity = [], KLoss = [];
        public int[] Start = [], Leave = [], Ramp = [];
        public ulong[] PStream = [];
        public bool[] NewHire = [];
    }

    private static Prepared Prepare(SimModel model, Scenario scenario, Calibration cal)
    {
        var tasks = model.Tasks.ToList();
        var people = model.People.ToList();
        var scenarioIndex = 0;

        foreach (var change in scenario.Changes)
        {
            scenarioIndex++;
            switch (change)
            {
                case RemoveTask r:
                    tasks.RemoveAll(t => t.Id == r.TaskId);
                    break;
                case PrioritySwap ps:
                    tasks = tasks.Select(t => t.Id == ps.TaskId ? t with { Priority = Math.Clamp(ps.NewPriority, 1, 5) } : t).ToList();
                    break;
                case ClientDelay cd:
                    tasks = tasks.Select(t => t.Id == cd.TaskId ? t with { BlockedUntilDay = Math.Max(t.BlockedUntilDay, cd.UntilDay) } : t).ToList();
                    break;
                case ScopeChange sc:
                    if (Math.Abs(sc.EffortMultiplier - 1.0) > 1e-9)
                        tasks = tasks.Select(t => sc.Role is null || t.Role == sc.Role
                            ? t with { EstimateDays = t.EstimateDays * sc.EffortMultiplier } : t).ToList();
                    for (var i = 0; i < sc.AddTasks; i++)
                    {
                        var id = Crn.DeterministicGuid($"scope:{scenarioIndex}:{i}");
                        tasks.Add(new SimTask(id, $"NEW-{scenarioIndex}.{i + 1}", $"Added scope #{i + 1}",
                            sc.Role ?? WorkRole.FullStack, Math.Max(0.5, sc.EffortDaysEach), 1.0,
                            sc.DependsOn is { } dep ? [dep] : [], 3, null, 0, true));
                    }
                    break;
                case PersonLeaves pl:
                    people = people.Select(p => p.Id == pl.PersonId
                        ? p with { LeaveDay = Math.Min(p.LeaveDay ?? int.MaxValue, Math.Max(0, pl.Day)), KnowledgeLossOnLeave = Math.Clamp(pl.KnowledgeLoss, 0, 1) }
                        : p).ToList();
                    break;
                case NewHire nh:
                    people.Add(new SimPerson(Crn.DeterministicGuid($"hire:{scenarioIndex}:{nh.Name}"), nh.Name, nh.Role,
                        Math.Clamp(nh.Capacity, 0.1, 1), Math.Max(0, nh.Day), null, Math.Max(0, nh.RampUpDays), 0.3, true));
                    break;
                case MeetingLoadChange ml:
                    people = people.Select(p => ml.PersonId is null || p.Id == ml.PersonId
                        ? p with { Capacity = Math.Clamp(p.Capacity + ml.CapacityDelta, 0.05, 1.0) } : p).ToList();
                    break;
            }
        }

        var p = new Prepared { N = tasks.Count, M = people.Count, StartDate = WorkingCalendar.FirstWorkingDay(model.StartDate) };
        var index = tasks.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var personIndex = people.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);

        p.TaskIds = tasks.Select(t => t.Id).ToArray();
        p.Keys = tasks.Select(t => t.Key).ToArray();
        p.Titles = tasks.Select(t => t.Title).ToArray();
        p.Roles = tasks.Select(t => t.Role).ToArray();
        p.Estimate = tasks.Select(t => Math.Max(0.25, t.EstimateDays)).ToArray();
        p.RemainingFraction = tasks.Select(t => Math.Clamp(t.RemainingFraction, 0, 1)).ToArray();
        p.Deps = tasks.Select(t => t.DependsOn.Where(index.ContainsKey).Select(d => index[d]).Distinct().ToArray()).ToArray();
        p.Priority = tasks.Select(t => t.Priority).ToArray();
        p.Owner = tasks.Select(t => t.OwnerId is { } o && personIndex.TryGetValue(o, out var k) ? k : -1).ToArray();
        p.BlockedUntil = tasks.Select(t => t.BlockedUntilDay).ToArray();
        p.TaskStream = tasks.Select(t => Crn.Stream(t.Id)).ToArray();
        p.Added = tasks.Select(t => t.AddedByScenario).ToArray();

        p.PersonIds = people.Select(x => x.Id).ToArray();
        p.Names = people.Select(x => x.Name).ToArray();
        p.PRoles = people.Select(x => x.Role).ToArray();
        p.Capacity = people.Select(x => Math.Clamp(x.Capacity, 0.05, 1)).ToArray();
        p.KLoss = people.Select(x => x.KnowledgeLossOnLeave).ToArray();
        p.Start = people.Select(x => x.StartDay).ToArray();
        p.Leave = people.Select(x => x.LeaveDay ?? int.MaxValue).ToArray();
        p.Ramp = people.Select(x => x.RampUpDays).ToArray();
        p.PStream = people.Select(x => Crn.Stream(x.Id)).ToArray();
        p.NewHire = people.Select(x => x.IsNewHire).ToArray();

        ComputeCriticality(p, cal);
        return p;
    }

    /// <summary>Criticality = expected remaining work on the longest chain starting at the task.</summary>
    private static void ComputeCriticality(Prepared p, Calibration cal)
    {
        var successors = Enumerable.Range(0, p.N).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < p.N; i++)
            foreach (var d in p.Deps[i]) successors[d].Add(i);

        var crit = new double[p.N];
        var state = new int[p.N]; // 0 new, 1 visiting, 2 done

        double Visit(int i)
        {
            if (state[i] == 2) return crit[i];
            if (state[i] == 1) return 0; // cycle guard
            state[i] = 1;
            var own = p.Estimate[i] * p.RemainingFraction[i] * cal.For(p.Roles[i]).MeanRatio;
            var best = 0.0;
            foreach (var s in successors[i]) best = Math.Max(best, Visit(s));
            crit[i] = own + best;
            state[i] = 2;
            return crit[i];
        }

        for (var i = 0; i < p.N; i++) Visit(i);

        p.Score = new double[p.N];
        for (var i = 0; i < p.N; i++)
            p.Score[i] = crit[i] * (1 + (3 - p.Priority[i]) * 0.25);

        p.OnCriticalPath = new bool[p.N];
        var open = Enumerable.Range(0, p.N).Where(i => p.RemainingFraction[i] > 0).ToList();
        if (open.Count == 0) return;
        var cur = open.OrderByDescending(i => crit[i]).First();
        while (cur >= 0)
        {
            p.OnCriticalPath[cur] = true;
            cur = successors[cur].Where(s => p.RemainingFraction[s] > 0).OrderByDescending(s => crit[s]).DefaultIfEmpty(-1).First();
        }
    }

    private static WorkRole Bottleneck(Prepared p, Calibration cal)
    {
        var load = new Dictionary<WorkRole, double>();
        for (var i = 0; i < p.N; i++)
        {
            var w = p.Estimate[i] * p.RemainingFraction[i] * cal.For(p.Roles[i]).MeanRatio;
            load[p.Roles[i]] = load.GetValueOrDefault(p.Roles[i]) + w;
        }
        var cap = new Dictionary<WorkRole, double>();
        for (var k = 0; k < p.M; k++)
            if (p.Leave[k] == int.MaxValue) cap[p.PRoles[k]] = cap.GetValueOrDefault(p.PRoles[k]) + p.Capacity[k];

        return load.Count == 0
            ? WorkRole.Backend
            : load.OrderByDescending(kv => kv.Value / Math.Max(0.25, cap.GetValueOrDefault(kv.Key))).First().Key;
    }

    // ---- Simulation -----------------------------------------------------------------------------

    private sealed record Trace(int FinishDay, bool Finished, int[] DoneDay, double[] Effort, List<DayFrame>? Frames, int AbsentDays = 0, List<double>? Burn = null);

    private static Trace[] RunMany(Prepared p, Calibration cal, SimulationOptions o)
    {
        var runs = new Trace[Math.Max(1, o.Iterations)];
        Parallel.For(0, runs.Length, i => runs[i] = RunOnce(p, cal, o, i, record: false));
        return runs;
    }

    private static bool RoleMatch(WorkRole person, WorkRole task) =>
        person == task ||
        (person == WorkRole.FullStack && task is WorkRole.Backend or WorkRole.Frontend) ||
        (task == WorkRole.FullStack && person is WorkRole.Backend or WorkRole.Frontend);

    private static Trace RunOnce(Prepared p, Calibration cal, SimulationOptions o, int iteration, bool record, bool burn = false)
    {
        var n = p.N;
        var m = p.M;
        var effort = new double[n];
        var remaining = new double[n];
        var done = new bool[n];
        var doneDay = new int[n];
        var assignee = new int[n];
        var current = new int[m];
        Array.Fill(doneDay, -1);
        Array.Fill(assignee, -1);
        Array.Fill(current, -1);

        var left = 0;
        for (var i = 0; i < n; i++)
        {
            var rc = cal.For(p.Roles[i]);
            var mu = Math.Log(rc.MeanRatio) - rc.LogSigma * rc.LogSigma / 2;
            var mult = Math.Exp(mu + rc.LogSigma * Crn.Normal(o.Seed, iteration, p.TaskStream[i], 0));
            effort[i] = Math.Max(0.25, p.Estimate[i] * mult);
            remaining[i] = effort[i] * p.RemainingFraction[i];
            if (remaining[i] <= 1e-9) { done[i] = true; doneDay[i] = -1; }
            else left++;
        }

        // People keep the in-progress tickets they own.
        for (var i = 0; i < n; i++)
        {
            var k = p.Owner[i];
            if (done[i] || k < 0 || p.RemainingFraction[i] >= 1 || current[k] >= 0) continue;
            current[k] = i;
            assignee[i] = k;
        }

        var frames = record ? new List<DayFrame>() : null;
        var burnDown = burn ? new List<double> { Math.Round(remaining.Sum(), 2) } : null;
        var absentDays = 0;
        var start = p.StartDate;
        var lastDay = -1;
        var idleStreak = 0;
        var rampers = new Dictionary<WorkRole, int>();
        var activeRoles = new HashSet<WorkRole>();
        var personState = record ? new int[m] : null;

        bool IsReady(int t, int day)
        {
            if (done[t] || assignee[t] >= 0 || p.BlockedUntil[t] > day) return false;
            foreach (var d in p.Deps[t])
                if (!done[d] || doneDay[d] >= day) return false;
            return true;
        }

        int Pick(int k, int day)
        {
            int best = -1, crossBest = -1;
            double bestScore = double.MinValue, crossScore = double.MinValue;
            for (var t = 0; t < n; t++)
            {
                if (!IsReady(t, day)) continue;
                var s = p.Score[t] + (p.Owner[t] == k ? 1000 : 0);
                if (RoleMatch(p.PRoles[k], p.Roles[t]))
                {
                    if (s > bestScore) { bestScore = s; best = t; }
                }
                else if (!activeRoles.Any(r => RoleMatch(r, p.Roles[t])) && s > crossScore)
                {
                    crossScore = s; crossBest = t;
                }
            }
            return best >= 0 ? best : crossBest;
        }

        for (var day = 0; day < o.MaxDays && left > 0; day++)
        {
            lastDay = day;
            rampers.Clear();
            activeRoles.Clear();
            var anyoneNowOrLater = false;

            for (var k = 0; k < m; k++)
            {
                // Leaving: lose part of the in-progress work (knowledge loss).
                if (p.Leave[k] == day && current[k] >= 0)
                {
                    var t = current[k];
                    remaining[t] = Math.Min(effort[t], remaining[t] + (effort[t] - remaining[t]) * p.KLoss[k]);
                    assignee[t] = -1;
                    current[k] = -1;
                }
                if (day < p.Leave[k]) anyoneNowOrLater = true;
                if (day >= p.Start[k] && day < p.Leave[k])
                {
                    activeRoles.Add(p.PRoles[k]);
                    if (p.Ramp[k] > 0 && day - p.Start[k] < p.Ramp[k])
                        rampers[p.PRoles[k]] = rampers.GetValueOrDefault(p.PRoles[k]) + 1;
                }
            }
            if (!anyoneNowOrLater) break; // nobody left to do the work

            var progressed = false;
            for (var k = 0; k < m; k++)
            {
                if (day < p.Start[k] || day >= p.Leave[k]) { if (personState != null) personState[k] = -3; continue; }
                if (Crn.Uniform(o.Seed, iteration, p.PStream[k], day + 1) < o.AbsenceRate)
                {
                    if (personState != null) personState[k] = -2;
                    absentDays++;
                    continue;
                }

                var t = current[k];
                if (t < 0 || done[t])
                {
                    t = Pick(k, day);
                    current[k] = t;
                    if (t >= 0) assignee[t] = k;
                }
                if (t < 0) { if (personState != null) personState[k] = -1; continue; }
                if (personState != null) personState[k] = t;

                var ramp = p.Ramp[k] > 0 ? Math.Min(1.0, 0.3 + 0.7 * (day - p.Start[k]) / p.Ramp[k]) : 1.0;
                var eff = RoleMatch(p.PRoles[k], p.Roles[t])
                    ? (p.PRoles[k] == p.Roles[t] ? 1.0 : 0.9)
                    : o.CrossRoleEfficiency;
                var drag = !p.NewHire[k] && rampers.TryGetValue(p.PRoles[k], out var r) ? Math.Min(0.3, o.MentoringDrag * r) : 0;

                remaining[t] -= p.Capacity[k] * ramp * eff * (1 - drag);
                progressed = true;
                if (remaining[t] <= 1e-9)
                {
                    remaining[t] = 0;
                    done[t] = true;
                    doneDay[t] = day;
                    assignee[t] = -1;
                    current[k] = -1;
                    left--;
                }
            }

            if (frames != null)
            {
                var prog = new double[n];
                var st = new int[n];
                for (var t = 0; t < n; t++)
                {
                    prog[t] = Math.Round(1 - remaining[t] / effort[t], 3);
                    st[t] = done[t] ? 3
                        : assignee[t] >= 0 ? 2
                        : p.BlockedUntil[t] > day ? 4
                        : p.Deps[t].All(d => done[d]) ? 1 : 0;
                }
                frames.Add(new DayFrame(day, WorkingCalendar.AddWorkingDays(start, day), prog, st, (int[])personState!.Clone()));
            }

            burnDown?.Add(Math.Round(remaining.Sum(), 2));
            idleStreak = progressed ? 0 : idleStreak + 1;
            if (idleStreak > 60 && p.Start.All(s => s <= day) && p.BlockedUntil.All(b => b <= day)) break; // deadlock
        }

        var finished = left == 0;
        var finishDay = finished ? doneDay.DefaultIfEmpty(-1).Max() + 1 : o.MaxDays;
        if (finished && finishDay <= 0) finishDay = 0;
        return new Trace(finishDay, finished, doneDay, effort, frames, absentDays, burnDown);
    }

    // ---- Summaries ------------------------------------------------------------------------------

    private static int Percentile(int[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Ceiling(q * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private static ForecastSummary Summarize(Prepared p, Trace[] runs, DateOnly start, DateOnly? deadline)
    {
        var finish = runs.Select(r => r.FinishDay).OrderBy(x => x).ToArray();
        int p50 = Percentile(finish, 0.5), p80 = Percentile(finish, 0.8), p95 = Percentile(finish, 0.95);
        var onTime = deadline is { } dl
            ? runs.Count(r => r.Finished && FinishDate(start, r.FinishDay) <= dl) / (double)runs.Length
            : 1.0;

        var tasks = new List<TaskForecast>();
        for (var i = 0; i < p.N; i++)
        {
            var days = runs.Select(r => r.DoneDay[i] < 0 ? (r.Finished ? 0 : int.MaxValue / 2) : r.DoneDay[i] + 1).OrderBy(x => x).ToArray();
            var t80 = Math.Min(Percentile(days, 0.8), int.MaxValue / 4);
            tasks.Add(new TaskForecast(p.TaskIds[i], p.Keys[i], p.Titles[i], Percentile(days, 0.5), t80,
                FinishDate(start, Math.Min(t80, 5000)), p.OnCriticalPath[i]));
        }

        return new ForecastSummary(p50, p80, p95,
            FinishDate(start, p50), FinishDate(start, p80), FinishDate(start, p95),
            Math.Round(onTime, 4), Math.Round(finish.Average(), 2), runs.Count(r => !r.Finished), tasks);
    }
}
