using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 028-lint-at-scale (US2, T016, Constitution Principle IV) — validates the
/// <c>lint_agent.run</c> root span's <c>coverage.*</c> attributes, read from the production
/// composition root (the real <c>Grimoire.LintAgent</c> <see cref="ActivitySource"/> and the
/// real <see cref="LintAgentTracing"/> static class) rather than a test-only
/// <see cref="ActivitySource"/> — mirrors <c>LintDeletionObservabilityTests</c>' pattern.
/// </summary>
public class LintCoverageObservabilityTests
{
    [Fact]
    public void RunSpan_CarriesCoverageAttributes_SetByRecordCoverageOnCurrentRun()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var coverage = WikiCoverage.Compute(pagesTotal: 633, pagesConsidered: 611);

        using (LintAgentTracing.StartRunActivity("run-coverage-obs-1"))
        {
            // Mirrors exactly what LintIntentHandler.ExecuteAsync calls once it computes
            // the run's coverage report, right before the run's terminal event.
            LintAgentTracing.RecordCoverageOnCurrentRun(coverage);
        }

        var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
        Assert.Equal("run-coverage-obs-1", GetTag(run, "run_id"));
        Assert.Equal("633", GetTag(run, "coverage.pages_total"));
        Assert.Equal("611", GetTag(run, "coverage.pages_considered"));
        Assert.Equal("partial", GetTag(run, "coverage.status"));
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
