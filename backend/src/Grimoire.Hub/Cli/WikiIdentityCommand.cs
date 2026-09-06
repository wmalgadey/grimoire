using Grimoire.Hub.Runtime.Paths;
using Grimoire.Hub.WikiIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// The wiki-identity wizard's <c>set</c> action (029-shared-foundation-prompt T038,
/// contracts/wiki-identity-cli.md): "default" writes nothing, "specialised" emits a
/// drafting brief built from the operator's own words
/// (<see cref="WikiIdentityDraftingBrief"/>), and a hand-back persists a drafted document
/// verbatim through <see cref="WikiIdentityDocumentStore"/> — this command never composes,
/// templates, or judges content (FR-013a, ADR-053's authorship rule). No prompting path
/// exists anywhere in here: every answer is required by <see cref="WikiIdentitySettings"/>'s
/// own validation, so a malformed invocation never reaches this class (FR-015/FR-016).
/// </summary>
public sealed class WikiIdentityCommand : AsyncCommand<WikiIdentitySettings>
{
    private readonly ResolvedGrimoirePaths _paths;
    private readonly ILogger<WikiIdentityCommand> _logger;
    private readonly TextWriter _stdout;

    // Disambiguates ActivatorUtilities.CreateInstance between this constructor and the
    // test seam below, mirroring RemediationAuthorizeCommand/LintRunCommand.
    [ActivatorUtilitiesConstructor]
    public WikiIdentityCommand(ResolvedGrimoirePaths paths, ILogger<WikiIdentityCommand> logger)
        : this(paths, logger, Console.Out)
    {
    }

    /// <summary>Test seam: inject a stdout writer instead of the real console stream.</summary>
    public WikiIdentityCommand(ResolvedGrimoirePaths paths, ILogger<WikiIdentityCommand> logger, TextWriter stdout)
    {
        _paths = paths;
        _logger = logger;
        _stdout = stdout;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, WikiIdentitySettings settings, CancellationToken cancellationToken)
    {
        using var wizardSpan = HubTracing.ActivitySource.StartActivity("hub.wiki_identity.wizard");
        var answer = settings.Default ? "default" : settings.Specialised ? "specialised" : "hand-back";
        wizardSpan?.SetTag("answer", answer);

        var (outcome, exitCode) = settings.Default
            ? KeepDefault()
            : settings.Specialised
                ? await EmitBriefAsync(settings.Description!, cancellationToken)
                : await PersistFromFileAsync(settings.FromFile!, settings.Replace, cancellationToken);

        wizardSpan?.SetTag("outcome", outcome);
        HubMetrics.RecordWikiIdentityWizardOutcome(outcome);
        return (int)exitCode;
    }

    private (string Outcome, CliExitCode ExitCode) KeepDefault()
    {
        _stdout.WriteLine("Instance stays on the shipped default foundation document. Nothing was written.");
        WikiIdentityLogEvents.LogDefaultKept(_logger, "default_kept");
        return ("default_kept", CliExitCode.Success);
    }

    private async Task<(string Outcome, CliExitCode ExitCode)> EmitBriefAsync(string description, CancellationToken cancellationToken)
    {
        var resolvedDescription = description == "-"
            ? (await Console.In.ReadToEndAsync(cancellationToken)).Trim()
            : description;

        var brief = WikiIdentityDraftingBrief.Build(resolvedDescription);
        _stdout.WriteLine(brief);
        WikiIdentityLogEvents.LogBriefEmitted(_logger, resolvedDescription.Length, brief.Length);
        return ("brief_emitted", CliExitCode.Success);
    }

    private async Task<(string Outcome, CliExitCode ExitCode)> PersistFromFileAsync(
        string fromFile, bool replace, CancellationToken cancellationToken)
    {
        if (!File.Exists(fromFile))
        {
            _stdout.WriteLine($"'{fromFile}' does not exist.");
            return ("rejected", CliExitCode.NotFound);
        }

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(fromFile, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _stdout.WriteLine($"'{fromFile}' could not be read: {ex.Message}");
            return ("rejected", CliExitCode.OperationFailed);
        }

        using var persistSpan = HubTracing.ActivitySource.StartActivity("hub.wiki_identity.persist");
        var result = await new WikiIdentityDocumentStore(_paths.InstanceFoundationPromptPath)
            .PersistAsync(content, replace, cancellationToken);

        persistSpan?.SetTag("sha256", result.Sha256);
        persistSpan?.SetTag("replaced_existing", result.ReplacedExisting);
        persistSpan?.SetTag("resolved_path", _paths.InstanceFoundationPromptPath);

        switch (result.Outcome)
        {
            case WikiIdentityPersistOutcome.Rejected:
                _stdout.WriteLine($"The drafted document was rejected: {result.RejectionReason}. Nothing was placed.");
                return ("rejected", CliExitCode.OperationFailed);

            case WikiIdentityPersistOutcome.ReplaceRefused:
                _stdout.WriteLine(
                    $"An instance document already exists (sha256: {result.Sha256}). Re-run with --replace to overwrite it.");
                WikiIdentityLogEvents.LogReplaceRefused(_logger, result.Sha256!, result.RejectionReason!);
                return ("replace_refused", CliExitCode.StateConflict);

            case WikiIdentityPersistOutcome.Persisted:
                var replacedSuffix = result.ReplacedExisting ? ", replaced existing" : string.Empty;
                _stdout.WriteLine(
                    $"Instance foundation document persisted (sha256: {result.Sha256}, {result.Bytes} bytes{replacedSuffix}).");
                WikiIdentityLogEvents.LogDocumentPersisted(_logger, result.Sha256!, result.Bytes, result.ReplacedExisting);
                return ("document_persisted", CliExitCode.Success);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(WikiIdentityPersistOutcome)}: {result.Outcome}");
        }
    }
}
