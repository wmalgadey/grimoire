using NetArchTest.Rules;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rules H1/H2 for ADR-023 (022-align-wiki-structure): operator-controlled
/// harness-surface read scope.
///
/// H1: <c>Grimoire.Domain</c> — and specifically <c>Grimoire.Domain.Guardrails</c>, home of
/// <c>SafetyPolicy</c> — must not reference a configuration or options type. The subtractive
/// read-scope narrowing takes plain strings; the boolean-grant-set-to-denied-subtree mapping
/// belongs in agent composition, not the Domain (Constitution Principle I: the Domain Core is
/// strictly dependency-free).
///
/// H2: the four reserved harness surface names, *as a set*, are declared as C# string literals
/// in exactly one production file, <c>HarnessSurfaces/ReservedHarnessSurfaces.cs</c> — mirroring
/// ADR-022's R2 tripwire idiom (one designated owner for a literal set, everyone else references
/// the owner). This deliberately does not forbid any individual reserved word from appearing
/// elsewhere as a string literal — "tasks", "findings", and friends are ordinary English words
/// with legitimate unrelated uses (e.g. an eval fixture's own <c>tasks</c> subdirectory path);
/// what H2 forbids is a *second* file re-declaring the whole four-name set, which is the actual
/// drift this rule exists to catch (someone hand-copying the reserved-surface list instead of
/// referencing <c>ReservedHarnessSurfaces.All</c>).
/// </summary>
public class HarnessSurfaceScopeRuleTests
{
    private const string OwnerFileName = "ReservedHarnessSurfaces.cs";

    private static readonly string[] ReservedSurfaceLiterals =
        ["tasks", "conversations", "findings", "remediation-tasks"];

    [Fact]
    public void Domain_Must_Not_Depend_On_ExtensionsOptions()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Options")
            .GetResult();

        Assert.True(result.IsSuccessful, "ADR-023 H1: Grimoire.Domain must not depend on Microsoft.Extensions.Options.");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_ExtensionsConfiguration()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Configuration")
            .GetResult();

        Assert.True(result.IsSuccessful, "ADR-023 H1: Grimoire.Domain must not depend on Microsoft.Extensions.Configuration.");
    }

    [Fact]
    public void DomainGuardrails_Must_Not_Depend_On_ExtensionsOptions()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .That()
            .ResideInNamespaceStartingWith("Grimoire.Domain.Guardrails")
            .Should()
            .NotHaveDependencyOn("Microsoft.Extensions.Options")
            .GetResult();

        Assert.True(result.IsSuccessful, "ADR-023 H1: Grimoire.Domain.Guardrails must not depend on Microsoft.Extensions.Options.");
    }

    [Fact]
    public void ReservedSurfaceNameSet_MustNotBeRedeclaredOutsideItsOwner()
    {
        var repositoryRoot = ArchScan.FindRepositoryRoot();
        var violations = ScanForStraySetRedeclarations(
            ArchScan.EnumerateScanTargets(repositoryRoot, "backend/src", "*.cs"));

        Assert.True(
            violations.Count == 0,
            "ADR-023 H2: the full reserved-harness-surface set (tasks, conversations, findings, " +
            $"remediation-tasks) must be declared together only in {OwnerFileName} — other files " +
            $"should reference ReservedHarnessSurfaces.All. {violations.Count} violation(s):\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Pure scan, file-I/O-free — see <see cref="RetiredPagesWrapperPathRuleTests.Scan"/> for
    /// the same shape. A file whose name is <see cref="OwnerFileName"/> is the declared owner
    /// and is skipped; every other production file whose C# string literals contain the
    /// complete four-name reserved set is flagged as a stray redeclaration.
    /// </summary>
    internal static List<string> ScanForStraySetRedeclarations(IEnumerable<ArchScan.ScanTarget> targets)
    {
        var violations = new List<string>();

        foreach (var target in targets)
        {
            if (Path.GetFileName(target.RelativePath) == OwnerFileName)
                continue;

            var (_, literals) = ArchScan.Tokenize(target.Text);
            var found = literals
                .Select(l => l.Text)
                .Where(text => ReservedSurfaceLiterals.Contains(text, StringComparer.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            if (found.Count == ReservedSurfaceLiterals.Length)
            {
                violations.Add($"{target.RelativePath} → redeclares the full reserved-surface set " +
                                $"(belongs only in {OwnerFileName})");
            }
        }

        return violations;
    }

    // -------------------------------------------------------------------------
    // Red/Green probes (Constitution Principle III).
    // -------------------------------------------------------------------------

    [Fact]
    public void H2_DetectsAViolation_WhenTheFullSetIsRedeclaredOutsideItsOwner()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.Hub/SomeOtherFile.cs",
            "var surfaces = new[] { \"tasks\", \"conversations\", \"findings\", \"remediation-tasks\" };");

        var violations = ScanForStraySetRedeclarations([target]);

        Assert.True(violations.Count >= 1, "Expected the stray full-set redeclaration to be flagged.");
    }

    [Fact]
    public void H2_DoesNotFlag_AnIncidentalSingleReservedWord()
    {
        // Legitimate: EvalWorkspace.cs builds a path into its fixture's own "tasks"
        // subdirectory — an unrelated use of an ordinary English word, not a redeclaration
        // of the reserved-surface set.
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs",
            "public string TasksDir => Path.Combine(WikiRoot, \"tasks\");");

        var violations = ScanForStraySetRedeclarations([target]);

        Assert.True(violations.Count == 0, $"Expected no violation for a single incidental reserved word; got: {string.Join(", ", violations)}");
    }

    [Fact]
    public void H2_DoesNotFlag_TheDeclaredOwnerFile()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.Hub/HarnessSurfaces/ReservedHarnessSurfaces.cs",
            "internal static readonly string[] All = [\"tasks\", \"conversations\", \"findings\", \"remediation-tasks\"];");

        var violations = ScanForStraySetRedeclarations([target]);

        Assert.True(violations.Count == 0, $"Expected no violations for the owner file; got: {string.Join(", ", violations)}");
    }
}
