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
    // Every Regex entry point that can construct/compile a pattern: the constructor and the
    // static convenience methods, each of which has a (..., RegexOptions, TimeSpan) overload
    // that must be the one used — matches Copilot's finding that scanning newobj alone would
    // miss e.g. a bare Regex.IsMatch(input, pattern) call. Expected parameter count is the
    // bounded overload's own count: 3 for the constructor (pattern, options, timeout), 4 for
    // the static methods (an extra leading "input"/"count" argument).
    private static readonly Dictionary<string, int> _regexEntryPoints = new(StringComparer.Ordinal)
    {
        ["System.Text.RegularExpressions.Regex::.ctor"] = 3,
        ["System.Text.RegularExpressions.Regex::IsMatch"] = 4,
        ["System.Text.RegularExpressions.Regex::Match"] = 4,
        ["System.Text.RegularExpressions.Regex::Matches"] = 4,
        // Replace's bounded overload carries an extra "replacement" string between pattern
        // and options: (input, pattern, replacement, options, timeout).
        ["System.Text.RegularExpressions.Regex::Replace"] = 5,
        ["System.Text.RegularExpressions.Regex::Split"] = 4,
    };

    private const string InfiniteMatchTimeoutGetter = "System.Text.RegularExpressions.Regex::get_InfiniteMatchTimeout";

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
                        if (instruction.OpCode != OpCodes.Newobj && instruction.OpCode != OpCodes.Call)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        var callSig = $"{callee.DeclaringType.FullName}::{callee.Name}";
                        if (!_regexEntryPoints.TryGetValue(callSig, out var expectedParameterCount))
                            continue;

                        constructionSitesFound++;
                        var site = $"{type.FullName}.{method.Name}";

                        if (callee.Parameters.Count != expectedParameterCount)
                        {
                            violations.Add($"{site}: {callSig} called with {callee.Parameters.Count} argument(s) " +
                                $"instead of its bounded {expectedParameterCount}-argument (..., options, timeout) overload");
                            continue;
                        }

                        // Stack order puts every argument in declaration order, options
                        // second-to-last and timeout last, regardless of how many arguments
                        // precede them. The timeout argument is not always a single
                        // instruction — e.g. a field load compiles to `ldarg.0; ldfld` — so
                        // its boundary is found structurally (via ECMA-335 stack-behaviour
                        // push/pop counts per instruction) rather than assumed to be exactly
                        // one instruction before the call.
                        var timeoutArgStart = FindArgumentStart(instructions, i - 1);
                        if (timeoutArgStart <= 0)
                        {
                            violations.Add($"{site}: {callSig}'s timeout argument could not be resolved " +
                                "structurally — cannot verify NonBacktracking");
                            continue;
                        }

                        var optionsArgIndex = timeoutArgStart - 1;
                        var optionsArgStart = FindArgumentStart(instructions, optionsArgIndex);
                        if (optionsArgStart != optionsArgIndex || !TryDecodeLdcI4(instructions[optionsArgIndex], out var optionsValue))
                        {
                            violations.Add($"{site}: {callSig}'s options argument is not a simple constant load " +
                                "— cannot verify NonBacktracking");
                            continue;
                        }

                        if ((optionsValue & NonBacktrackingFlag) == 0)
                        {
                            violations.Add($"{site}: {callSig} called with options value {optionsValue}, " +
                                "missing RegexOptions.NonBacktracking");
                        }

                        for (var timeoutIdx = timeoutArgStart; timeoutIdx <= i - 1; timeoutIdx++)
                        {
                            if (instructions[timeoutIdx].Operand is MethodReference timeoutCallee &&
                                string.Equals($"{timeoutCallee.DeclaringType.FullName}::{timeoutCallee.Name}", InfiniteMatchTimeoutGetter, StringComparison.Ordinal))
                            {
                                violations.Add($"{site}: {callSig} called with Regex.InfiniteMatchTimeout — " +
                                    "removes the finite backstop ADR-030 R2/R5 requires");
                                break;
                            }
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

    /// <summary>
    /// Finds the first instruction of the argument expression whose last instruction is
    /// <paramref name="lastInstructionIndex"/>, using only ECMA-335 stack-behaviour push/pop
    /// counts — no assumption about how many IL instructions any one argument compiles to.
    /// Relies on a structural property of verifiable CIL: evaluating one argument expression
    /// nets exactly one value pushed relative to the stack depth at its own start, and never
    /// dips below that starting depth along the way (an argument's own instructions cannot
    /// consume values that were pushed for a prior argument). So scanning backward from the
    /// argument's last instruction, tracking the relative depth `relDepth` (1 at the end,
    /// working back to 0), the first point where `relDepth` reaches exactly 0 is the
    /// argument's first instruction.
    /// </summary>
    private static int FindArgumentStart(IList<Instruction> instructions, int lastInstructionIndex)
    {
        var relDepth = 1;
        for (var index = lastInstructionIndex; index >= 0; index--)
        {
            var netEffect = GetPushCount(instructions[index]) - GetPopCount(instructions[index]);
            relDepth -= netEffect;
            if (relDepth == 0)
                return index;
        }

        return -1;
    }

    private static int GetPushCount(Instruction instruction)
    {
        switch (instruction.OpCode.StackBehaviourPush)
        {
            case StackBehaviour.Push0:
                return 0;
            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref:
                return 1;
            case StackBehaviour.Push1_push1:
                return 2;
            case StackBehaviour.Varpush:
                // call/callvirt: pushes 1 iff the callee has a non-void return (newobj is
                // its own Push1 case above — it always pushes exactly the constructed object).
                return instruction.Operand is MethodReference { ReturnType.MetadataType: not MetadataType.Void }
                    ? 1
                    : 0;
            default:
                return 0;
        }
    }

    private static int GetPopCount(Instruction instruction)
    {
        switch (instruction.OpCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0:
                return 0;
            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                return 1;
            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                return 2;
            case StackBehaviour.Popi_popi_popi:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
            case StackBehaviour.Popref_popi_popref:
                return 3;
            case StackBehaviour.Varpop:
                return instruction.Operand is MethodReference method
                    ? method.Parameters.Count + (instruction.OpCode.Code != Code.Newobj && method.HasThis ? 1 : 0)
                    : 0;
            default:
                return 0;
        }
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
