using System.ComponentModel;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.Runtime.Paths;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="IngestSubmitSourceCommand"/> (018-hub-cli-commands T010, migrated
/// from the inline <c>Program.cs</c> special case). <c>--path</c> is required and
/// non-empty; a missing/blank value is now a graceful Spectre usage error (exit 2) —
/// unlike the previous unhandled <see cref="ArgumentException"/> — per FR-009.
/// </summary>
public sealed class IngestSubmitSourceSettings : HubPathSettings
{
    [CommandOption("--path <PATH>", isRequired: true)]
    [Description("Path to the source file to submit for ingest (required).")]
    public string? Path { get; set; }

    [CommandOption("--source-kind <KIND>")]
    [Description("Kind of the submitted source (default: file).")]
    public string SourceKind { get; set; } = "file";

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Path)
            ? ValidationResult.Error("--path is required and must not be empty.")
            : ValidationResult.Success();
}

/// <summary>
/// Submits a source document for ingest — the Hub's original (and, pre-018, only) CLI
/// command. Unlike the retired inline case in <c>Program.cs</c> (which constructed its
/// own <c>LocalSecretsLoader</c>/<c>AgentProcessHost</c>/<c>IngestSubmissionService</c>), this
/// command resolves <see cref="IngestSubmissionService"/> from the same DI container
/// <see cref="HubHostComposition"/> builds for every other command and for the HTTP
/// endpoints — the "same coordinators the HTTP endpoints use" model FR-005 requires.
/// Execution (in-process run-to-exit via <c>IAgentProcessLauncher.RunToExitAsync</c>,
/// ADR-008's manual-CLI-path exemption) and the exact output line are unchanged.
/// </summary>
public sealed class IngestSubmitSourceCommand : AsyncCommand<IngestSubmitSourceSettings>
{
    private readonly IngestSubmissionService _submissionService;
    private readonly IngestContentPaths _contentPaths;
    private readonly ResolvedGrimoirePaths _resolvedPaths;

    public IngestSubmitSourceCommand(
        IngestSubmissionService submissionService, IngestContentPaths contentPaths, ResolvedGrimoirePaths resolvedPaths)
    {
        _submissionService = submissionService;
        _contentPaths = contentPaths;
        _resolvedPaths = resolvedPaths;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, IngestSubmitSourceSettings settings, CancellationToken cancellationToken)
    {
        string? pastedText = null;
        if (settings.SourceKind == "pasted_text")
        {
            pastedText = await Console.In.ReadToEndAsync();
        }

        var taskId = await _submissionService.SubmitAsync(
            new IngestSubmitSourceOptions(settings.Path!, settings.SourceKind, pastedText),
            _contentPaths,
            _resolvedPaths,
            cancellationToken);

        Console.WriteLine($"Submitted ingest task: {taskId}");
        return (int)CliExitCode.Success;
    }
}
