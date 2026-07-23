namespace Grimoire.EvalRunner.Providers;

/// <summary>Which provider (if any) is active for a live capture run (007 data model, moved here by 009).</summary>
public enum ProviderKind
{
    None,
    Anthropic,
    Affordable,
}

/// <summary>
/// The resolved, validated configuration for one live eval run. A configuration is
/// "affordable-complete" only when all three of BaseUrl, Model, and the credential are
/// non-empty; a partial affordable configuration does not count as present.
/// </summary>
public sealed record ProviderConfiguration(ProviderKind Kind, string? BaseUrl, string? Model, bool HasCredential)
{
    public static readonly ProviderConfiguration None = new(ProviderKind.None, null, null, false);
}

/// <summary>Outcome status of resolving the eval provider gate.</summary>
public enum EvalGateStatus
{
    Enabled,
    Skipped,
    ConfigurationError,
}

/// <summary>
/// The result of resolving <see cref="ProviderConfiguration"/>.
/// <see cref="Reason"/> is null when <see cref="Status"/> is <see cref="EvalGateStatus.Enabled"/>.
/// </summary>
public sealed record EvalGateOutcome(EvalGateStatus Status, ProviderConfiguration Configuration, string? Reason);

/// <summary>
/// Resolves which model provider (if any) serves a live capture run, as a pure function
/// of environment variables (007 contracts/eval-provider-env-vars.md — semantics
/// unchanged by the 009 move from Grimoire.AgentEvals into the eval runner).
/// </summary>
public static class EvalProviderResolver
{
    public const string NeitherConfiguredReason =
        "Set ANTHROPIC_AUTH_TOKEN, or all three of GRIMOIRE_EVAL_PROVIDER_BASE_URL/" +
        "GRIMOIRE_EVAL_PROVIDER_MODEL/GRIMOIRE_EVAL_PROVIDER_API_KEY, to run live agent-behavior evals.";

    public const string BothConfiguredReason =
        "Both ANTHROPIC_AUTH_TOKEN and a complete GRIMOIRE_EVAL_PROVIDER_* configuration " +
        "(BASE_URL + MODEL + API_KEY) are set. Configure exactly one provider for agent evals.";

    public static EvalGateOutcome Resolve() => Resolve(Environment.GetEnvironmentVariable);

    public static EvalGateOutcome Resolve(Func<string, string?> getEnvironmentVariable)
    {
        var anthropicToken = getEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var anthropicPresent = !string.IsNullOrWhiteSpace(anthropicToken);

        var affordableBaseUrl = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL");
        var affordableModel = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL");
        var affordableApiKey = getEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY");
        var affordableComplete = !string.IsNullOrWhiteSpace(affordableBaseUrl)
            && !string.IsNullOrWhiteSpace(affordableModel)
            && !string.IsNullOrWhiteSpace(affordableApiKey);

        if (anthropicPresent && affordableComplete)
        {
            return new EvalGateOutcome(EvalGateStatus.ConfigurationError, ProviderConfiguration.None, BothConfiguredReason);
        }

        if (anthropicPresent)
        {
            var anthropicModel = getEnvironmentVariable("GRIMOIRE_INGEST_MODEL");
            var configuration = new ProviderConfiguration(ProviderKind.Anthropic, BaseUrl: null, Model: anthropicModel, HasCredential: true);
            return new EvalGateOutcome(EvalGateStatus.Enabled, configuration, Reason: null);
        }

        if (affordableComplete)
        {
            var configuration = new ProviderConfiguration(ProviderKind.Affordable, affordableBaseUrl, affordableModel, HasCredential: true);
            return new EvalGateOutcome(EvalGateStatus.Enabled, configuration, Reason: null);
        }

        return new EvalGateOutcome(EvalGateStatus.Skipped, ProviderConfiguration.None, NeitherConfiguredReason);
    }

    /// <summary>
    /// Scrubs both eval-provider credential sources from any runner output (007 FR-008,
    /// 009 FR-011): the ANTHROPIC_AUTH_TOKEN value, the GRIMOIRE_EVAL_PROVIDER_API_KEY
    /// value, and sk-ant-shaped tokens.
    /// </summary>
    public static string SanitizeErrorText(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown eval error.";
        }

        var sanitized = message;

        var anthropicToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(anthropicToken))
        {
            sanitized = sanitized.Replace(anthropicToken, "[REDACTED]", StringComparison.Ordinal);
        }

        var providerApiKey = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY");
        if (!string.IsNullOrWhiteSpace(providerApiKey))
        {
            sanitized = sanitized.Replace(providerApiKey, "[REDACTED]", StringComparison.Ordinal);
        }

        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return sanitized;
    }
}
