using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for ADR-030 R2 (026-guarded-tool-surface, FR-007a): every regular
/// expression constructed by the <c>search_files</c> implementation is built with
/// <see cref="System.Text.RegularExpressions.RegexOptions.NonBacktracking"/> and an explicit
/// match timeout — never the bare 1- or 2-argument <c>Regex</c> overloads, and never with the
/// non-backtracking bit unset. Non-backtracking makes catastrophic backtracking on
/// agent-supplied patterns structurally impossible rather than merely time-limited.
///
/// Scoped to <see cref="Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor"/> specifically —
/// where T020 places the search implementation — rather than the whole
/// <c>Grimoire.AgentRuntime.Guardrails</c> namespace: the sibling
/// <c>Coordination.SharedFileWriteGuard</c> type already constructs its own, unrelated
/// regular expressions (the ADR-017 log-heading/catalog-entry format checks) with
/// <c>RegexOptions.Compiled</c> and no timeout, on trusted, harness-authored patterns —
/// that pre-existing usage is out of this rule's scope by construction (Constitution
/// Principle II "Test what we own": this rule protects the boundary ADR-030 R2 draws around
/// agent-supplied search patterns, not every regex in the codebase).
///
/// At Phase 0 (026-guarded-tool-surface T002) this rule passes vacuously: no `search_files`
/// dispatch exists yet (that is Phase 3, T015/T020), so <c>GuardedToolExecutor</c> constructs
/// no regular expressions at all. Proven live by a Red/Green probe (recorded in the T002
/// commit message): a temporary scratch field constructing a plain
/// <c>new Regex(".*")</c> inside <c>GuardedToolExecutor</c> is added, confirmed to fail the
/// rule naming the violation, then removed and confirmed green again.
/// </summary>
public class SearchRegexBoundaryRuleTests
{
    private const string RegexCtorPrefix = "System.Text.RegularExpressions.Regex::.ctor";

    // RegexOptions.NonBacktracking (System.Text.RegularExpressions.RegexOptions), a plain
    // enum flag value, stable across target frameworks.
    private const int NonBacktrackingFlag = 1024;

    [Fact]
    public void GuardedToolExecutor_RegexConstruction_MustBeNonBacktrackingWithTimeout()
    {
        var assemblyPath = typeof(Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor).Assembly.Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        var scannedTypeName = typeof(Grimoire.AgentRuntime.Guardrails.GuardedToolExecutor).FullName;
        var violations = new List<string>();
        var constructionSitesFound = 0;

        foreach (var module in assembly.Modules)
        {
            foreach (var (type, topLevelName) in module.Types.SelectMany(t => WithTopLevelName(t, t.FullName)))
            {
                if (!string.Equals(topLevelName, scannedTypeName, StringComparison.Ordinal))
                    continue;

                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    var instructions = method.Body.Instructions;
                    for (var i = 0; i < instructions.Count; i++)
                    {
                        var instruction = instructions[i];
                        if (instruction.OpCode != OpCodes.Newobj)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                        if (!callSig.StartsWith(RegexCtorPrefix, StringComparison.Ordinal))
                            continue;

                        constructionSitesFound++;
                        var site = $"{type.FullName}.{method.Name}";

                        if (callee.Parameters.Count != 3)
                        {
                            violations.Add($"{site}: Regex constructed with {callee.Parameters.Count} argument(s) " +
                                "instead of the (pattern, options, timeout) 3-argument overload");
                            continue;
                        }

                        // Stack order for `new Regex(pattern, options, timeout)`: pattern is
                        // pushed first, then options, then timeout, then newobj. When options
                        // and timeout are each single-instruction pushes (a constant and a
                        // static-field read respectively — the shape this rule requires),
                        // the options push is exactly two instructions before newobj.
                        if (i < 2 || !TryDecodeLdcI4(instructions[i - 2], out var optionsValue))
                        {
                            violations.Add($"{site}: Regex options argument is not a simple constant load " +
                                "immediately preceding the timeout argument — cannot verify NonBacktracking");
                            continue;
                        }

                        if ((optionsValue & NonBacktrackingFlag) == 0)
                        {
                            violations.Add($"{site}: Regex constructed with options value {optionsValue}, " +
                                "missing RegexOptions.NonBacktracking");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-030 R2: every Regex constructed in GuardedToolExecutor must use the " +
            "(pattern, options, timeout) overload with RegexOptions.NonBacktracking set. Violations:\n" +
            string.Join("\n", violations));

        // Not asserted as a hard requirement (Phase 0 passes vacuously, before search exists)
        // — informational only, so a future reader of a failing run sees whether any
        // construction sites were found at all.
        _ = constructionSitesFound;
    }

    private static bool TryDecodeLdcI4(Instruction instruction, out int value)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_I4:
                value = (int)instruction.Operand;
                return true;
            case Code.Ldc_I4_S:
                value = (sbyte)instruction.Operand;
                return true;
            case Code.Ldc_I4_0: value = 0; return true;
            case Code.Ldc_I4_1: value = 1; return true;
            case Code.Ldc_I4_2: value = 2; return true;
            case Code.Ldc_I4_3: value = 3; return true;
            case Code.Ldc_I4_4: value = 4; return true;
            case Code.Ldc_I4_5: value = 5; return true;
            case Code.Ldc_I4_6: value = 6; return true;
            case Code.Ldc_I4_7: value = 7; return true;
            case Code.Ldc_I4_8: value = 8; return true;
            case Code.Ldc_I4_M1: value = -1; return true;
            default:
                value = 0;
                return false;
        }
    }

    private static IEnumerable<(TypeDefinition Type, string TopLevelName)> WithTopLevelName(
        TypeDefinition type, string topLevelName)
    {
        yield return (type, topLevelName);
        foreach (var nested in type.NestedTypes.SelectMany(n => WithTopLevelName(n, topLevelName)))
            yield return nested;
    }
}
