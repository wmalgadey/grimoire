using Grimoire.IngestAgent.Guardrails;

namespace Grimoire.ArchTests;

public class AutonomousGuardrailBoundaryTests
{
    [Fact]
    public void Guardrails_Deny_Writes_Outside_Wiki_By_Default()
    {
        var policy = new GuardrailPolicy(
            Version: "1",
            DenyByDefault: true,
            WriteAllowPrefixes: ["wiki/", "wiki/tasks/"],
            ReadAllowPaths: [],
            Rules: []);

        var evaluator = new GuardrailEvaluator(policy);
        var decision = evaluator.Evaluate(GuardrailAction.Write, "backend/src/Grimoire.IngestAgent/Program.cs");

        Assert.False(decision.IsAllowed);
        Assert.Contains("Denied by default", decision.Reason);
    }

    [Fact]
    public void Guardrails_Detect_Known_Bypass_Probe_Path()
    {
        var policy = new GuardrailPolicy(
            Version: "1",
            DenyByDefault: true,
            WriteAllowPrefixes: ["wiki/"],
            ReadAllowPaths: [],
            Rules: []);

        var evaluator = new GuardrailEvaluator(policy);
        var decision = evaluator.Evaluate(GuardrailAction.Write, Probes.BadGuardrailBypassProbe.ForbiddenTargetPath);

        Assert.False(decision.IsAllowed);
    }
}
