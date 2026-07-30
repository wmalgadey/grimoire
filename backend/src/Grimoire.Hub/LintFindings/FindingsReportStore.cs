using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.LintFindings;

/// <summary>
/// Hub-written, one file per Lint Run (data-model.md "Findings Report",
/// contracts/findings-report-format.md). Concrete class, directly injected —
/// persistence exemption (Constitution I / ADR-010); confined to
/// <c>Grimoire.Hub.LintFindings</c>, mirroring <c>ConversationRecordStore</c>'s
/// containment. Unlike the Conversation Record, a Findings Report is written exactly
/// once per run — there is no append path.
/// </summary>
public sealed class FindingsReportStore
{
    private readonly ResolvedGrimoirePaths _paths;
    private readonly ILogger<FindingsReportStore> _logger;

    public FindingsReportStore(ResolvedGrimoirePaths paths, ILogger<FindingsReportStore>? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullLogger<FindingsReportStore>.Instance;
    }

    /// <summary>
    /// Writes the run's Findings Report exactly once, at its terminal transition.
    /// Directory auto-created (matches every other writable-data location, ADR-009).
    /// </summary>
    public async Task<string> WriteAsync(FindingsReport report, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.FindingsDir);
        var path = _paths.FindingsReportPathFor(report.RunId);
        var content = FindingsReportFormat.Build(report);
        await File.WriteAllTextAsync(path, content, FindingsReportFormat.Encoding, cancellationToken);

        LintFindingsLogEvents.LogFindingsReportCreated(_logger, report.RunId, path);
        return path;
    }

    /// <summary>Reads a previously-written report's raw content, or null if none exists for this run id.</summary>
    public async Task<string?> TryReadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var path = _paths.FindingsReportPathFor(runId);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, FindingsReportFormat.Encoding, cancellationToken)
            : null;
    }
}
