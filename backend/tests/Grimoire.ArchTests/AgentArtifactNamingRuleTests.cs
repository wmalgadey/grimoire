using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural rule N1 for ADR-013 (feature 010): agent-artifact naming.
///
/// Part 1 (reference-based ownership): in the shared assemblies
/// (Grimoire.IntegrationTests, Grimoire.AgentEvals, Grimoire.ArchTests, and the
/// Grimoire.EvalRunner scenario types), a top-level type that references exactly one
/// agent's owned assemblies/namespaces must carry that agent's token ("Ingest" /
/// "Query") in its name. Unprefixed names are reserved for genuinely cross-agent
/// artifacts (referencing both agents, neither, or the platform/harness itself).
///
/// Part 2 (Hub namespace-ownership map): every Grimoire.Hub namespace is explicitly
/// classified ingest-owned, query-owned, or cross-agent. Agent-owned types must not
/// live in the shared dispatch namespace (Grimoire.Hub.AgentDispatch keeps only the
/// cross-agent port surface: IAgentProcessLauncher, AgentRunEvent, Adapters), the
/// ingest-owned namespaces must not host Query-token types and vice versa, and no Hub
/// namespace may exist outside the map. Other cross-agent namespaces (Realtime,
/// OperationalState, ...) legitimately contain per-agent endpoint types of shared
/// infrastructure (e.g. IngestLifecycleHub) — the namespace, not the type name, is the
/// ownership statement there; the convention document records this.
///
/// Exemption fixture: curated cross-agent artifacts (each justified in
/// docs/conventions/agent-artifact-naming.md; the doc↔fixture mirror assertion is
/// wired by T042 once the document exists).
///
/// The feature-010 legacy-rename baseline (remove-only ratchet) was fully emptied by
/// the US2 rename tasks and deleted by T042 — the rule now enforces the convention
/// outright, with no suppression mechanism. Proven live by Red/Green probes
/// (T002/T043).
/// </summary>
public class AgentArtifactNamingRuleTests
{
    private const string IngestToken = "Ingest";
    private const string QueryToken = "Query";
    private const string LintToken = "Lint";

    // Agent-owned namespace prefixes used for reference-based ownership detection.
    private static readonly string[] _ingestOwnedNamespacePrefixes =
    [
        "Grimoire.IngestAgent",
        "Grimoire.Hub.IngestSubmission",
        "Grimoire.Hub.IngestDispatch",
        "Grimoire.Hub.IngestTaskArtifact",
    ];

    private static readonly string[] _queryOwnedNamespacePrefixes =
    [
        "Grimoire.QueryAgent",
        "Grimoire.Hub.QueryDispatch",
        "Grimoire.Hub.QuerySubmission",
        "Grimoire.Hub.QueryRunArtifact",
    ];

    // 013-lint-agent (ADR-016): same reference-based ownership detection, third agent.
    private static readonly string[] _lintOwnedNamespacePrefixes =
    [
        "Grimoire.LintAgent",
        "Grimoire.Hub.LintDispatch",
        "Grimoire.Hub.LintFindings",
    ];

    /// <summary>
    /// Curated cross-agent exemptions (mirrored in the convention document, T027/T042):
    /// eval/replay infrastructure, platform guardrail tests, cross-agent tracing tests,
    /// and shared fixtures serve both agents or the harness itself and stay unprefixed.
    /// </summary>
    internal static readonly string[] ExemptedTypeNames =
    [
        "ReplayContractTests",
        "StalenessTests",
        "CaptureHygieneTests",
        "EvalProviderResolverTests",
        "TimeoutEnforcingModelClientTests",
        "SyntheticRecordings",
        "LocalEnvFileTests",
        "PathTraversalTests",
        "PolicyMisconfigurationTests",
        "TraceContextPropagationTests",
        "HubRequestTracingTests",
        // Cross-agent interaction test: verifies Query dispatch is independent of
        // Ingest's single-slot queue; exercises both pipelines (the query surface via
        // HTTP + shared fakes, so only ingest namespaces appear as type references).
        "QueryConcurrencyIndependenceTests",
        // Solution-wide structural rules: they enforce hexagonal/path boundaries across
        // all assemblies; their concrete forbidden-type anchors currently happen to be
        // ingest adapters, which does not make the rules ingest-owned.
        "HexagonalPortsAdapterRuleTests",
        "RuntimePathsBoundaryRuleTests",
        // T028 classification: exercises only the shared spawn/credential machinery
        // (AgentProcessHost.BuildChildEnvironment, LocalSecretsLoader) in cross-agent
        // Grimoire.Hub.AgentDispatch — the ADR-004 credential scoping applies to every
        // agent spawn, so the test is cross-agent and stays unprefixed.
        "CredentialScopingTests",
        // 014-wiki-storage-restructure (US2/SC-002): triggers a real task-artifact write
        // (Grimoire.IngestAgent.TaskArtifact, ingest-owned) and a real Conversation Record
        // append (Grimoire.Hub.QueryConversations) side by side against one resolved path
        // set — genuinely cross-agent, but QueryConversations is not one of Part 1's
        // reference-detection prefixes (only Part 2's Hub ownership map), so the scan
        // would otherwise see Ingest as the sole owner.
        "SiblingDirectoryLayoutTests",
        // 018-hub-cli-commands: these test files are the CLI command surface's
        // contract/concurrency/parity matrix, growing across every user story this
        // feature adds (US1 lint-run, US2 remediation, US3 ingest, US4 query) — cross-
        // agent by construction, like Grimoire.Hub.Cli itself (Part 2's cross-agent map
        // entry). US1 (this phase) happens to be the only story landed so far, so each
        // currently references only lint-owned namespaces; later phases' additions to
        // these same files will reference ingest/query namespaces too.
        "HubCliCommandTests",
        "HubCliConcurrencyTests",
        "HubCliParityTests",
        // 024-api-error-presentation (ADR-026): the Hub-wide HTTP failure envelope and its
        // observability contract. Cross-agent by construction — the envelope has no owning agent —
        // but reference detection sees only ingest-owned namespaces, because the lint cases reach
        // that surface through LintTriggerHostHarness instead of importing a lint namespace.
        "HubApiErrorEnvelopeTests",
        "HubApiErrorObservabilityTests",
    ];

    // Shared fixture namespaces: everything under *.Fakes is cross-agent by definition
    // (a fixture shared by ingest and query tests stays unprefixed, per the spec edge
    // case). Individual fake types that are single-agent-owned are handled by the
    // rename inventory (T041), not by this scan.
    private static readonly string[] _exemptedNamespaceSuffixes =
    [
        ".Fakes",
    ];

    // -------------------------------------------------------------------------
    // Part 1: reference-based ownership in the shared assemblies
    // -------------------------------------------------------------------------

    [Fact]
    public void SharedAssemblies_SingleAgentTypes_MustCarryTheAgentToken()
    {
        var violations = new List<string>();

        foreach (var (assemblyName, namespaceFilter) in SharedAssemblies())
        {
            var assemblyPath = System.Reflection.Assembly.Load(assemblyName).Location;
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.Types)
                {
                    if (IsCompilerGenerated(type))
                        continue;

                    if (namespaceFilter is not null &&
                        !(type.Namespace ?? string.Empty).StartsWith(namespaceFilter, StringComparison.Ordinal))
                        continue;

                    if (_exemptedNamespaceSuffixes.Any(s => (type.Namespace ?? string.Empty).EndsWith(s, StringComparison.Ordinal)))
                        continue;

                    if (ExemptedTypeNames.Contains(type.Name, StringComparer.Ordinal))
                        continue;

                    var referencesIngest = false;
                    var referencesQuery = false;
                    var referencesLint = false;
                    foreach (var ns in ReferencedNamespaces(type))
                    {
                        referencesIngest |= _ingestOwnedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                        referencesQuery |= _queryOwnedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                        referencesLint |= _lintOwnedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                    }

                    var ownerCount = (referencesIngest ? 1 : 0) + (referencesQuery ? 1 : 0) + (referencesLint ? 1 : 0);
                    if (ownerCount != 1)
                        continue; // cross-agent (2+ owners) or platform-only (0 owners): unprefixed is correct

                    var requiredToken = referencesIngest ? IngestToken : referencesQuery ? QueryToken : LintToken;
                    if (!type.Name.Contains(requiredToken, StringComparison.Ordinal))
                    {
                        violations.Add(
                            $"{assemblyName}::{type.FullName} references only {requiredToken.ToLowerInvariant()}-owned " +
                            $"namespaces but does not carry the '{requiredToken}' token in its name");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "N1 (ADR-013): a shared-assembly type referencing exactly one agent must carry that " +
            "agent's token in its name; unprefixed names are reserved for genuinely cross-agent " +
            "artifacts (docs/conventions/agent-artifact-naming.md). Rename the type (see the " +
            "convention document's rename map) or, if it is genuinely cross-agent, add a justified " +
            "exemption to the convention document and this rule's fixture. Violations:\n" +
            string.Join("\n", violations));
    }

    // -------------------------------------------------------------------------
    // Part 2: Grimoire.Hub namespace-ownership map
    // -------------------------------------------------------------------------

    private static readonly string[] _hubIngestOwnedNamespaces =
    [
        "Grimoire.Hub.IngestSubmission",
        "Grimoire.Hub.IngestDispatch",
        "Grimoire.Hub.IngestTaskArtifact",
    ];

    private static readonly string[] _hubQueryOwnedNamespaces =
    [
        "Grimoire.Hub.QueryDispatch",
        "Grimoire.Hub.QuerySubmission",
        // QueryRunArtifact was retired by 011-query-conversations (ADR-014): the
        // per-turn artifact writer is gone, replaced by the Conversation Record.
        "Grimoire.Hub.QueryConversations",
    ];

    // 013-lint-agent (ADR-016): the third agent's owned namespaces — dispatch/
    // coordination and its Findings Report store, mirroring the Ingest/Query shape.
    // 015-lint-board-parity (ADR-018): RemediationTasks is Lint-owned — proposals
    // originate from lint runs and execution/message turns are Lint-agent invocation
    // modes. Deliberately NOT in Part 1's _lintOwnedNamespacePrefixes: remediation
    // artifacts carry their own "Remediation" vocabulary instead of the Lint token
    // (tasks.md T004-T007 test naming), so referencing this namespace alone must not
    // force a Lint prefix on shared-assembly types.
    private static readonly string[] _hubLintOwnedNamespaces =
    [
        "Grimoire.Hub.LintDispatch",
        "Grimoire.Hub.LintFindings",
        "Grimoire.Hub.RemediationTasks",
    ];

    // Cross-agent Hub namespaces. Realtime/Runtime/ContentRoot/OperationalState/
    // Conversion may contain per-agent endpoint types of the shared infrastructure
    // (IngestLifecycleHub, QueuedIngestRun, ...); AgentDispatch is held to the stricter
    // standard below (only the shared port surface). 018-hub-cli-commands (ADR-020, N1):
    // Grimoire.Hub.Cli is the CLI command surface — a cross-agent entry point that may
    // host agent-token command types (LintRunCommand, QueryCommand, ...) as per-agent
    // entries of shared infrastructure, like Realtime.
    private static readonly string[] _hubCrossAgentNamespaces =
    [
        "Grimoire.Hub.AgentDispatch",
        "Grimoire.Hub.Realtime",
        "Grimoire.Hub.Runtime",
        "Grimoire.Hub.ContentRoot",
        "Grimoire.Hub.OperationalState",
        "Grimoire.Hub.Conversion",
        "Grimoire.Hub.Cli",
        // 024-api-error-presentation (ADR-026, N1): the HTTP failure contract. Serves the
        // ingest, query, lint and remediation endpoint families and the Hub's own
        // unhandled-exception path, so it is cross-agent by construction — and per BR1 it
        // is the only namespace permitted to produce an error result at all.
        "Grimoire.Hub.ApiErrors",
    ];

    [Fact]
    public void HubNamespaces_MustFollowTheOwnershipMap()
    {
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.Hub").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.Types)
            {
                if (IsCompilerGenerated(type))
                    continue;

                var ns = type.Namespace ?? string.Empty;

                // Global namespace (top-level-statement Program) and the assembly root
                // (HubTracing, HubMetrics, TelemetryExtensions) are cross-agent hosting
                // infrastructure.
                if (ns.Length == 0 || ns == "Grimoire.Hub")
                    continue;

                var mapped = _hubIngestOwnedNamespaces.Concat(_hubQueryOwnedNamespaces).Concat(_hubLintOwnedNamespaces).Concat(_hubCrossAgentNamespaces)
                    .Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                if (!mapped)
                {
                    violations.Add($"{type.FullName}: namespace '{ns}' is not in the ADR-013 Hub ownership map " +
                                   "(ingest-owned, query-owned, lint-owned, or cross-agent)");
                    continue;
                }

                var inIngestOwned = _hubIngestOwnedNamespaces.Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                var inQueryOwned = _hubQueryOwnedNamespaces.Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                var inLintOwned = _hubLintOwnedNamespaces.Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                var inSharedDispatch = ns == "Grimoire.Hub.AgentDispatch" || ns.StartsWith("Grimoire.Hub.AgentDispatch.", StringComparison.Ordinal);

                if (inIngestOwned && (type.Name.Contains(QueryToken, StringComparison.Ordinal) || type.Name.Contains(LintToken, StringComparison.Ordinal)))
                    violations.Add($"{type.FullName}: another agent's token type in the ingest-owned namespace '{ns}'");

                if (inQueryOwned && (type.Name.Contains(IngestToken, StringComparison.Ordinal) || type.Name.Contains(LintToken, StringComparison.Ordinal)))
                    violations.Add($"{type.FullName}: another agent's token type in the query-owned namespace '{ns}'");

                if (inLintOwned && (type.Name.Contains(IngestToken, StringComparison.Ordinal) || type.Name.Contains(QueryToken, StringComparison.Ordinal)))
                    violations.Add($"{type.FullName}: another agent's token type in the lint-owned namespace '{ns}'");

                // The shared dispatch namespace keeps only the cross-agent port surface
                // (IAgentProcessLauncher, AgentRunEvent, Adapters.AgentProcess): agent-token
                // types belong in IngestDispatch / QueryDispatch / LintDispatch.
                if (inSharedDispatch &&
                    (type.Name.Contains(IngestToken, StringComparison.Ordinal) ||
                     type.Name.Contains(QueryToken, StringComparison.Ordinal) ||
                     type.Name.Contains(LintToken, StringComparison.Ordinal)))
                {
                    violations.Add($"{type.FullName}: agent-owned type in the cross-agent namespace '{ns}' " +
                                   "(belongs in Grimoire.Hub.IngestDispatch / Grimoire.Hub.QueryDispatch / Grimoire.Hub.LintDispatch)");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "N1 (ADR-013): Grimoire.Hub namespace ownership violated " +
            "(docs/conventions/agent-artifact-naming.md). Violations:\n" +
            string.Join("\n", violations));
    }

    // -------------------------------------------------------------------------
    // Doc↔fixture mirror (T042): the exemption list lives in the convention document
    // with a justification per entry; this test parses it and fails on any drift
    // between document and in-test fixture, in either direction.
    // -------------------------------------------------------------------------

    [Fact]
    public void ExemptionFixture_MustMirror_TheConventionDocument()
    {
        var documentPath = FindConventionDocument();
        var documentText = File.ReadAllText(documentPath);

        var sectionStart = documentText.IndexOf("## Exemption list", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, $"'{documentPath}' must contain an '## Exemption list' section.");
        var sectionEnd = documentText.IndexOf("\n## ", sectionStart + 1, StringComparison.Ordinal);
        var section = sectionEnd > 0 ? documentText[sectionStart..sectionEnd] : documentText[sectionStart..];

        var documented = System.Text.RegularExpressions.Regex
            .Matches(section, @"^\| `([A-Za-z0-9_]+)` \|", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var fixture = ExemptedTypeNames.ToHashSet(StringComparer.Ordinal);

        var onlyInDocument = documented.Except(fixture).Order().ToList();
        var onlyInFixture = fixture.Except(documented).Order().ToList();

        Assert.True(
            onlyInDocument.Count == 0 && onlyInFixture.Count == 0,
            "N1 (ADR-013): the exemption list in docs/conventions/agent-artifact-naming.md and the " +
            "in-test fixture must mirror each other exactly. " +
            $"Only in document: [{string.Join(", ", onlyInDocument)}]; " +
            $"only in fixture: [{string.Join(", ", onlyInFixture)}]");
    }

    private static string FindConventionDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "conventions", "agent-artifact-naming.md");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "docs/conventions/agent-artifact-naming.md not found in any parent of " + AppContext.BaseDirectory);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IEnumerable<(string AssemblyName, string? NamespaceFilter)> SharedAssemblies()
    {
        yield return ("Grimoire.IntegrationTests", null);
        yield return ("Grimoire.AgentEvals", null);
        yield return ("Grimoire.ArchTests", null);
        // EvalRunner: only the scenario types are in N1 scope; the capture/replay/
        // workspace/scoring machinery is cross-agent platform code (research.md R5).
        yield return ("Grimoire.EvalRunner", "Grimoire.EvalRunner.Scenarios");
    }

    private static bool IsCompilerGenerated(TypeDefinition type)
        => type.Name.StartsWith("<", StringComparison.Ordinal) ||
           type.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute");

    /// <summary>
    /// Every namespace the type (incl. nested/state-machine types) references: base
    /// types, interfaces, fields, method signatures, method bodies, and custom
    /// attributes.
    /// </summary>
    private static IEnumerable<string> ReferencedNamespaces(TypeDefinition type)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        void AddTypeRef(TypeReference? reference)
        {
            if (reference is null)
                return;

            var element = reference.GetElementType();
            var ns = element.Namespace;
            if (element.IsNested)
            {
                var declaring = element.DeclaringType;
                while (declaring is { IsNested: true })
                    declaring = declaring.DeclaringType;
                ns = declaring?.Namespace ?? ns;
            }

            if (!string.IsNullOrEmpty(ns))
                namespaces.Add(ns);

            if (reference is IGenericInstance generic)
            {
                foreach (var arg in generic.GenericArguments)
                    AddTypeRef(arg);
            }
        }

        void Walk(TypeDefinition current)
        {
            AddTypeRef(current.BaseType);

            foreach (var iface in current.Interfaces)
                AddTypeRef(iface.InterfaceType);

            foreach (var attribute in current.CustomAttributes)
                AddTypeRef(attribute.AttributeType);

            foreach (var field in current.Fields)
                AddTypeRef(field.FieldType);

            foreach (var property in current.Properties)
                AddTypeRef(property.PropertyType);

            foreach (var method in current.Methods)
            {
                AddTypeRef(method.ReturnType);
                foreach (var parameter in method.Parameters)
                    AddTypeRef(parameter.ParameterType);

                if (!method.HasBody)
                    continue;

                foreach (var variable in method.Body.Variables)
                    AddTypeRef(variable.VariableType);

                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case MethodReference callee:
                            AddTypeRef(callee.DeclaringType);
                            AddTypeRef(callee.ReturnType);
                            foreach (var parameter in callee.Parameters)
                                AddTypeRef(parameter.ParameterType);
                            break;
                        case FieldReference fieldRef:
                            AddTypeRef(fieldRef.DeclaringType);
                            AddTypeRef(fieldRef.FieldType);
                            break;
                        case TypeReference typeRef:
                            AddTypeRef(typeRef);
                            break;
                    }
                }
            }

            foreach (var nested in current.NestedTypes)
                Walk(nested);
        }

        Walk(type);
        return namespaces;
    }
}
