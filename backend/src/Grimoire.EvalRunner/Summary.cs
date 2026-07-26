using System.Globalization;
using System.Text;
using Grimoire.EvalRunner.Capture;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;

namespace Grimoire.EvalRunner;

/// <summary>
/// Markdown summary in the shape established by 007's `scripts/ci/format-eval-summary`
/// ("## Agent Eval Results" + per-test table) so `eval.yml`'s PR-comment/job-summary
/// contract carries over unchanged.
/// </summary>
public static class Summary
{
    public static string ForReplay(IReadOnlyList<ScenarioReplayResult> results)
    {
        var builder = StartTable();
        foreach (var result in results)
        {
            var detail = result.TrustStatus == TrustStatus.Trusted
                ? FormatRate(result)
                : result.Detail ?? result.TrustStatus.ToString();
            AppendRow(builder, $"replay:{result.ScenarioId}", result.IsTrustedPass, detail);
        }

        return Finish(builder, results.Count(r => r.IsTrustedPass), results.Count(r => !r.IsTrustedPass));
    }

    public static string ForCapture(IReadOnlyList<CaptureScenarioResult> results)
    {
        var builder = StartTable();
        foreach (var result in results)
        {
            var pass = result.Stored && result.ThresholdMet && result.NoOutOfScopeGuaranteeHeld;
            var detail = result.Detail
                ?? string.Create(CultureInfo.InvariantCulture,
                    $"Success rate: {result.SuccessRate:P1} (threshold {result.Threshold:P0}); model {result.Model}");
            AppendRow(builder, $"capture:{result.ScenarioId}", pass, detail);
        }

        return Finish(builder, results.Count(r => r.Stored && r.ThresholdMet), results.Count(r => !(r.Stored && r.ThresholdMet)));
    }

    public static string ForStatus(IReadOnlyList<ScenarioTrustReport> reports)
    {
        var builder = StartTable();
        foreach (var report in reports)
        {
            var detail = report.Status == TrustStatus.Trusted
                ? $"model {report.Manifest?.Model}; captured {report.Manifest?.CapturedAt:yyyy-MM-dd}"
                : report.Detail ?? string.Empty;
            AppendRow(builder, $"status:{report.ScenarioId}", report.Status == TrustStatus.Trusted, detail);
        }

        return Finish(builder, reports.Count(r => r.Status == TrustStatus.Trusted), reports.Count(r => r.Status != TrustStatus.Trusted));
    }

    private static string FormatRate(ScenarioReplayResult result)
        => string.Create(CultureInfo.InvariantCulture,
            $"Success rate: {result.SuccessRate:P1} (threshold {result.Threshold:P0}); model {result.Model}; captured {result.CapturedAt:yyyy-MM-dd}");

    private static StringBuilder StartTable()
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Agent Eval Results");
        builder.AppendLine();
        builder.AppendLine("| Test | Outcome | Detail |");
        builder.AppendLine("|------|---------|--------|");
        return builder;
    }

    private static void AppendRow(StringBuilder builder, string name, bool pass, string detail)
        => builder.AppendLine($"| {name} | {(pass ? "✅ Passed" : "❌ Failed")} | {detail.Replace("|", "\\|").ReplaceLineEndings(" ")} |");

    private static string Finish(StringBuilder builder, int passed, int failed)
    {
        builder.AppendLine();
        builder.AppendLine($"**{passed} passed, {failed} failed.**");
        return builder.ToString();
    }
}
