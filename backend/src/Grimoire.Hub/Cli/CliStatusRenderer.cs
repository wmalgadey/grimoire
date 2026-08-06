using Spectre.Console;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Live status/event rendering for a blocking CLI command, written to <b>stderr only</b>
/// (018-hub-cli-commands FR-006 / contracts/cli-commands.md "Global rules": stdout
/// carries exactly the result contract, never progress output). Backed by a dedicated
/// <see cref="IAnsiConsole"/> instance targeting <see cref="Console.Error"/> — the
/// default static <c>AnsiConsole</c> writes to stdout, which would violate the
/// stdout-is-result-only contract.
///
/// Kept minimal for 018 Phase 2 (the shared command-framework foundation): a single
/// status-line write. Per-command status displays (run-id-at-start, streamed answer
/// deltas, ...) are added by the user-story phases that need them (US1–US4).
/// </summary>
public sealed class CliStatusRenderer
{
    private readonly IAnsiConsole _console;

    public CliStatusRenderer()
        : this(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }))
    {
    }

    /// <summary>Test seam: inject a pre-built console (e.g. one wrapping a captured <see cref="TextWriter"/>).</summary>
    public CliStatusRenderer(IAnsiConsole console)
    {
        _console = console;
    }

    /// <summary>Writes one status/event line to stderr.</summary>
    public void WriteLine(string message) => _console.WriteLine(message);
}
