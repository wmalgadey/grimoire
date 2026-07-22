namespace Grimoire.AgentEvals;

/// <summary>
/// Groups tests that mutate real process environment variables (ANTHROPIC_AUTH_TOKEN,
/// GRIMOIRE_EVAL_PROVIDER_*) so xUnit never runs them concurrently with each other.
/// </summary>
[CollectionDefinition("EvalProviderEnvironment", DisableParallelization = true)]
public sealed class EvalEnvironmentCollection;
