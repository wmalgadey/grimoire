using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Shared settings for the three remediation transition commands (018-hub-cli-commands
/// T023, data-model.md "RemediationTaskSettings"): a single required, non-empty
/// <c>--task-id</c>. Mirrors <see cref="SubmitSourceSettings"/>'s
/// <c>isRequired: true</c> + <see cref="Validate"/> combo (FR-009) — Spectre performs
/// this check before <c>ExecuteAsync</c> runs, so a missing/blank value never contacts
/// the store (T026's "no store contact" assertion).
/// </summary>
public sealed class RemediationTaskSettings : HubPathSettings
{
    [CommandOption("--task-id <ID>", isRequired: true)]
    [Description("Id of the remediation task to transition (required).")]
    public string? TaskId { get; set; }

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(TaskId)
            ? ValidationResult.Error("--task-id is required and must not be empty.")
            : ValidationResult.Success();
}
