using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.AgentRuntime.Host;

/// <summary>
/// The ADR-007 instruction documents an agent's host requires before a run may start
/// (fail-closed load). The system prompt is required by every agent; Ingest additionally
/// requires the versioned default-user-prompt document.
/// </summary>
public enum InstructionDocument
{
    SystemPrompt,
    DefaultUserPrompt,
}

/// <summary>
/// The per-agent declaration that fully distinguishes one agent from another (ADR-013,
/// feature 010 data-model.md): identity, frozen telemetry identities, the complete tool
/// set, required instruction documents, and the ADR-004 model/base-url env-var names.
/// One instance per host assembly, constructed in that host's composition root.
/// In-memory only — never serialized. A profile never contains agent-conditional
/// platform behavior (FR-002); all identity fields are frozen constants asserted by the
/// existing observability/guardrail tests (FR-008).
/// </summary>
public sealed record AgentProfile(
    string AgentName,
    string ServiceName,
    string ActivitySourceName,
    string MeterName,
    string RunSpanName,
    string CorrelationAttribute,
    ToolRegistry ToolRegistry,
    IReadOnlySet<InstructionDocument> RequiredInstructionDocuments,
    ModelEnvVarNames ModelEnvVarNames);
