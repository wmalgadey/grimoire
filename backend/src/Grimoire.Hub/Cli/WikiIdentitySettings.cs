using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Settings for <see cref="WikiIdentityCommand"/> (029-shared-foundation-prompt T038,
/// contracts/wiki-identity-cli.md). A single positional <c>&lt;ACTION&gt;</c> — only
/// <c>set</c> is recognised at this layer (US2, the wizard); reporting the identity in
/// effect with no action is US3's own task (T054) — plus the four <c>set</c> options,
/// exactly one of which must be given per invocation (FR-011, FR-013, FR-013a). All
/// checks run in <see cref="Validate"/>, which Spectre calls before <c>ExecuteAsync</c> —
/// a malformed invocation never touches the filesystem (FR-015/FR-016: no answer ever
/// waits for input, and a missing one changes nothing).
/// </summary>
public sealed class WikiIdentitySettings : HubPathSettings
{
    [CommandArgument(0, "<ACTION>")]
    [Description("Only 'set' is supported.")]
    public string? Action { get; set; }

    [CommandOption("--default")]
    [Description("Keep the shipped default foundation document. Writes nothing.")]
    public bool Default { get; set; }

    [CommandOption("--specialised")]
    [Description("Emit a drafting brief from --description. Writes nothing.")]
    public bool Specialised { get; set; }

    [CommandOption("--description <TEXT>")]
    [Description("Plain-language description of the wiki to maintain (required with --specialised; '-' reads it from stdin).")]
    public string? Description { get; set; }

    [CommandOption("--from-file <PATH>")]
    [Description("Path to a drafted foundation document to validate and persist verbatim.")]
    public string? FromFile { get; set; }

    [CommandOption("--replace")]
    [Description("Permit replacing an existing instance document (only valid with --from-file).")]
    public bool Replace { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrEmpty(Action))
        {
            return ValidationResult.Error("<ACTION> is required: wiki-identity set ...");
        }

        if (!string.Equals(Action, "set", StringComparison.Ordinal))
        {
            return ValidationResult.Error($"Unknown action '{Action}'. Only 'set' is supported: wiki-identity set ...");
        }

        var answerCount = new[] { Default, Specialised, FromFile is not null }.Count(answered => answered);
        if (answerCount == 0)
        {
            return ValidationResult.Error(
                "wiki-identity set requires exactly one of --default, --specialised, or --from-file.");
        }

        if (answerCount > 1)
        {
            return ValidationResult.Error(
                "wiki-identity set accepts only one of --default, --specialised, or --from-file.");
        }

        if (Specialised && string.IsNullOrEmpty(Description))
        {
            return ValidationResult.Error(
                "--specialised requires --description <text> (or '-' to read the description from stdin).");
        }

        if (!Specialised && Description is not null)
        {
            return ValidationResult.Error("--description is only valid with --specialised.");
        }

        if (Replace && FromFile is null)
        {
            return ValidationResult.Error("--replace is only valid with --from-file.");
        }

        return ValidationResult.Success();
    }
}
