using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

// --- Failure classification ---

[JsonConverter(typeof(JsonStringEnumConverter<FailureKind>))]
public enum FailureKind
{
    [JsonStringEnumMemberName("no_scenarios")]
    NoScenarios,

    [JsonStringEnumMemberName("completion_regression")]
    CompletionRegression,

    [JsonStringEnumMemberName("threshold")]
    Threshold,

    [JsonStringEnumMemberName("spec_conformance_failure")]
    SpecConformanceFailure,

    [JsonStringEnumMemberName("skill_not_activated")]
    SkillNotActivated,

    [JsonStringEnumMemberName("noise_degradation")]
    NoiseDegradation,
}

// --- Assertion types ---

public enum AssertionType
{
    FileExists,
    FileNotExists,
    FileContains,
    FileNotContains,
    OutputContains,
    OutputNotContains,
    OutputMatches,
    OutputNotMatches,
    ExitSuccess,
    RunCommandAndAssert,
    ExpectTools,
    RejectTools,
    MaxTurns,
    MaxTokens,
}

public sealed record CommandAssertionArgs(
    string CommandToRun,
    string? CommandArguments = null,
    int? ExpectedExitCode = null,
    string? ExpectedStdOutContains = null,
    string? ExpectedStdErrorContains = null,
    string? ExpectedStdOutMatches = null,
    string? ExpectedStdErrorMatches = null,
    int? Timeout = null);

public sealed record Assertion(
    AssertionType Type,
    string? Path = null,
    string? Value = null,
    string? Pattern = null,
    CommandAssertionArgs? CommandArgs = null);

public sealed record AssertionResult(
    Assertion Assertion,
    bool Passed,
    string Message);

// --- Setup ---

public sealed record SetupFile(
    string Path,
    string? Source = null,
    string? Content = null);

public sealed record SetupConfig(
    bool CopyTestFiles = false,
    IReadOnlyList<SetupFile>? Files = null,
    IReadOnlyList<string>? Commands = null,
    IReadOnlyList<string>? AdditionalRequiredSkills = null,
    IReadOnlyList<string>? AdditionalRequiredAgents = null);

// --- Scenario ---

public sealed record EvalScenario(
    string Name,
    string Prompt,
    SetupConfig? Setup = null,
    IReadOnlyList<Assertion>? Assertions = null,
    IReadOnlyList<string>? Rubric = null,
    int Timeout = EvalSchema.DefaultScenarioTimeoutSeconds,
    IReadOnlyList<string>? ExpectTools = null,
    IReadOnlyList<string>? RejectTools = null,
    int? MaxTurns = null,
    int? MaxTokens = null,
    bool ExpectActivation = true,
    string? ExpectedSkill = null,
    IReadOnlyList<string>? ExpectedSkills = null);

public sealed record EvalConfig(
    IReadOnlyList<EvalScenario> Scenarios,
    int? MaxParallelScenarios = null,
    int? MaxParallelRuns = null);

/// <summary>
/// Extends SkillInfo with evaluation-specific data (eval.yaml config, MCP servers).
/// Used only by the eval command and its supporting services.
/// </summary>
public sealed record EvalSkillInfo(
    SkillInfo Skill,
    string? EvalPath,
    EvalConfig? EvalConfig,
    IReadOnlyDictionary<string, MCPServerDef>? McpServers = null);

/// <summary>
/// Unified eval target — either a skill or an agent.
/// Most of the evaluation pipeline operates on this generically.
/// </summary>
public enum EvalTargetKind { Skill, Agent }

public sealed record EvalTargetInfo(
    string Name,
    string Path,
    EvalTargetKind Kind,
    SkillInfo? Skill,
    AgentInfo? Agent,
    string? EvalPath,
    EvalConfig? EvalConfig,
    string? PluginRoot,
    IReadOnlyDictionary<string, MCPServerDef>? McpServers);

// --- Agent events ---

public sealed record AgentEvent(
    string Type,
    long Timestamp,
    Dictionary<string, JsonNode?> Data);

// --- Judge results ---

public sealed record RubricScore(
    string Criterion,
    double Score,
    string Reasoning);

public sealed record JudgeResult(
    IReadOnlyList<RubricScore> RubricScores,
    double OverallScore,
    string OverallReasoning);

/// <summary>Lightweight token counter returned by judge helpers.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0, 0);

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens,
            a.OutputTokens + b.OutputTokens,
            a.CacheReadTokens + b.CacheReadTokens,
            a.CacheWriteTokens + b.CacheWriteTokens);
}

// --- Run metrics ---

public sealed class RunMetrics
{
    public int TokenEstimate { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public string? ReasoningEffort { get; set; }
    public double Cost { get; set; }
    public int JudgeInputTokens { get; set; }
    public int JudgeOutputTokens { get; set; }
    public int JudgeCacheReadTokens { get; set; }
    public int JudgeCacheWriteTokens { get; set; }
    public int ToolCallCount { get; set; }
    public Dictionary<string, int> ToolCallBreakdown { get; set; } = new();
    public int TurnCount { get; set; }
    public long WallTimeMs { get; set; }
    public int ErrorCount { get; set; }
    public bool TimedOut { get; set; }
    public List<AssertionResult> AssertionResults { get; set; } = [];
    public bool TaskCompleted { get; set; }
    public string AgentOutput { get; set; } = "";
    public List<AgentEvent> Events { get; set; } = [];
    public string WorkDir { get; set; } = "";

    /// <summary>
    /// Per-run outcomes for this arm, preserved before <c>AverageResults</c> collapses the k runs
    /// into this single representative metrics object. Enables the downstream graded-yield ladder
    /// (Fails → Satisfies → Delivers) and the cost bootstrap, which need every run's pass/cost/turn
    /// data rather than the averaged summary. Policy-free: raw counts and cost only; the analyzer
    /// owns the ladder definition. Null on individual (un-averaged) run metrics.
    /// </summary>
    public List<RunOutcome>? PerRun { get; set; }

    /// <summary>
    /// Creates a per-run copy.  Scalar fields are copied by value and the mutable
    /// collections are re-wrapped in fresh instances so mutating the clone (e.g.
    /// accumulating judge tokens) never affects the source.  This is essential when a
    /// cached baseline is reused concurrently across parallel target evaluations: each
    /// evaluation works on its own copy instead of sharing one mutable instance.
    /// </summary>
    public RunMetrics Clone() => new()
    {
        TokenEstimate = TokenEstimate,
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
        CacheReadTokens = CacheReadTokens,
        CacheWriteTokens = CacheWriteTokens,
        JudgeInputTokens = JudgeInputTokens,
        JudgeOutputTokens = JudgeOutputTokens,
        JudgeCacheReadTokens = JudgeCacheReadTokens,
        JudgeCacheWriteTokens = JudgeCacheWriteTokens,
        ToolCallCount = ToolCallCount,
        ToolCallBreakdown = new Dictionary<string, int>(ToolCallBreakdown),
        TurnCount = TurnCount,
        WallTimeMs = WallTimeMs,
        ErrorCount = ErrorCount,
        TimedOut = TimedOut,
        AssertionResults = [.. AssertionResults],
        TaskCompleted = TaskCompleted,
        AgentOutput = AgentOutput,
        Events = [.. Events],
        WorkDir = WorkDir,
    };
}

public sealed record RunResult(
    RunMetrics Metrics,
    JudgeResult JudgeResult);

/// <summary>
/// A single run's raw outcome, captured per arm before averaging so the downstream analyzer can
/// score the graded-yield ladder (Fails → Satisfies → Delivers) and run the cost bootstrap. Kept
/// policy-free: the analyzer defines Satisfies (all functional assertions pass) and Delivers from
/// these primitives. <see cref="AssertionsPassed"/>/<see cref="AssertionsTotal"/> count FUNCTIONAL
/// assertions only (reject-tools excluded), matching the analyzer's Satisfies definition. IET is
/// recomputed from the token fields exactly as for the averaged arm.
/// </summary>
public sealed record RunOutcome(
    int AssertionsPassed,
    int AssertionsTotal,
    bool TaskCompleted,
    double Cost,
    int InputTokens,
    int CacheReadTokens,
    int OutputTokens,
    int ToolCallCount,
    int TurnCount,
    long WallTimeMs);

// --- Pairwise judging ---

public enum PairwiseMagnitude
{
    MuchBetter,
    SlightlyBetter,
    Equal,
    SlightlyWorse,
    MuchWorse,
}

public sealed record PairwiseRubricResult(
    string Criterion,
    string Winner, // "baseline" | "skill" | "tie"
    PairwiseMagnitude Magnitude,
    string Reasoning);

public sealed record PairwiseJudgeResult(
    IReadOnlyList<PairwiseRubricResult> RubricResults,
    string OverallWinner, // "baseline" | "skill" | "tie"
    PairwiseMagnitude OverallMagnitude,
    string OverallReasoning,
    bool PositionSwapConsistent);

public static class PairwiseMagnitudeScores
{
    public static double GetScore(PairwiseMagnitude magnitude) => magnitude switch
    {
        PairwiseMagnitude.MuchBetter => 1.0,
        PairwiseMagnitude.SlightlyBetter => 0.4,
        PairwiseMagnitude.Equal => 0.0,
        PairwiseMagnitude.SlightlyWorse => -0.4,
        PairwiseMagnitude.MuchWorse => -1.0,
        _ => 0.0,
    };
}

public enum JudgeMode
{
    Pairwise,
    Independent,
    Both,
}

// --- Skill activation ---

public sealed record SkillActivationInfo(
    bool Activated,
    IReadOnlyList<string> DetectedSkills,
    IReadOnlyList<string> ExtraTools,
    int SkillEventCount);

// --- Subagent (custom agent) activation ---

public sealed record SubagentActivationInfo(
    IReadOnlyList<string> InvokedAgents,
    int SubagentEventCount);

// --- Comparison ---

public sealed record MetricBreakdown(
    double TokenReduction,
    double ToolCallReduction,
    double TaskCompletionImprovement,
    double TimeReduction,
    double QualityImprovement,
    double OverallJudgmentImprovement,
    double ErrorReduction);

public sealed record ConfidenceInterval(
    double Low,
    double High,
    double Level);

public sealed class ScenarioComparison
{
    public required string ScenarioName { get; init; }
    public required RunResult Baseline { get; init; }
    public RunResult SkilledIsolated { get; init; } = null!;
    public RunResult? SkilledPlugin { get; init; }
    /// <summary>The lens that produced this comparison. In <see cref="Evaluate.EvalMode.Holistic"/> the
    /// isolated arm is a placeholder and all scores read the plugin arm.</summary>
    public EvalMode EvalMode { get; init; } = EvalMode.PerSkill;
    public required double ImprovementScore { get; init; }
    public double IsolatedImprovementScore { get; init; }
    public double PluginImprovementScore { get; init; }
    public required MetricBreakdown Breakdown { get; init; }
    public MetricBreakdown? IsolatedBreakdown { get; init; }
    public MetricBreakdown? PluginBreakdown { get; init; }
    public PairwiseJudgeResult? PairwiseResult { get; init; }
    public IReadOnlyList<double>? PerRunScores { get; set; }
    /// <summary>True when the coefficient of variation across runs exceeds the high-variance threshold.</summary>
    public bool HighVariance { get; set; }
    /// <summary>Coefficient of variation across per-run scores. Null when fewer than 2 runs.</summary>
    public double? VarianceCV { get; set; }
    public SkillActivationInfo? SkillActivationIsolated { get; set; }
    public SkillActivationInfo? SkillActivationPlugin { get; set; }
    /// <summary>Run-ordered isolated-arm activation records, used for pull-consistency analysis.</summary>
    public IReadOnlyList<SkillActivationInfo>? SkillActivationIsolatedPerRun { get; set; }
    /// <summary>Run-ordered plugin-arm activation records, used for pull-consistency analysis.</summary>
    public IReadOnlyList<SkillActivationInfo>? SkillActivationPluginPerRun { get; set; }
    public SubagentActivationInfo? SubagentActivationIsolated { get; set; }
    public SubagentActivationInfo? SubagentActivationPlugin { get; set; }
    public bool TimedOut { get; set; }
    /// <summary>The scenario timeout in seconds, if known (e.g., from eval.yaml or persisted session data).</summary>
    public int? TimeoutSeconds { get; set; }
    /// <summary>When false, non-activation is expected (negative test) and should not flag the verdict.</summary>
    public bool ExpectActivation { get; set; } = true;
    /// <summary>Methodology prior: the ONE target skill the scenario is designed to exercise (from eval.yaml expected_skill). Null when unset. Compared against the skills actually pulled to report over/under-fire and a target-skill hit rate.</summary>
    [JsonPropertyName("expectedSkill")]
    public string? ExpectedSkill { get; set; }
    /// <summary>
    /// Methodology prior: the complete stable skill set this scenario is designed to exercise.
    /// A legacy <c>expected_skill</c> value is normalized to a singleton list.
    /// </summary>
    [JsonPropertyName("expectedSkills")]
    public IReadOnlyList<string>? ExpectedSkills { get; set; }
    /// <summary>Number of individual runs that failed with exceptions and were excluded from aggregation.</summary>
    public int FailedRunCount { get; set; }
    /// <summary>Non-null when the entire scenario failed with an execution error (not a timeout).</summary>
    public string? ExecutionError { get; set; }

    // Backward-compatible aliases for JSON deserialization of older results files.
    [JsonPropertyName("withSkill")]
    public RunResult WithSkill { get => SkilledIsolated; init => SkilledIsolated = value; }
    [JsonPropertyName("skillActivation")]
    public SkillActivationInfo? SkillActivation { get => SkillActivationIsolated; init => SkillActivationIsolated = value; }
}

// --- Verdict ---

public sealed class SkillVerdict
{
    public required string SkillName { get; init; }
    public required string SkillPath { get; init; }
    public required bool Passed { get; set; }
    public required IReadOnlyList<ScenarioComparison> Scenarios { get; init; }
    public required double OverallImprovementScore { get; init; }
    public double? NormalizedGain { get; init; }
    public ConfidenceInterval? ConfidenceInterval { get; init; }
    public bool? IsSignificant { get; init; }
    public double? IsolatedScore { get; set; }
    public double? PluginScore { get; set; }
    /// <summary>The lens that produced this verdict (per-skill min-verdict, or holistic plugin-lens).</summary>
    public EvalMode EvalMode { get; init; } = EvalMode.PerSkill;
    public required string Reason { get; set; }
    /// <summary>Categorizes why the verdict failed, if it did.</summary>
    public FailureKind? FailureKind { get; set; }
    public bool SkillNotActivated { get; set; }
    public OverfittingResult? OverfittingResult { get; set; }
    public NoiseTestResult? NoiseTestResult { get; set; }
}

// --- Overfitting assessment ---

[JsonConverter(typeof(JsonStringEnumConverter<OverfittingSeverity>))]
public enum OverfittingSeverity
{
    Low,
    Moderate,
    High,
}

public sealed record RubricOverfitAssessment(
    string Scenario,
    string Criterion,
    string Classification,      // "outcome" | "technique" | "vocabulary"
    double Confidence,
    string Reasoning);

public sealed record AssertionOverfitAssessment(
    string Scenario,
    string AssertionSummary,
    string Classification,      // "broad" | "narrow"
    double Confidence,
    string Reasoning);

public sealed record PromptOverfitAssessment(
    string Scenario,
    string Issue,               // e.g. "explicit_skill_reference" | "skill_instruction"
    double Confidence,
    string Reasoning);

public sealed record OverfittingResult(
    double Score,               // [0, 1]
    OverfittingSeverity Severity,
    IReadOnlyList<RubricOverfitAssessment> RubricAssessments,
    IReadOnlyList<AssertionOverfitAssessment> AssertionAssessments,
    IReadOnlyList<PromptOverfitAssessment> PromptAssessments,
    IReadOnlyList<string> CrossScenarioIssues,
    string OverallReasoning);

public sealed record OverfittingJudgeOptions(
    string Model,
    bool Verbose,
    int Timeout,
    string WorkDir);

// --- Multi-skill noise test ---

public sealed record NoiseScenarioResult(
    string ScenarioName,
    RunResult WithSkillOnly,
    RunResult WithAllSkills,
    double DegradationScore,
    MetricBreakdown Breakdown,
    SkillActivationInfo? SkillActivation,
    int TotalSkillsLoaded);

public sealed record NoiseTestResult(
    IReadOnlyList<NoiseScenarioResult> Scenarios,
    double OverallDegradation,
    bool Passed,
    string Reason,
    int TotalSkillsLoaded);

// --- Eval config ---

public sealed record ReporterSpec(ReporterType Type);

public enum ReporterType
{
    Console,
    Json,
    Junit,
    Markdown,
}

/// <summary>
/// The evaluation lens. <see cref="PerSkill"/> is the per-skill-PR paradigm: baseline vs
/// skilled-isolated vs skilled-plugin, graded on the conservative min(isolated, plugin) verdict —
/// a claim about ONE skill's standalone value. <see cref="Holistic"/> is the whole-shelf benchmark:
/// baseline vs skilled-plugin only, the agent self-selects from the shelf, and the verdict reads the
/// plugin arm — a claim about the shelf answering a real distribution of questions. Holistic skips
/// the isolated arm (≈⅓ of run cost) and lets intentional multi-skill tasks pass, since the isolated
/// arm can never satisfy a task that needs two skills.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EvalMode>))]
public enum EvalMode { PerSkill, Holistic }

public sealed record ValidatorConfig
{
    public double MinImprovement { get; init; } = 0.1;
    public bool RequireCompletion { get; init; } = true;
    public bool Verbose { get; init; }
    public string Model { get; init; } = "claude-opus-4.6";
    public string JudgeModel { get; init; } = "claude-opus-4.6";
    public JudgeMode JudgeMode { get; init; } = JudgeMode.Pairwise;
    public int Runs { get; init; } = 5;
    public int ParallelSkills { get; init; } = 1;
    public int ParallelScenarios { get; init; } = 1;
    public int ParallelRuns { get; init; } = 1;
    public int JudgeTimeout { get; init; } = 300_000;
    public double ConfidenceLevel { get; init; } = 0.95;
    public IReadOnlyList<ReporterSpec> Reporters { get; init; } = [];
    public IReadOnlyList<string> SkillPaths { get; init; } = [];
    public bool VerdictWarnOnly { get; init; }
    public string? ResultsDir { get; init; }
    public string? TestsDir { get; init; }
    public bool OverfittingCheck { get; init; } = true;
    public bool OverfittingFix { get; init; }
    public bool KeepSessions { get; init; }
    public string? NoiseSkillsDir { get; init; }
    public double NoiseDegradationLimit { get; init; } = 0.2;
    public double NoiseMaxScenarioDegradation { get; init; } = 0.4;

    /// <summary>When set, persist each scenario's averaged baseline to this file after the run.</summary>
    public string? BaselineOut { get; init; }

    /// <summary>When set, reuse the precomputed baseline from this file instead of re-running the baseline arm.</summary>
    public string? BaselineFrom { get; init; }

    /// <summary>
    /// When set, run the requested agent arms and persist sessions/metrics but skip all judging.
    /// Judging is deferred to a later <c>rejudge</c>/<c>judge</c> step. Implies session persistence
    /// and does not require a baseline.
    /// </summary>
    public bool NoJudge { get; init; }

    /// <summary>The evaluation lens (per-skill or holistic). See <see cref="Evaluate.EvalMode"/>.</summary>
    public EvalMode EvalMode { get; init; } = EvalMode.PerSkill;

    /// <summary>
    /// Leave-one-out ablation: skill names to omit from the plugin (skilled-plugin) arm's shelf.
    /// Empty means the full shelf. Used to measure each skill's marginal contribution
    /// (full − (shelf−X)) on multi-skill-pull scenarios.
    /// </summary>
    public IReadOnlyList<string> ExcludeSkills { get; init; } = [];
}

public static class DefaultWeights
{
    public static readonly IReadOnlyDictionary<string, double> Values = new Dictionary<string, double>
    {
        ["TokenReduction"] = 0.05,
        ["ToolCallReduction"] = 0.025,
        ["TaskCompletionImprovement"] = 0.15,
        ["TimeReduction"] = 0.025,
        ["QualityImprovement"] = 0.40,
        ["OverallJudgmentImprovement"] = 0.30,
        ["ErrorReduction"] = 0.05,
    };
}

// --- JSON transport types ---

internal sealed class ConsolidateData
{
    public string? Model { get; set; }
    public string? JudgeModel { get; set; }
    public List<SkillVerdict>? Verdicts { get; set; }
}

internal sealed class ResultsOutput
{
    public required string Model { get; init; }
    public required string JudgeModel { get; init; }
    public required string Timestamp { get; init; }
    public required IReadOnlyList<SkillVerdict> Verdicts { get; init; }
}
