using System.Text.Json;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;
using MonteCarlo.Domain.Simulation;

namespace MonteCarlo.Application.Services;

/// <summary>Use case: RunWhatIf. Reads people, work items, dependencies and history from the knowledge graph.</summary>
public sealed class SimulationService(IKnowledgeStore store, IInsightNarrator narrator)
{
    public async Task<SimulationModelDto> GetModelAsync(Guid? projectId, bool includeAllReleases, CancellationToken ct = default)
    {
        var (project, model, calibration) = await BuildAsync(projectId, includeAllReleases, null, ct);
        return new SimulationModelDto(project.Id, project.Name, model.StartDate, project.Deadline, project.CurrentRelease,
            model.People.Select(p => new SimPersonDto(p.Id, p.Name, p.Role, p.Capacity, p.StartDay, p.LeaveDay)).ToList(),
            model.Tasks.Select(t => new SimTaskDto(t.Id, t.Key, t.Title, t.Role, Math.Round(t.EstimateDays, 2), Math.Round(t.RemainingFraction, 2),
                t.DependsOn, t.Priority, t.OwnerId, t.BlockedUntilDay)).ToList(),
            ToDto(calibration));
    }

    public async Task<SimulationResultDto> RunAsync(RunSimulationRequest req, CancellationToken ct = default)
    {
        var iterations = Math.Clamp(req.Iterations, 50, 20000);
        var (project, model, calibration) = await BuildAsync(req.ProjectId, req.IncludeAllReleases, req.Deadline, ct);
        if (model.Tasks.Count == 0) throw new ValidationException("No open work items to simulate.");
        if (model.People.Count == 0) throw new ValidationException("Add people to the team before simulating.");

        var changesDto = req.Changes ?? [];
        var changes = changesDto.Select(ToDomain).ToList();
        var name = string.IsNullOrWhiteSpace(req.Name) ? (changes.Count == 0 ? "Baseline forecast" : "What-if") : req.Name.Trim();
        var scenario = new Scenario(name, changes);
        var options = new SimulationOptions { Iterations = iterations, Seed = req.Seed, AbsenceRate = Math.Clamp(req.AbsenceRate, 0, 0.5) };

        var comparison = MonteCarloEngine.Compare(model, scenario, calibration, options);
        var descriptions = changesDto.Select(c => Describe(c, model)).ToList();

        // Mitigation: if the scenario hurts, test hiring a contractor for the bottleneck role on day 10.
        MitigationResult? mitigation = null;
        if (req.AutoMitigate && comparison.DeltaP80Days > 0)
        {
            var role = Enum.TryParse<WorkRole>(comparison.BottleneckRole, out var r) ? r : WorkRole.Backend;
            var hire = new NewHire($"{role} contractor", role, 10, 1.0, 10);
            var mitigated = MonteCarloEngine.Forecast(model, scenario with { Changes = [.. changes, hire] }, calibration,
                new SimulationOptions { Iterations = Math.Min(iterations, 1000), Seed = req.Seed, AbsenceRate = options.AbsenceRate });
            mitigation = new MitigationResult($"Hire a {role.ToString().ToLowerInvariant()} contractor by day 10",
                comparison.Scenario.P80Days - mitigated.P80Days, comparison.DeltaP80Days, mitigated.P80, mitigated.ProbabilityOnTime);
        }

        var narrative = await narrator.ExplainAsync(new NarrativeInput(name, descriptions, comparison, mitigation), ct);

        var run = new SimulationRun
        {
            ProjectId = project.Id,
            Name = name,
            ScenarioJson = JsonSerializer.Serialize(changesDto, Json.Options),
            Iterations = iterations,
            Seed = req.Seed,
            BaselineP50 = comparison.Baseline.P50,
            BaselineP80 = comparison.Baseline.P80,
            BaselineP95 = comparison.Baseline.P95,
            ScenarioP50 = comparison.Scenario.P50,
            ScenarioP80 = comparison.Scenario.P80,
            ScenarioP95 = comparison.Scenario.P95,
            BaselineOnTime = comparison.Baseline.ProbabilityOnTime,
            ScenarioOnTime = comparison.Scenario.ProbabilityOnTime,
            Narrative = narrative
        };
        var result = new SimulationResultDto(run.Id, name, run.CreatedAt, iterations, req.Seed, changesDto, descriptions, comparison,
            mitigation is null ? null : new MitigationResultDto(mitigation.Description, mitigation.RecoveredDays, mitigation.LostDays, mitigation.P80, mitigation.ProbabilityOnTime),
            narrative, ToDto(calibration));
        run.ResultJson = JsonSerializer.Serialize(result, Json.Options);
        store.Add(run);
        store.Audit("user", "RunWhatIf", "SimulationRun", run.Id, $"{name}: P80 {comparison.Baseline.P80} -> {comparison.Scenario.P80}");
        await store.SaveChangesAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<SimulationRunSummaryDto>> ListRunsAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var runs = await store.ListAsync(store.SimulationRuns.Where(r => r.ProjectId == project.Id).OrderByDescending(r => r.CreatedAt).Take(50), ct);
        return runs.Select(ToSummary).ToList();
    }

    public async Task<SimulationResultDto> GetRunAsync(Guid id, CancellationToken ct = default)
    {
        var run = await store.FirstOrDefaultAsync(store.SimulationRuns.Where(r => r.Id == id), ct) ?? throw new NotFoundException("Run not found");
        return JsonSerializer.Deserialize<SimulationResultDto>(run.ResultJson, Json.Options) ?? throw new NotFoundException("Run data missing");
    }

    public async Task DeleteRunAsync(Guid id, CancellationToken ct = default)
    {
        var run = await store.FirstOrDefaultAsync(store.SimulationRuns.Where(r => r.Id == id), ct) ?? throw new NotFoundException("Run not found");
        store.Remove(run);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>Runs the Monte Carlo simulation and returns every iteration so the 3D lab can replay it run by run.</summary>
    public async Task<MonteCarloTraceDto> TraceAsync(MonteCarloTraceRequest req, CancellationToken ct = default)
    {
        var (_, model, calibration) = await BuildAsync(req.ProjectId, req.IncludeAllReleases, null, ct);
        if (model.Tasks.Count == 0) throw new ValidationException("No open work items to simulate.");
        if (model.People.Count == 0) throw new ValidationException("Add people to the team before simulating.");

        var changesDto = req.Changes ?? [];
        var scenario = new Scenario(changesDto.Count == 0 ? "Baseline" : "What-if", changesDto.Select(ToDomain).ToList());
        var options = new SimulationOptions { Iterations = Math.Clamp(req.Iterations, 50, 5000), Seed = req.Seed, AbsenceRate = Math.Clamp(req.AbsenceRate, 0, 0.5) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var trace = MonteCarloEngine.Explore(model, scenario, calibration, options, Math.Clamp(req.PathCount, 0, 500));
        return new MonteCarloTraceDto(scenario.Name, changesDto.Select(c => Describe(c, model)).ToList(), watch.ElapsedMilliseconds, trace);
    }

    /// <summary>
    /// Risk landscape for the 3D lab: a grid of forecasts over two levers (e.g. team capacity x scope).
    /// Every cell uses the same seed, so differences between cells come from the levers, not luck.
    /// </summary>
    public async Task<LandscapeResultDto> LandscapeAsync(LandscapeRequest req, CancellationToken ct = default)
    {
        var steps = Math.Clamp(req.Steps, 3, 10);
        var iterations = Math.Clamp(req.Iterations, 100, 2000);
        var absence = Math.Clamp(req.AbsenceRate, 0, 0.5);
        var xName = NormalizeAxis(req.XAxis);
        var zName = NormalizeAxis(req.ZAxis);
        if (xName == zName) throw new ValidationException("Pick two different axes.");

        var (_, model, calibration) = await BuildAsync(req.ProjectId, req.IncludeAllReleases, null, ct);
        if (model.Tasks.Count == 0) throw new ValidationException("No open work items to simulate.");
        if (model.People.Count == 0) throw new ValidationException("Add people to the team before simulating.");

        var hireRole = BusiestRole(model);
        var x = Axis(xName, steps, absence, hireRole);
        var z = Axis(zName, steps, absence, hireRole);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var cells = new LandscapeCellDto[steps * steps];
        Parallel.For(0, cells.Length, new ParallelOptions { CancellationToken = ct }, idx =>
        {
            int xi = idx % steps, zi = idx / steps;
            var m = model;
            var changes = new List<ScenarioChange>();
            var abs = absence;
            Apply(xName, x.Values[xi], hireRole, ref m, changes, ref abs);
            Apply(zName, z.Values[zi], hireRole, ref m, changes, ref abs);
            var f = MonteCarloEngine.Forecast(m, new Scenario("cell", changes), calibration,
                new SimulationOptions { Iterations = iterations, Seed = req.Seed, AbsenceRate = abs });
            cells[idx] = new LandscapeCellDto(xi, zi, f.P50Days, f.P80Days, f.P95Days, f.P80, Math.Round(f.ProbabilityOnTime, 3));
        });

        var start = WorkingCalendar.FirstWorkingDay(model.StartDate);
        int? deadlineDay = model.Deadline is { } dl ? WorkingCalendar.WorkingDaysBetween(start, dl) + 1 : null;
        return new LandscapeResultDto(start, model.Deadline, deadlineDay, hireRole.ToString(), iterations, x, z, cells, watch.ElapsedMilliseconds);
    }

    private static string NormalizeAxis(string? axis) => (axis ?? "").Trim().ToLowerInvariant() switch
    {
        "capacity" => "Capacity",
        "scope" => "Scope",
        "hires" => "Hires",
        "absence" => "Absence",
        _ => throw new ValidationException($"Unknown axis '{axis}'. Use Capacity, Scope, Hires or Absence.")
    };

    private static LandscapeAxisDto Axis(string name, int steps, double absence, WorkRole hireRole)
    {
        double[] values;
        int baseline;
        switch (name)
        {
            case "Capacity": // 50%..100% of today's focus time
                values = Enumerable.Range(0, steps).Select(i => Math.Round(0.5 + 0.5 * i / (steps - 1), 3)).ToArray();
                baseline = steps - 1;
                return new(name, "Team capacity", values, values.Select(v => $"{v * 100:0}%").ToArray(), baseline);
            case "Scope": // 80%, 90%, 100%, ... of today's estimated effort
                values = Enumerable.Range(0, steps).Select(i => Math.Round(0.8 + 0.1 * i, 2)).ToArray();
                baseline = 2;
                return new(name, "Scope (effort)", values, values.Select(v => $"{v * 100:0}%").ToArray(), baseline);
            case "Hires":
                values = Enumerable.Range(0, steps).Select(i => (double)i).ToArray();
                return new(name, $"Extra {hireRole.ToString().ToLowerInvariant()} hires", values, values.Select(v => $"+{v:0}").ToArray(), 0);
            default: // Absence 0%..24%
                values = Enumerable.Range(0, steps).Select(i => Math.Round(0.04 * i, 2)).ToArray();
                baseline = Array.IndexOf(values, values.OrderBy(v => Math.Abs(v - absence)).First());
                return new(name, "Daily absence", values, values.Select(v => $"{v * 100:0}%").ToArray(), baseline);
        }
    }

    private static void Apply(string axis, double value, WorkRole hireRole, ref SimModel model, List<ScenarioChange> changes, ref double absence)
    {
        switch (axis)
        {
            case "Capacity":
                model = model with { People = model.People.Select(p => p with { Capacity = Math.Max(0.05, p.Capacity * value) }).ToList() };
                break;
            case "Scope":
                if (Math.Abs(value - 1) > 1e-9) changes.Add(new ScopeChange(null, 0, 0, value));
                break;
            case "Hires":
                for (var i = 0; i < (int)value; i++) changes.Add(new NewHire($"{hireRole} hire {i + 1}", hireRole, 0, 1.0, 10));
                break;
            case "Absence":
                absence = value;
                break;
        }
    }

    /// <summary>Role with the most remaining work per unit of capacity: where an extra person helps most.</summary>
    private static WorkRole BusiestRole(SimModel model)
    {
        var supply = model.People.GroupBy(p => p.Role).ToDictionary(g => g.Key, g => g.Sum(p => p.Capacity));
        return model.Tasks.GroupBy(t => t.Role)
            .Select(g => (Role: g.Key, Load: g.Sum(t => t.EstimateDays * t.RemainingFraction) / Math.Max(0.5, supply.GetValueOrDefault(g.Key))))
            .OrderByDescending(r => r.Load).Select(r => r.Role).FirstOrDefault();
    }

    public static SimulationRunSummaryDto ToSummary(SimulationRun r) => new(r.Id, r.Name, r.CreatedAt, r.Iterations,
        r.BaselineP50, r.BaselineP80, r.BaselineP95, r.ScenarioP50, r.ScenarioP80, r.ScenarioP95, r.BaselineOnTime, r.ScenarioOnTime, r.Narrative);

    // ---- Model building -------------------------------------------------------------------------

    private async Task<(Project, SimModel, Calibration)> BuildAsync(Guid? projectId, bool includeAllReleases, DateOnly? deadlineOverride, CancellationToken ct)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = WorkingCalendar.FirstWorkingDay(today > project.StartDate ? today : project.StartDate);

        var items = await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == project.Id), ct);
        var blockers = await store.ListAsync(store.Blockers.Where(b => b.ProjectId == project.Id && b.ResolvedAt == null && b.BlockingWorkItemId != null), ct);
        var people = await store.ListAsync(store.People, ct);

        // Calibration from history: real estimate-versus-actual ratios per role.
        var calibration = Calibration.FromHistory(items
            .Where(w => w.Status == WorkItemStatus.Done && w.EstimateDays > 0 && w.ActualDays > 0)
            .Select(w => (w.Role, w.EstimateDays!.Value, w.ActualDays!.Value)));

        var medians = items.Where(w => w.Status == WorkItemStatus.Done && w.ActualDays > 0)
            .GroupBy(w => w.Role).ToDictionary(g => g.Key, g => g.Select(w => w.ActualDays!.Value).OrderBy(x => x).ElementAt(g.Count() / 2));

        var open = items.Where(w => w.Status.IsOpen() && w.Status != WorkItemStatus.Backlog &&
                                    (includeAllReleases || string.Equals(w.Release, project.CurrentRelease, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var openIds = open.Select(w => w.Id).ToHashSet();
        var blockedIds = blockers.Select(b => b.BlockingWorkItemId!.Value).ToHashSet();

        var tasks = open.Select(w =>
        {
            var estimate = w.EstimateDays ?? (medians.TryGetValue(w.Role, out var m) ? m : 3.0);
            var remaining = w.Status switch
            {
                WorkItemStatus.InReview => 0.15,
                WorkItemStatus.InProgress or WorkItemStatus.Blocked when w.StartedAt is { } s =>
                    Math.Clamp(1 - WorkingCalendar.WorkingDaysBetween(DateOnly.FromDateTime(s.UtcDateTime), today) / (estimate * calibration.For(w.Role).MeanRatio), 0.2, 0.9),
                WorkItemStatus.InProgress or WorkItemStatus.Blocked => 0.6,
                _ => 1.0
            };
            var blockedUntil = w.Status == WorkItemStatus.Blocked || blockedIds.Contains(w.Id) ? 3 : 0;
            return new SimTask(w.Id, w.ExternalRef, w.Title, w.Role, estimate, remaining,
                w.Dependencies.Select(d => d.DependsOnId).Where(openIds.Contains).ToList(), w.Priority, w.OwnerId, blockedUntil);
        }).ToList();

        var team = people
            .Where(p => p.LeaveDate is null || p.LeaveDate > start)
            .Select(p => new SimPerson(p.Id, p.Name, p.Role, p.Capacity,
                p.StartDate > start ? WorkingCalendar.WorkingDaysBetween(start, p.StartDate) : 0,
                p.LeaveDate is { } l ? WorkingCalendar.WorkingDaysBetween(start, l) : null))
            .ToList();

        return (project, new SimModel(start, deadlineOverride ?? project.Deadline, team, tasks), calibration);
    }

    private static ScenarioChange ToDomain(ScenarioChangeDto c) => c.Type.Trim().ToLowerInvariant() switch
    {
        "personleaves" => new PersonLeaves(c.PersonId ?? throw new ValidationException("PersonLeaves needs personId"), c.Day ?? 0, c.KnowledgeLoss ?? 0.3),
        "newhire" => new NewHire(string.IsNullOrWhiteSpace(c.Name) ? "New hire" : c.Name!, c.Role ?? WorkRole.Backend, c.Day ?? 0, c.Capacity ?? 1.0, c.RampUpDays ?? 15),
        "scopechange" => new ScopeChange(c.Role, c.AddTasks ?? 0, c.EffortDaysEach ?? 3, c.EffortMultiplier ?? 1.0, c.TaskId),
        "removetask" => new RemoveTask(c.TaskId ?? throw new ValidationException("RemoveTask needs taskId")),
        "priorityswap" => new PrioritySwap(c.TaskId ?? throw new ValidationException("PrioritySwap needs taskId"), c.NewPriority ?? 1),
        "meetingload" => new MeetingLoadChange(c.PersonId, c.CapacityDelta ?? -0.1),
        "clientdelay" => new ClientDelay(c.TaskId ?? throw new ValidationException("ClientDelay needs taskId"), c.UntilDay ?? 10),
        _ => throw new ValidationException($"Unknown scenario type '{c.Type}'.")
    };

    private static string Describe(ScenarioChangeDto c, SimModel model)
    {
        string Person(Guid? id) => model.People.FirstOrDefault(p => p.Id == id)?.Name ?? "someone";
        string Task(Guid? id) => model.Tasks.FirstOrDefault(t => t.Id == id) is { } t ? $"{t.Key} {t.Title}" : "a task";
        return c.Type.Trim().ToLowerInvariant() switch
        {
            "personleaves" => $"{Person(c.PersonId)} leaves on day {c.Day ?? 0} ({(c.KnowledgeLoss ?? 0.3) * 100:0}% of in-progress work lost)",
            "newhire" => $"{(string.IsNullOrWhiteSpace(c.Name) ? "A new hire" : c.Name)} ({c.Role ?? WorkRole.Backend}) joins on day {c.Day ?? 0}, {c.RampUpDays ?? 15}-day ramp-up",
            "scopechange" => (c.AddTasks ?? 0) > 0
                ? $"Scope grows by {c.AddTasks} {(c.Role?.ToString() ?? "")} tasks of ~{c.EffortDaysEach ?? 3} days"
                : $"Effort x{c.EffortMultiplier ?? 1.0} for {(c.Role?.ToString() ?? "all")} work",
            "removetask" => $"{Task(c.TaskId)} is dropped from scope",
            "priorityswap" => $"{Task(c.TaskId)} priority set to P{c.NewPriority ?? 1}",
            "meetingload" => $"Meeting load {(c.CapacityDelta ?? -0.1) * -100:+0;-0}% of the day for {(c.PersonId is null ? "everyone" : Person(c.PersonId))}",
            "clientdelay" => $"Client delay blocks {Task(c.TaskId)} until day {c.UntilDay ?? 10}",
            _ => c.Type
        };
    }

    private static CalibrationDto ToDto(Calibration c) =>
        new(c.Default.MeanRatio, c.Default.LogSigma, c.Default.Samples, c.ByRole.ToDictionary(k => k.Key.ToString(), v => v.Value));
}
