using Grimoire.Hub.Cli;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Invokes the production <see cref="WikiIdentityCommand"/> via <see cref="ICommand{TSettings}"/>
/// directly (029-shared-foundation-prompt T038-T045), mirroring
/// <c>HubCliCommandTests.RunLintRunCommandAsync</c>'s idiom: bypasses Spectre's own
/// argument-parsing pipeline (out-of-process parsing is covered separately, per that same
/// precedent), but still runs <see cref="WikiIdentitySettings.Validate"/> first — a Spectre
/// invocation never reaches <c>ExecuteAsync</c> on a validation failure, so neither does this.
/// </summary>
internal static class WikiIdentityCommandTestHarness
{
    public static async Task<(int ExitCode, string Stdout)> RunSetAsync(
        ResolvedGrimoirePaths paths,
        ILogger<WikiIdentityCommand>? logger = null,
        bool @default = false,
        bool specialised = false,
        string? description = null,
        string? fromFile = null,
        bool replace = false,
        CancellationToken cancellationToken = default)
    {
        var settings = new WikiIdentitySettings
        {
            Action = "set",
            Default = @default,
            Specialised = specialised,
            Description = description,
            FromFile = fromFile,
            Replace = replace,
        };

        var validation = settings.Validate();
        if (!validation.Successful)
        {
            return ((int)CliExitCode.UsageError, validation.Message ?? string.Empty);
        }

        var stdout = new StringWriter();
        var command = new WikiIdentityCommand(paths, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WikiIdentityCommand>.Instance, stdout);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "wiki-identity", null);

        var exitCode = await ((ICommand<WikiIdentitySettings>)command).ExecuteAsync(context, settings, cancellationToken);
        return (exitCode, stdout.ToString());
    }

    public static async Task<(int ExitCode, string Stdout)> RunReportAsync(
        ResolvedGrimoirePaths paths,
        ILogger<WikiIdentityCommand>? logger = null,
        CancellationToken cancellationToken = default)
    {
        var settings = new WikiIdentitySettings();

        var validation = settings.Validate();
        if (!validation.Successful)
        {
            return ((int)CliExitCode.UsageError, validation.Message ?? string.Empty);
        }

        var stdout = new StringWriter();
        var command = new WikiIdentityCommand(paths, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WikiIdentityCommand>.Instance, stdout);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "wiki-identity", null);

        var exitCode = await ((ICommand<WikiIdentitySettings>)command).ExecuteAsync(context, settings, cancellationToken);
        return (exitCode, stdout.ToString());
    }

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public static readonly EmptyRemainingArguments Instance = new();

        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);

        public IReadOnlyList<string> Raw { get; } = [];
    }
}
