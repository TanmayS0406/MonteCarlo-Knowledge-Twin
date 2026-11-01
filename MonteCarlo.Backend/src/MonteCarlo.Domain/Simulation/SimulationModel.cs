using MonteCarlo.Domain.Common;

namespace MonteCarlo.Domain.Simulation;

public sealed record SimPerson(
    Guid Id,
    string Name,
    WorkRole Role,
    double Capacity,
    int StartDay = 0,
    int? LeaveDay = null,
    int RampUpDays = 0,
    double KnowledgeLossOnLeave = 0.3,
    bool IsNewHire = false);

public sealed record SimTask(
    Guid Id,
    string Key,
    string Title,
    WorkRole Role,
    double EstimateDays,
    double RemainingFraction,
    IReadOnlyList<Guid> DependsOn,
    int Priority,
    Guid? OwnerId,
    int BlockedUntilDay = 0,
    bool AddedByScenario = false);

public sealed record SimModel(DateOnly StartDate, DateOnly? Deadline, IReadOnlyList<SimPerson> People, IReadOnlyList<SimTask> Tasks);

public sealed record RoleCalibration(double MeanRatio, double LogSigma, int Samples);

/// <summary>Estimate-versus-actual behaviour learned from the knowledge graph's history.</summary>
public sealed record Calibration(RoleCalibration Default, IReadOnlyDictionary<WorkRole, RoleCalibration> ByRole)
{
    public static Calibration Uncalibrated { get; } = new(new RoleCalibration(1.2, 0.35, 0), new Dictionary<WorkRole, RoleCalibration>());

    public RoleCalibration For(WorkRole role) => ByRole.TryGetValue(role, out var c) ? c : Default;

    public static Calibration FromHistory(IEnumerable<(WorkRole Role, double Estimate, double Actual)> history, int minSamples = 3)
    {
        var rows = history.Where(h => h.Estimate > 0 && h.Actual > 0).ToList();
        if (rows.Count < minSamples) return Uncalibrated;

        static RoleCalibration Fit(IReadOnlyCollection<(WorkRole Role, double Estimate, double Actual)> r)
        {
            var ratios = r.Select(x => x.Actual / x.Estimate).ToArray();
            var logs = ratios.Select(x => Math.Log(x)).ToArray();
            var meanLog = logs.Average();
            var sd = Math.Sqrt(logs.Sum(l => (l - meanLog) * (l - meanLog)) / Math.Max(1, logs.Length - 1));
            return new RoleCalibration(Math.Clamp(ratios.Average(), 0.5, 3.0), Math.Clamp(sd, 0.1, 1.0), ratios.Length);
        }

        var byRole = rows.GroupBy(r => r.Role)
            .Where(g => g.Count() >= minSamples)
            .ToDictionary(g => g.Key, g => Fit(g.ToList()));
        return new Calibration(Fit(rows), byRole);
    }
}

public sealed class SimulationOptions
{
    public int Iterations { get; init; } = 1000;
    public int Seed { get; init; } = 42;
    /// <summary>Probability that a person is absent on a given working day.</summary>
    public double AbsenceRate { get; init; } = 0.03;
    public int MaxDays { get; init; } = 500;
    /// <summary>Capacity lost by each veteran of the same role per new hire still ramping up.</summary>
    public double MentoringDrag { get; init; } = 0.1;
    /// <summary>Efficiency when someone picks up work outside their role.</summary>
    public double CrossRoleEfficiency { get; init; } = 0.5;
}

// ----- Scenarios ---------------------------------------------------------------------------------

public abstract record ScenarioChange;

public sealed record PersonLeaves(Guid PersonId, int Day, double KnowledgeLoss = 0.3) : ScenarioChange;

public sealed record NewHire(string Name, WorkRole Role, int Day, double Capacity = 1.0, int RampUpDays = 15) : ScenarioChange;

public sealed record ScopeChange(WorkRole? Role, int AddTasks, double EffortDaysEach, double EffortMultiplier = 1.0, Guid? DependsOn = null) : ScenarioChange;

public sealed record RemoveTask(Guid TaskId) : ScenarioChange;

public sealed record PrioritySwap(Guid TaskId, int NewPriority) : ScenarioChange;

/// <summary>CapacityDelta is added to capacity, e.g. -0.1 means 10% more of the day goes to meetings.</summary>
public sealed record MeetingLoadChange(Guid? PersonId, double CapacityDelta) : ScenarioChange;

public sealed record ClientDelay(Guid TaskId, int UntilDay) : ScenarioChange;

public sealed record Scenario(string Name, IReadOnlyList<ScenarioChange> Changes)
{
    public static Scenario Baseline { get; } = new("Baseline", []);
}

// ----- Results -----------------------------------------------------------------------------------

public sealed record TaskForecast(Guid Id, string Key, string Title, int P50Day, int P80Day, DateOnly P80Date, bool OnCriticalPath);

public sealed record ForecastSummary(
    int P50Days, int P80Days, int P95Days,
    DateOnly P50, DateOnly P80, DateOnly P95,
    double ProbabilityOnTime,
    double MeanDays,
    int UnfinishedRuns,
    IReadOnlyList<TaskForecast> Tasks);

public sealed record HistogramBin(int Day, DateOnly Date, int Baseline, int Scenario);

/// <summary>Task state codes in a frame: 0 waiting, 1 ready, 2 active, 3 done, 4 blocked.</summary>
public sealed record DayFrame(int Day, DateOnly Date, double[] Progress, int[] TaskState, int[] PersonTask);

public sealed record ReplayTask(Guid Id, string Key, string Title, WorkRole Role, double EffortDays, int[] DependsOn, bool OnCriticalPath, bool AddedByScenario);

public sealed record ReplayPerson(Guid Id, string Name, WorkRole Role, int StartDay, int? LeaveDay, bool IsNewHire);

/// <summary>One typical run (closest to P50) recorded day by day for the 3D view. Person task codes: index, -1 idle, -2 absent, -3 not on team.</summary>
public sealed record ReplayRun(int Iteration, int FinishDay, IReadOnlyList<ReplayTask> Tasks, IReadOnlyList<ReplayPerson> People, IReadOnlyList<DayFrame> Frames);

public sealed record RemovedTask(Guid Id, string Key, string Title);

public sealed record SimulationComparison(
    DateOnly StartDate,
    DateOnly? Deadline,
    int? DeadlineDay,
    ForecastSummary Baseline,
    ForecastSummary Scenario,
    int DeltaP50Days,
    int DeltaP80Days,
    double MeanPairedDeltaDays,
    IReadOnlyList<HistogramBin> Histogram,
    string BottleneckRole,
    IReadOnlyList<RemovedTask> RemovedTasks,
    ReplayRun Replay);

// ----- Full Monte Carlo trace (3D lab) ----------------------------------------------------------

/// <summary>
/// One Monte Carlo iteration. SampledWorkDays is the remaining work after this iteration's random estimate errors;
/// EstimateRatio = sampled / planned. LastTask is the index of the task that finished last. Burndown holds the
/// remaining work at the start of the run and after each working day (only for the first N iterations).
/// </summary>
public sealed record IterationSample(int Iteration, int FinishDay, bool Finished, bool OnTime, double SampledWorkDays,
    double EstimateRatio, int AbsentDays, int LastTask, double[]? Burndown);

public sealed record DayCount(int Day, DateOnly Date, int Count, int OnTime);

public sealed record ConvergencePoint(int Iterations, int P50Days, int P80Days, int P95Days, double MeanDays, double OnTime);

/// <summary>DriverShare (criticality index) = share of iterations in which this task was on that run's critical chain: the chain of latest-finishing dependencies ending at the last task.</summary>
public sealed record TaskDriver(Guid Id, string Key, string Title, WorkRole Role, double DriverShare, int P50Day, int P80Day, bool OnCriticalPath);

public sealed record MonteCarloTrace(
    DateOnly StartDate,
    DateOnly? Deadline,
    int? DeadlineDay,
    int Iterations,
    int Seed,
    double PlannedWorkDays,
    double CalibrationMeanRatio,
    double CalibrationLogSigma,
    ForecastSummary Summary,
    IReadOnlyList<IterationSample> Samples,
    IReadOnlyList<DayCount> Distribution,
    IReadOnlyList<ConvergencePoint> Convergence,
    IReadOnlyList<TaskDriver> Drivers);
