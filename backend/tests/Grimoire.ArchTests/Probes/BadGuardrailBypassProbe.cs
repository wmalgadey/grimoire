namespace Grimoire.ArchTests.Probes;

public static class BadGuardrailBypassProbe
{
    // Deliberately forbidden target used to prove guardrail deny behavior.
    public const string ForbiddenTargetPath = "backend/src/Grimoire.IngestAgent/Program.cs";
}
