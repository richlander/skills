using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

/// <summary>
/// The holistic judge. Where <see cref="Comparator"/> grades a single skill on the conservative
/// min(isolated, plugin) verdict (the per-skill-PR paradigm), this computes the whole-shelf verdict
/// from the <b>plugin arm alone</b> — the lens the CT-24 holistic benchmark ships. It reads the same
/// per-scenario comparisons but ignores the (placeholder) isolated arm, so intentional multi-skill
/// tasks are scored on what the shelf actually did rather than false-failed for needing more than one
/// skill. This is also the seam where per-skill attribution (leave-one-out ablation over the skill
/// lattice) will plug in.
/// </summary>
public static class HolisticComparator
{
    public static SkillVerdict ComputeVerdict(
        SkillInfo skill,
        IReadOnlyList<ScenarioComparison> comparisons,
        double minImprovement,
        bool requireCompletion,
        double confidenceLevel = 0.95)
    {
        if (comparisons.Count == 0)
        {
            return new SkillVerdict
            {
                SkillName = skill.Name,
                SkillPath = skill.Path,
                Passed = false,
                Scenarios = [],
                OverallImprovementScore = 0,
                Reason = "No scenarios to evaluate",
                FailureKind = FailureKind.NoScenarios,
                EvalMode = EvalMode.Holistic,
            };
        }

        // In holistic mode each comparison's ImprovementScore and PerRunScores already read the
        // plugin arm (set by the aggregator), so no min() is applied here.
        var allPerRunScores = comparisons
            .SelectMany(c => c.PerRunScores ?? [c.ImprovementScore])
            .ToList();

        double overallImprovementScore = comparisons.Average(c => c.ImprovementScore);
        double normalizedGain = ComputeNormalizedGain(comparisons);

        var ci = Statistics.BootstrapConfidenceInterval(allPerRunScores, confidenceLevel);
        bool significant = Statistics.IsStatisticallySignificant(ci);

        if (requireCompletion)
        {
            // Only the plugin arm exists in holistic mode; a regression is baseline-correct →
            // plugin-wrong. The isolated arm is a placeholder and is never consulted.
            bool regressed = comparisons.Any(c =>
                c.Baseline.Metrics.TaskCompleted &&
                c.SkilledPlugin is { } p && !p.Metrics.TaskCompleted);
            if (regressed)
            {
                return new SkillVerdict
                {
                    SkillName = skill.Name,
                    SkillPath = skill.Path,
                    Passed = false,
                    Scenarios = comparisons,
                    OverallImprovementScore = overallImprovementScore,
                    NormalizedGain = normalizedGain,
                    ConfidenceInterval = ci,
                    IsSignificant = significant,
                    Reason = "Shelf regressed on task completion (plugin arm) in one or more scenarios",
                    FailureKind = FailureKind.CompletionRegression,
                    EvalMode = EvalMode.Holistic,
                };
            }
        }

        bool passed = overallImprovementScore >= minImprovement;

        string reason = passed
            ? $"Improvement score {overallImprovementScore * 100:F1}% meets threshold of {minImprovement * 100:F1}%"
            : $"Improvement score {overallImprovementScore * 100:F1}% below threshold of {minImprovement * 100:F1}%";

        if (!significant && allPerRunScores.Count > 1)
            reason += " (not statistically significant)";

        var highVarianceScenarios = comparisons.Where(c => c.HighVariance).ToList();
        if (highVarianceScenarios.Count > 0)
        {
            var names = string.Join(", ", highVarianceScenarios.Select(c => c.ScenarioName));
            reason += $" [high variance in: {names}]";
        }

        return new SkillVerdict
        {
            SkillName = skill.Name,
            SkillPath = skill.Path,
            Passed = passed,
            Scenarios = comparisons,
            OverallImprovementScore = overallImprovementScore,
            NormalizedGain = normalizedGain,
            ConfidenceInterval = ci,
            IsSignificant = significant,
            IsolatedScore = null,
            PluginScore = comparisons.Average(c => c.PluginImprovementScore),
            Reason = reason,
            FailureKind = passed ? null : FailureKind.Threshold,
            EvalMode = EvalMode.Holistic,
        };
    }

    /// <summary>
    /// Normalized gain g = (post - pre) / (1 - pre) per Hake (1998), reading the plugin arm's
    /// overall judge score as the post-treatment value.
    /// </summary>
    private static double ComputeNormalizedGain(IReadOnlyList<ScenarioComparison> comparisons)
    {
        if (comparisons.Count == 0) return 0;

        double totalGain = 0;
        int count = 0;

        foreach (var c in comparisons)
        {
            double pre = (c.Baseline.JudgeResult.OverallScore - 1) / 4.0;
            double effectiveScore = c.SkilledPlugin?.JudgeResult.OverallScore ?? c.Baseline.JudgeResult.OverallScore;
            double post = (effectiveScore - 1) / 4.0;

            if (pre >= 1)
                totalGain += post >= pre ? 0 : post - pre;
            else
                totalGain += (post - pre) / (1 - pre);

            count++;
        }

        return count > 0 ? totalGain / count : 0;
    }
}
