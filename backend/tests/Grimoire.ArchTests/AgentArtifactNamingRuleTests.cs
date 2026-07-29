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
/// Legacy-rename baseline (ratchet): the complete inventory of today's violations
/// (research.md R5 + the T008 report-mode sweep). Entries may only ever be REMOVED
/// (as US2's rename tasks land) — adding an entry is a review-reject. The whole
/// mechanism is deleted by T042/T050 once the baseline is empty; some entries
/// (e.g. AgentCliOptions, Grimoire.IngestAgent.AgentCore.*) double as the
/// authoritative rename inventory even where this scan cannot flag them mechanically.
/// Proven live by a Red/Green probe (T002).
/// </summary>
public class AgentArtifactNamingRuleTests
{
    private const string IngestToken = "Ingest";
    private const string QueryToken = "Query";

    // Agent-owned namespace prefixes used for reference-based ownership detection.
    // Grimoire.Hub.Submission / Grimoire.Hub.TaskArtifact are the legacy (pre-rename)
    // ingest-owned Hub namespaces; they move to IngestSubmission / IngestTaskArtifact
    // in US2 (T037/T038).
    private static readonly string[] _ingestOwnedNamespacePrefixes =
    [
        "Grimoire.IngestAgent",
        "Grimoire.Hub.IngestSubmission",
        "Grimoire.Hub.IngestDispatch",
        "Grimoire.Hub.IngestTaskArtifact",
        "Grimoire.Hub.Submission",
        "Grimoire.Hub.TaskArtifact",
    ];

    private static readonly string[] _queryOwnedNamespacePrefixes =
    [
        "Grimoire.QueryAgent",
        "Grimoire.Hub.QueryDispatch",
        "Grimoire.Hub.QuerySubmission",
        "Grimoire.Hub.QueryRunArtifact",
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
    ];

    // Shared fixture namespaces: everything under *.Fakes is cross-agent by definition
    // (a fixture shared by ingest and query tests stays unprefixed, per the spec edge
    // case). Individual fake types that are single-agent-owned are handled by the
    // rename inventory (T041), not by this scan.
    private static readonly string[] _exemptedNamespaceSuffixes =
    [
        ".Fakes",
    ];

    /// <summary>
    /// Legacy-rename baseline. Matches by simple type name, by full name, or — for
    /// entries ending in ".*" — by namespace prefix. REMOVE-ONLY (see class docs).
    /// </summary>
    internal static readonly List<string> LegacyRenameBaseline =
    [
        // research.md R5 inventory (renamed by T031-T039)
        "ReplayEvalTests",
        "ScenarioDefinitions",
        "GuardedWriteBoundaryRuleTests",
        "ObservabilityLogTests",
        "ObservabilityMetricsTests",
        "ObservabilityTraceTests",
        "AgentRunLifecycleTests",
        "AgentTaskArtifactTests",
        "InstructionContextTests",
        "InstructionLoadFailureTests",
        "UserPromptTests",
        "RunQueueTests",
        "RunSupervisionTests",
        "RunActivityRealtimeTests",
        "FailureAndReconciliationTests",
        "ConvertStepTests",
        "SourceArtifactPersistenceTests",
        "SubmissionPromptApiTests",
        "Grimoire.Hub.AgentDispatch.IngestRunCoordinator",
        "Grimoire.Hub.AgentDispatch.IngestAgentRequest",
        "Grimoire.Hub.AgentDispatch.QueryAgentRequest",
        "Grimoire.Hub.AgentDispatch.QueryPriorTurn", // report-mode find: moves to QueryDispatch with QueryAgentRequest (T036)
        "Grimoire.Hub.Submission.*",
        "Grimoire.Hub.TaskArtifact.*",
        "AgentCliOptions",
        "Grimoire.IngestAgent.AgentCore.*",
        // T008 report-mode sweep: additional single-agent-owned unprefixed artifacts
        // R5 missed (settled by T029/T041: rename or classify cross-agent).
        "TaskRecordApiTests",
        "TaskRecordLogEventTests",
        "TaskRecordMetricsTests",
        "TaskRecordTraceTests",
        "TaskRecordWatcherTests",
        "OperationalStateAndDispatchTests",
        "KanbanBoardApiTests",
        "GovernanceIdentityTests",
        "ReplayAdapterTests",
        "UrlContentFetcherTests",
        "CredentialScopingTests",
        "DispatchPathArgumentsTests",
        "RepoLessStartupTests",
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

                    if (IsBaselined(type))
                        continue;

                    var referencesIngest = false;
                    var referencesQuery = false;
                    foreach (var ns in ReferencedNamespaces(type))
                    {
                        referencesIngest |= _ingestOwnedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                        referencesQuery |= _queryOwnedNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                    }

                    if (referencesIngest == referencesQuery)
                        continue; // cross-agent (both) or platform-only (neither): unprefixed is correct

                    var requiredToken = referencesIngest ? IngestToken : QueryToken;
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
        "Grimoire.Hub.QueryRunArtifact",
    ];

    // Cross-agent Hub namespaces. Realtime/Runtime/ContentRoot/OperationalState/
    // Conversion may contain per-agent endpoint types of the shared infrastructure
    // (IngestLifecycleHub, QueuedIngestRun, ...); AgentDispatch is held to the stricter
    // standard below (only the shared port surface).
    private static readonly string[] _hubCrossAgentNamespaces =
    [
        "Grimoire.Hub.AgentDispatch",
        "Grimoire.Hub.Realtime",
        "Grimoire.Hub.Runtime",
        "Grimoire.Hub.ContentRoot",
        "Grimoire.Hub.OperationalState",
        "Grimoire.Hub.Conversion",
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

                if (IsBaselined(type))
                    continue;

                var mapped = _hubIngestOwnedNamespaces.Concat(_hubQueryOwnedNamespaces).Concat(_hubCrossAgentNamespaces)
                    .Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                if (!mapped)
                {
                    violations.Add($"{type.FullName}: namespace '{ns}' is not in the ADR-013 Hub ownership map " +
                                   "(ingest-owned, query-owned, or cross-agent)");
                    continue;
                }

                var inIngestOwned = _hubIngestOwnedNamespaces.Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                var inQueryOwned = _hubQueryOwnedNamespaces.Any(m => ns == m || ns.StartsWith(m + ".", StringComparison.Ordinal));
                var inSharedDispatch = ns == "Grimoire.Hub.AgentDispatch" || ns.StartsWith("Grimoire.Hub.AgentDispatch.", StringComparison.Ordinal);

                if (inIngestOwned && type.Name.Contains(QueryToken, StringComparison.Ordinal))
                    violations.Add($"{type.FullName}: Query-token type in the ingest-owned namespace '{ns}'");

                if (inQueryOwned && type.Name.Contains(IngestToken, StringComparison.Ordinal))
                    violations.Add($"{type.FullName}: Ingest-token type in the query-owned namespace '{ns}'");

                // The shared dispatch namespace keeps only the cross-agent port surface
                // (IAgentProcessLauncher, AgentRunEvent, Adapters.AgentProcess): agent-token
                // types belong in IngestDispatch / QueryDispatch.
                if (inSharedDispatch &&
                    (type.Name.Contains(IngestToken, StringComparison.Ordinal) ||
                     type.Name.Contains(QueryToken, StringComparison.Ordinal)))
                {
                    violations.Add($"{type.FullName}: agent-owned type in the cross-agent namespace '{ns}' " +
                                   "(belongs in Grimoire.Hub.IngestDispatch / Grimoire.Hub.QueryDispatch)");
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

    private static bool IsBaselined(TypeDefinition type)
    {
        foreach (var entry in LegacyRenameBaseline)
        {
            if (entry.EndsWith(".*", StringComparison.Ordinal))
            {
                var prefix = entry[..^2];
                if ((type.Namespace ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            else if (type.Name == entry || type.FullName == entry)
            {
                return true;
            }
        }

        return false;
    }

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
