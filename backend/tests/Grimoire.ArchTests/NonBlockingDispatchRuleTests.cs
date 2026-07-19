using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-008: the Hub's dispatch path must not synchronously
/// wait on agent process exit to derive a run outcome. Run outcome arrives via Agent Run
/// Events (or the liveness window). The only type permitted to touch
/// <c>Process.WaitForExit*</c> on the agent-run dispatch path is the process lifecycle
/// owner <c>AgentProcessHost</c> (ADR-010 <c>Grimoire.Hub.AgentDispatch.Adapters.AgentProcess</c>),
/// which waits solely for cleanup after termination and for the manual CLI run-to-exit
/// path (ADR-008 exit-code note). <c>MarkItDownConverter</c> (ADR-010
/// <c>Grimoire.Hub.IngestSubmission.Adapters.MarkItDown</c>) is also allowed: it waits on
/// its own owned document-conversion subprocess, not an agent run.
/// </summary>
public class NonBlockingDispatchRuleTests
{
    // The dispatch path (ADR-008): everything that starts or schedules agent runs.
    // Other Hub child processes (git in Program, markitdown in its ADR-010 adapter
    // namespace) are not agent runs and may legitimately be awaited.
    private static readonly string[] _dispatchPathNamespaces =
    [
        "Grimoire.Hub.AgentDispatch",
        "Grimoire.Hub.Submission",
        "Grimoire.Hub.IngestSubmission",
    ];

    private static readonly HashSet<string> _allowedOutermostTypes =
    [
        "Grimoire.Hub.AgentDispatch.Adapters.AgentProcess.AgentProcessHost",
        "Grimoire.Hub.IngestSubmission.Adapters.MarkItDown.MarkItDownConverter",
    ];

    private static readonly string[] _blockingWaitMethods =
    [
        "System.Diagnostics.Process::WaitForExit",
        "System.Diagnostics.Process::WaitForExitAsync",
    ];

    [Fact]
    public void Hub_MustNotWaitOnAgentProcessExit_OutsideAgentProcessHost()
    {
        var assemblyPath = typeof(Grimoire.Hub.HubMetrics).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var violations = new List<string>();

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, outermost) in module.Types.SelectMany(t => GetAllTypesWithOutermost(t, t.FullName)))
            {
                if (!_dispatchPathNamespaces.Any(ns => outermost.StartsWith(ns + ".", StringComparison.Ordinal)))
                    continue;

                // Async/iterator state machines compile to nested types; attribute the call
                // to the outermost declaring type so the allowlist covers them.
                if (_allowedOutermostTypes.Contains(outermost))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call &&
                            instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                        if (_blockingWaitMethods.Any(w => callSig.StartsWith(w, StringComparison.Ordinal)))
                        {
                            violations.Add($"{type.FullName}.{method.Name} → {callSig}");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-008: run outcome must come from Agent Run Events or the liveness window, " +
            "never from waiting on process exit. Process.WaitForExit* is only allowed inside " +
            "Grimoire.Hub.AgentDispatch.AgentProcessHost. Violations:\n" +
            string.Join("\n", violations));
    }

    private static IEnumerable<(TypeDefinition Type, string OutermostFullName)> GetAllTypesWithOutermost(
        TypeDefinition type, string outermostFullName)
    {
        yield return (type, outermostFullName);
        foreach (var nested in type.NestedTypes.SelectMany(n => GetAllTypesWithOutermost(n, outermostFullName)))
            yield return nested;
    }
}
