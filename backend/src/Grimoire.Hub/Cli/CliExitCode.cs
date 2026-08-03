namespace Grimoire.Hub.Cli;

/// <summary>
/// The Hub CLI's exit-code convention (018-hub-cli-commands, research.md D5, fixed by
/// ADR-020, contracts/cli-commands.md "Global rules"). Every command's terminal outcome
/// maps to exactly one of these values; Spectre's own pre-execution failures (unknown
/// command, settings validation) are mapped to <see cref="UsageError"/> by
/// <c>HubCliApp</c> before any command's <c>ExecuteAsync</c> runs.
/// </summary>
public enum CliExitCode
{
    /// <summary>Action completed; a triggered run/turn reached its successful terminal state.</summary>
    Success = 0,

    /// <summary>A triggered run/turn/task reached a failed terminal state, or an unexpected error occurred.</summary>
    OperationFailed = 1,

    /// <summary>Unknown command, missing/malformed required argument — no action was attempted.</summary>
    UsageError = 2,

    /// <summary>The referenced id (task, run, ...) does not exist.</summary>
    NotFound = 3,

    /// <summary>The requested action conflicts with the target's current state.</summary>
    StateConflict = 4,

    /// <summary><c>query</c>'s <c>--timeout</c> elapsed; the turn was interrupted.</summary>
    WaitTimeout = 5,

    /// <summary>An interrupt signal (Ctrl-C) fired during a blocking wait; the turn was interrupted before exit.</summary>
    Cancelled = 130,
}
