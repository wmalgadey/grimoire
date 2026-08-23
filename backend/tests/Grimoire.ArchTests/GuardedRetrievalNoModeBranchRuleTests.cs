using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Two structural boundary rules for 026-guarded-tool-surface, grouped in one file because
/// both guard the same "one guarded layer, one policy, no mode branch" shape ADR-030 R1/R4
/// and ADR-031 R1 describe:
///
/// <list type="number">
/// <item><b>Guarded-only filesystem reach</b> (ADR-030 R1/R4, FR-014): the directory- and
/// file-enumeration APIs <c>search_files</c> and <c>batch</c>'s read-only dispatch need are
/// reachable only from <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> —
/// never from a separate helper type, which would be a second place the read policy would
/// need to be re-applied correctly. (Deletion's own API, <c>File.Delete</c>/
/// <c>Directory.Delete</c>, is covered on its own by
/// <see cref="WikiDeletionGuardedBoundaryRuleTests"/>.)</item>
/// <item><b>No run-mode policy branch</b> (ADR-031 R1, FR-022): none of the three coordinators
/// that launch a Lint run — <c>Grimoire.Hub.LintDispatch.LintRunCoordinator</c>,
/// <c>Grimoire.Hub.RemediationTasks.RemediationRunCoordinator</c>,
/// <c>Grimoire.Hub.RemediationTasks.RemediationMessageTurnCoordinator</c> — may select
/// between two different policy-path sources by run mode. All three pass the same
/// <c>_paths.Lint.PolicyPath</c> today; introducing a second, differently named
/// <c>*PolicyPath</c> property and branching on run mode to choose between them is exactly
/// the "two scopes selected by mode" shape ADR-031 rejected (Decision Outcome, option 3).
/// Checked by counting the distinct property-getter names ending in <c>PolicyPath</c> each
/// coordinator type invokes: today that set has exactly one member per type.</item>
/// </list>
///
/// At Phase 0 (026-guarded-tool-surface T003) rule 1 passes vacuously (no search/batch
/// dispatch exists yet — Phase 3/6) and rule 2 passes on the pre-existing single-scope shape.
/// Proven live by a Red/Green probe (recorded in the T003 commit message): a temporary
/// mode-conditional policy path is added to <c>RemediationRunCoordinator</c> (a second
/// <c>*PolicyPath</c>-suffixed property, selected by an <c>if</c> on run mode), confirmed to
/// fail rule 2 naming the violation, then reverted and confirmed green again.
/// </summary>
public class GuardedRetrievalNoModeBranchRuleTests
{
    // Directory/file enumeration APIs search_files and batch's read dispatch need. Narrower
    // than the sibling write-boundary rules' write-method lists on purpose — this rule is
    // about retrieval reach, not every filesystem API (Constitution Principle II "Test what
    // we own").
    // Deliberately excludes File.ReadAllText(Async)/ReadAllBytes(Async): those are already
    // legitimately called elsewhere in this assembly for unrelated reasons (PolicyLoader,
    // SystemPromptLoader, WriteJournal, SharedFileWriteGuard, the Replay adapter's
    // RecordingSchema) — including them here would flag pre-existing, correctly-placed code
    // as a boundary violation (Constitution Principle II "Test what we own": this rule
    // protects search/batch's retrieval reach, not every file-content read in the
    // assembly). Directory.GetFiles/GetDirectories are safe to add: verified (by direct
    // grep) that only GuardedToolExecutor — the one exempted type — calls them today.
    private static readonly string[] _retrievalEnumerationMethods =
    [
        "System.IO.Directory::EnumerateFiles",
        "System.IO.Directory::EnumerateFileSystemEntries",
        "System.IO.Directory::EnumerateDirectories",
        "System.IO.Directory::GetFiles",
        "System.IO.Directory::GetDirectories",
        "System.IO.Directory::GetFileSystemEntries",
        "System.IO.File::ReadLines",
        "System.IO.File::ReadAllLines",
        "System.IO.File::ReadAllLinesAsync",
    ];

    [Fact]
    public void GuardedRetrieval_FilesystemEnumerationAPIs_MustOnlyBeCalledFromGuardedToolExecutor()
    {
        var assemblyPath = typeof(Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var guardedExecutorName = typeof(Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor).FullName;

        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, topLevelName) in module.Types.SelectMany(t => WithTopLevelName(t, t.FullName)))
            {
                if (string.Equals(topLevelName, guardedExecutorName, StringComparison.Ordinal))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                        if (_retrievalEnumerationMethods.Any(w => callSig.StartsWith(w, StringComparison.Ordinal)))
                        {
                            violations.Add($"{type.FullName}.{method.Name} [{topLevelName}] → {callSig}");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-030 R1/R4: directory/file enumeration for search_files and batch must only be " +
            "reachable from GuardedToolExecutor. Violations:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void LintCoordinators_MustReferenceExactlyOnePolicyPathSource()
    {
        var hubAssemblyPath = typeof(Grimoire.Hub.HubMetrics).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(hubAssemblyPath);

        var coordinatorTypeNames = new[]
        {
            "Grimoire.Hub.LintDispatch.LintRunCoordinator",
            "Grimoire.Hub.RemediationTasks.RemediationRunCoordinator",
            "Grimoire.Hub.RemediationTasks.RemediationMessageTurnCoordinator",
        };

        var failures = new List<string>();
        var foundCoordinatorTypeNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in assembly.Modules)
        {
            foreach (var coordinatorTypeName in coordinatorTypeNames)
            {
                var matchingTypes = module.Types
                    .SelectMany(t => WithTopLevelName(t, t.FullName))
                    .Where(pair => string.Equals(pair.TopLevelName, coordinatorTypeName, StringComparison.Ordinal))
                    .Select(pair => pair.Type)
                    .ToList();

                if (matchingTypes.Count == 0)
                    continue; // Present in a different module of the same assembly — checked below.

                foundCoordinatorTypeNames.Add(coordinatorTypeName);

                var policyPathGetters = new HashSet<string>(StringComparer.Ordinal);

                foreach (var type in matchingTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                                continue;

                            if (instruction.Operand is not MethodReference callee)
                                continue;

                            if (callee.Name.EndsWith("PolicyPath", StringComparison.Ordinal))
                            {
                                policyPathGetters.Add($"{callee.DeclaringType.Name}.{callee.Name}");
                            }
                        }
                    }
                }

                if (policyPathGetters.Count > 1)
                {
                    failures.Add($"{coordinatorTypeName} references {policyPathGetters.Count} distinct " +
                        $"policy-path sources: {string.Join(", ", policyPathGetters)} — a run-mode branch " +
                        "over the write scope is exactly the split ADR-031 R1 rejected.");
                }
            }
        }

        // A rename, removal, or move of any of the three coordinators would otherwise let
        // this rule pass vacuously without checking the invariant it exists for.
        foreach (var expectedTypeName in coordinatorTypeNames)
        {
            Assert.True(
                foundCoordinatorTypeNames.Contains(expectedTypeName),
                $"ADR-031 R1: expected coordinator type '{expectedTypeName}' was not found in " +
                "Grimoire.Hub — this rule cannot check the no-mode-branch invariant on a type it cannot locate.");
        }

        Assert.True(
            failures.Count == 0,
            "ADR-031 R1: no coordinator may select between two policy-path sources by run mode. " +
            "Violations:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<(TypeDefinition Type, string TopLevelName)> WithTopLevelName(
        TypeDefinition type, string topLevelName)
    {
        yield return (type, topLevelName);
        foreach (var nested in type.NestedTypes.SelectMany(n => WithTopLevelName(n, topLevelName)))
            yield return nested;
    }
}
