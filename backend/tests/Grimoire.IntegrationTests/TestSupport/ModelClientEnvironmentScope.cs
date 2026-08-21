namespace Grimoire.IntegrationTests.TestSupport;

/// <summary>
/// Points a <c>Grimoire.AgentRuntime.Core.Adapters.Anthropic.AnthropicModelClient</c> at a
/// <see cref="FakeAnthropicEndpoint"/> without any two tests colliding over the same
/// process-wide variable.
/// <para>
/// The adapter takes the <em>names</em> of the model/base-url variables it reads (ADR-004
/// per-agent scoping), so each scope invents a pair of names unique to itself. Tests that
/// set the real <c>GRIMOIRE_INGEST_*</c> names raced each other under xUnit's parallel
/// collections; naming them per scope removes the shared state rather than serializing
/// access to it.
/// </para>
/// </summary>
public sealed class ModelClientEnvironmentScope : IDisposable
{
    private ModelClientEnvironmentScope(string modelEnvVar, string baseUrlEnvVar)
    {
        ModelEnvVar = modelEnvVar;
        BaseUrlEnvVar = baseUrlEnvVar;
    }

    public string ModelEnvVar { get; }

    public string BaseUrlEnvVar { get; }

    /// <summary>
    /// Sets a unique model/base-url variable pair for one adapter instance and returns the
    /// names to construct it with.
    /// </summary>
    public static ModelClientEnvironmentScope PointingAt(string baseUrl, string model = "fake-model")
    {
        var suffix = Guid.NewGuid().ToString("N");
        var scope = new ModelClientEnvironmentScope(
            $"GRIMOIRE_TEST_MODEL_{suffix}", $"GRIMOIRE_TEST_BASE_URL_{suffix}");

        Environment.SetEnvironmentVariable(scope.ModelEnvVar, model);
        Environment.SetEnvironmentVariable(scope.BaseUrlEnvVar, baseUrl);
        return scope;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ModelEnvVar, null);
        Environment.SetEnvironmentVariable(BaseUrlEnvVar, null);
    }
}
