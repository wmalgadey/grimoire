using System.Globalization;
using Grimoire.AgentRuntime.Core;

namespace Grimoire.LintAgent;

/// <summary>
/// Lint's per-run spend limit, resolved from <c>GRIMOIRE_LINT_SPEND_CAP</c> — the knob
/// Ingest has had since #107 and Lint did not.
/// <para>
/// Lint constructed its <see cref="AgentLoop"/> without a <c>spendTokenCap</c> argument,
/// so every Lint invocation ran at <see cref="AgentLoop.DefaultSpendTokenCap"/> with no
/// way for an operator to change it — not through the secrets file, not through the CLI.
/// On a production-size wiki that is the binding constraint: a survey run against 655
/// pages spent 1,002,601 input tokens for 2,923 output tokens across ten turns and died
/// on the default 1,000,000 cap at turn 10 of 50, while its peak context stayed at
/// 120,191 — well inside the context guard. The run was affordable to the operator and
/// unaffordable to a limit they could not reach.
/// </para>
/// <para>
/// Deliberately no legacy alias: unlike <c>GRIMOIRE_INGEST_SPEND_CAP</c>, which carries
/// <c>GRIMOIRE_INGEST_TOKEN_CAP</c> from before #107 renamed it, this variable has never
/// had another name and does not need one.
/// </para>
/// </summary>
public static class LintSpendCap
{
    /// <summary>The one name this setting answers to.</summary>
    public const string EnvironmentVariableName = "GRIMOIRE_LINT_SPEND_CAP";

    /// <summary>
    /// The cap for this process, or <see cref="AgentLoop.DefaultSpendTokenCap"/> when the
    /// variable is unset or unusable.
    /// </summary>
    public static int ResolveFromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// The parse-or-default rule, separated from the environment read so it is testable
    /// without mutating process-global state from a parallel test run.
    /// <para>
    /// A value that is absent, non-numeric, zero or negative falls back to the default
    /// rather than failing the run: unlike the model id (#117), which has no safe default
    /// and therefore fails closed, a spend cap has one — the value every Lint run used
    /// before this variable existed. Falling back keeps a typo from taking Lint offline,
    /// and the effective cap is named in the failure message when it is reached.
    /// </para>
    /// </summary>
    public static int Resolve(string? raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : AgentLoop.DefaultSpendTokenCap;
}
