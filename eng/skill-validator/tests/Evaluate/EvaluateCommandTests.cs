using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class EvaluateCommandTests
{
    [Fact]
    public void ToRunOutcomeSeparatesSatisfiesAndDeliversAssertions()
    {
        var metrics = new RunMetrics
        {
            AssertionResults =
            [
                new(new Assertion(
                    AssertionType.OutputContains,
                    Value: "report",
                    Tier: AssertionTier.Satisfies,
                    MiniPrompt: "Print the report"), true, ""),
                new(new Assertion(
                    AssertionType.FileContains,
                    Path: "*.cs",
                    Value: "Serializer.Serialize",
                    Tier: AssertionTier.Delivers,
                    MiniPrompt: "Use the serializer"), false, ""),
                new(new Assertion(AssertionType.RejectTools, Value: "web_search"), false, ""),
            ],
        };
        var result = new RunResult(metrics, new JudgeResult([], 0, ""));

        var outcome = EvaluateCommand.ToRunOutcome(result);

        Assert.Equal(1, outcome.AssertionsPassed);
        Assert.Equal(2, outcome.AssertionsTotal);
        Assert.Equal(1, outcome.SatisfiesAssertionsPassed);
        Assert.Equal(1, outcome.SatisfiesAssertionsTotal);
        Assert.Equal(0, outcome.DeliversAssertionsPassed);
        Assert.Equal(1, outcome.DeliversAssertionsTotal);
    }

    // These options are judging-dependent. Under --no-judge they cannot run, so Run must reject
    // them up front (before any model/network call) rather than silently ignoring them. Each case
    // short-circuits at the early validation, so no agent client is ever created.

    [Fact]
    public async Task Run_RejectsNoJudgeWithNoiseSkillsDir()
    {
        var config = new ValidatorConfig { NoJudge = true, NoiseSkillsDir = "some/dir" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithOverfittingFix()
    {
        var config = new ValidatorConfig { NoJudge = true, OverfittingFix = true };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithBaselineFrom()
    {
        var config = new ValidatorConfig { NoJudge = true, BaselineFrom = "baseline.json" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }
}
