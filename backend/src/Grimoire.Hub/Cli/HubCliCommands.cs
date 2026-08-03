namespace Grimoire.Hub.Cli;

/// <summary>
/// One catalog entry (018-hub-cli-commands, data-model.md "HubCliCommand"): a command's
/// name and one-line help description, plus (once its command class exists) the Spectre
/// <c>AsyncCommand&lt;TSettings&gt;</c> type that implements it.
/// </summary>
/// <param name="Name">Literal command name, e.g. <c>lint-run</c>.</param>
/// <param name="Description">One-line purpose shown in root help (FR-010 single source of truth).</param>
/// <param name="CommandType">
/// The command's Spectre command class, or <see langword="null"/> while the story that
/// implements it has not landed yet. <see cref="HubCliApp"/> registers only the entries
/// with a non-null type; <c>Program.cs</c>'s dispatch gate matches on <see cref="Name"/>
/// alone, so a catalog entry is a valid dispatch target from the moment it is listed here
/// — its command class can land in a later commit without touching the dispatch gate.
/// </param>
public sealed record HubCliCommand(string Name, string Description, Type? CommandType = null);

/// <summary>
/// Single source of truth for the Hub's command surface (FR-010): the root help's
/// <c>Commands:</c> section, <c>HubCliApp</c>'s <c>CommandApp</c> registration, and
/// <c>Program.cs</c>'s dispatch-gate check all read this one list. Descriptions are
/// sourced from each command's header in
/// <c>specs/018-hub-cli-commands/contracts/cli-commands.md</c>.
/// </summary>
public static class HubCliCommands
{
    public static readonly IReadOnlyList<HubCliCommand> All =
    [
        new("lint-run",
            "Trigger a lint run and supervise it to completion.",
            typeof(LintRunCommand)),
        new("remediation-authorize",
            "Authorize a proposed remediation task (supervises execution if eagerly dispatched)."),
        new("remediation-dismiss",
            "Dismiss a proposed remediation task."),
        new("remediation-withdraw",
            "Withdraw a remediation task's authorization."),
        new("ingest-retrigger",
            "Re-arm a queued ingest task and supervise its processing to a terminal state."),
        new("ingest-resume",
            "Resume the ingest queue and supervise it until it drains."),
        new("query",
            "Submit a query turn and block until its answer, streaming progress while waiting."),
        // 017-hub-help-usage parity (HubHelpUsageTests.ExpectedSwitches): the description
        // deliberately names --path/--source-kind so root help keeps surfacing them, like
        // the retired BuildUsageText's flat usage blob did — submit-source's own --help
        // (Spectre-generated from SubmitSourceSettings, which inherits HubPathSettings)
        // covers full option detail, including every ADR-009 path switch. No square
        // brackets here: Spectre renders ICommandInfo.Description as markup internally
        // (both the root Commands: table and each command's own description header), and
        // '[...]' is markup-tag syntax there, not literal text.
        new("submit-source",
            "Submit a source document for ingest into the wiki via --path <path>, with optional --source-kind <kind>.",
            typeof(SubmitSourceCommand)),
    ];

    static HubCliCommands()
    {
        var duplicates = All
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"HubCliCommands.All contains duplicate command name(s): {string.Join(", ", duplicates)}");
        }
    }
}
