namespace Grimoire.Domain.Guardrails;

/// <summary>
/// The four reserved harness-owned surfaces that live inside the wiki content root
/// (ADR-022 placed them there; ADR-023, 022-align-wiki-structure Phase 5, closes the
/// agent-visibility gap that placement opened): <c>tasks/</c> (Ingest task artifacts),
/// <c>conversations/</c> (Query conversation records), <c>findings/</c> (Lint findings
/// reports), and <c>remediation-tasks/</c> (remediation task records). These are harness
/// bookkeeping, not wiki content.
///
/// <b>H2 (Constitution Principle III, <c>Grimoire.ArchTests.HarnessSurfaceScopeRuleTests</c>):</b>
/// this is the ONLY production file permitted to declare the complete four-name set
/// together as C# string literals. Every other production file that needs one or more of
/// these names must reference the members below rather than re-declare the literals — a
/// single incidental reserved word elsewhere (e.g. an unrelated "tasks" subdirectory) is
/// fine; redeclaring the whole set is not.
///
/// <b>Located in Grimoire.Domain.Guardrails, not Grimoire.Hub</b> (a deliberate placement
/// choice, documented here since a reasonable reader would expect it in
/// <c>Grimoire.Hub.HarnessSurfaces</c> given ADR-023's prose): both the Hub composition
/// (which maps <c>HarnessSurfaceReadOptions</c>' four booleans to the granted-surface CLI
/// argument) and each agent process' composition (which maps the received granted set to
/// its complement, per ADR-023 "Enforcement: subtractive read scope") need the same four
/// names, and <c>Grimoire.Domain</c> — plain string constants, zero dependencies — is the
/// one assembly every one of those call sites (Hub, and Ingest/Query/Lint, none of which
/// reference <c>Grimoire.Hub</c> per ADR-002's child-process-only boundary) already
/// references, so no new project reference is needed anywhere. This mirrors
/// <see cref="SafetyPolicy"/>/<see cref="PolicyDecision"/> already living here as the pure,
/// dependency-free guardrail vocabulary shared by the Hub and every agent host (H1 is
/// satisfied trivially — plain <c>const string</c>/array, no config or I/O dependency).
/// <see cref="Grimoire.Hub.HarnessSurfaces.HarnessSurfaceReadOptions"/> (the
/// operator-facing configuration record, which DOES bind <c>IConfiguration</c>) stays in
/// <c>Grimoire.Hub</c>, where every other <c>Grimoire:*</c> options record lives.
/// </summary>
public static class ReservedHarnessSurfaces
{
    public const string Tasks = "tasks";
    public const string Conversations = "conversations";
    public const string Findings = "findings";
    public const string RemediationTasks = "remediation-tasks";

    /// <summary>The complete reserved set, in the fixed order used for CLI arguments and provenance records.</summary>
    public static readonly IReadOnlyList<string> All = [Tasks, Conversations, Findings, RemediationTasks];
}
