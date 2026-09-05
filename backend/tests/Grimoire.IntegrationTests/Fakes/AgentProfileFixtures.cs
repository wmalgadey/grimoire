using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.Instructions;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// 029-shared-foundation-prompt (T025/T028): the same three <see cref="AgentProfile"/>
/// shapes each real Program.cs constructs in its composition root, reused here because
/// none of the three agent projects declares <c>InternalsVisibleTo</c> for the test
/// assembly — their own <c>Program.cs</c>-internal intent handlers are not directly
/// testable. Tests that need the real shared <see cref="AgentHost"/> composition point
/// exercised for all three agent shapes construct the host with one of these profiles
/// and a hand-rolled <see cref="IAgentIntentHandler"/> instead.
/// </summary>
internal static class AgentProfileFixtures
{
    public static readonly AgentProfile Ingest = new(
        AgentName: "ingest",
        ServiceName: "Grimoire.IngestAgent",
        ActivitySourceName: "Grimoire.IngestAgent",
        MeterName: "Grimoire.IngestAgent",
        RunSpanName: "ingest_agent.run",
        CorrelationAttribute: "task_id",
        ToolRegistry: Grimoire.IngestAgent.IngestToolRegistry.Default,
        RequiredInstructionDocuments: new HashSet<InstructionDocument>
        {
            InstructionDocument.SystemPrompt,
            InstructionDocument.DefaultUserPrompt,
        },
        ModelEnvVarNames: new ModelEnvVarNames(
            "GRIMOIRE_INGEST_MODEL", "GRIMOIRE_INGEST_BASE_URL", "GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS"));

    public static readonly AgentProfile Query = new(
        AgentName: "query",
        ServiceName: "Grimoire.QueryAgent",
        ActivitySourceName: "Grimoire.QueryAgent",
        MeterName: "Grimoire.QueryAgent",
        RunSpanName: "query_agent.run",
        CorrelationAttribute: "turn_id",
        ToolRegistry: Grimoire.QueryAgent.QueryToolRegistry.Default,
        RequiredInstructionDocuments: new HashSet<InstructionDocument>
        {
            InstructionDocument.SystemPrompt,
        },
        ModelEnvVarNames: new ModelEnvVarNames(
            "GRIMOIRE_QUERY_MODEL", "GRIMOIRE_QUERY_BASE_URL", "GRIMOIRE_QUERY_MAX_OUTPUT_TOKENS"));

    public static readonly AgentProfile Lint = new(
        AgentName: "lint",
        ServiceName: "Grimoire.LintAgent",
        ActivitySourceName: "Grimoire.LintAgent",
        MeterName: "Grimoire.LintAgent",
        RunSpanName: "lint_agent.run",
        CorrelationAttribute: "run_id",
        ToolRegistry: Grimoire.LintAgent.LintToolRegistry.Default,
        RequiredInstructionDocuments: new HashSet<InstructionDocument>
        {
            InstructionDocument.SystemPrompt,
        },
        ModelEnvVarNames: new ModelEnvVarNames(
            "GRIMOIRE_LINT_MODEL", "GRIMOIRE_LINT_BASE_URL", "GRIMOIRE_LINT_MAX_OUTPUT_TOKENS"));

    /// <summary>All three profiles, named, for parameterized "every agent type" tests.</summary>
    public static IEnumerable<object[]> AllProfiles()
    {
        yield return [Ingest];
        yield return [Query];
        yield return [Lint];
    }
}
