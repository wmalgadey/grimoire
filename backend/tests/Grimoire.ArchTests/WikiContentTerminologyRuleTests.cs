using System.Text.RegularExpressions;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural terminology rule for 022-align-wiki-structure (FR-019, SC-014). "Article" is the
/// project's canonical term for a unit of wiki content; "page" is retired — not only as a path
/// concept (that is <see cref="RetiredPagesWrapperPathRuleTests"/>'s job) but as a *term*, down
/// to metric names, task-artifact fields, and persisted record fields.
///
/// This rule is the deliberate inverse of 014-wiki-storage-restructure's tokenizer, whose
/// <c>IsPagesPathSegment</c> predicate was written specifically to NOT misreport identifiers
/// like <c>pages_touched</c> or <c>wiki.ingest.pages_touched_total</c> — narrow-scoped for a
/// path-retirement rule that had no business touching terminology. Feature 022 retires the term
/// itself, so this rule scans raw text (comments included, no masking) across the same surface
/// as the path rule, plus C# code identifiers, and forbids the term in any of its forms:
/// standalone word ("page", "Page"), PascalCase-embedded ("PagesTouched", "createdPages"), and
/// snake_case-embedded ("pages_touched", "_pages"). Deliberately a loose case-insensitive
/// substring match — the opposite of the old rule's narrow tokenizer-driven precision — because
/// every legitimate use of the retired term in this codebase is exactly the substring "page"/
/// "pages"; a full sweep of backend/src turned up no unrelated English word containing it as a
/// substring (e.g. "package" does not contain "page").
///
/// Exemptions: the same directory/build-output exclusions as
/// <see cref="RetiredPagesWrapperPathRuleTests"/> (frontend's SvelteKit +page.svelte routing in
/// particular), and the same historical-marker carve-out for accepted ADRs and specs
/// documenting the retirement (FR-010).
/// </summary>
public class WikiContentTerminologyRuleTests
{
    private static readonly string[] MarkdownAndJsonRoots = ["backend/src", "docs"];
    private static readonly string[] RepoRootMarkdownFiles = ["README.md", "CONTRIBUTING.md", "CLAUDE.md"];

    private static readonly Regex TermPattern = new(@"pages?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void RepositoryText_MustNotUseTheRetiredPageTerm()
    {
        var repositoryRoot = ArchScan.FindRepositoryRoot();
        var targets = CollectTargets(repositoryRoot);
        var violations = Scan(targets);

        Assert.True(
            violations.Count == 0,
            "022-align-wiki-structure (FR-019/SC-014): \"article\" is the canonical term for a " +
            $"unit of wiki content; \"page\" is retired. {violations.Count} violation(s):\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Pure, file-I/O-free scan over already-loaded targets — see
    /// <see cref="RetiredPagesWrapperPathRuleTests.Scan"/> for the same shape and rationale.
    /// </summary>
    internal static List<string> Scan(IEnumerable<ArchScan.ScanTarget> targets)
    {
        var violations = new List<string>();

        foreach (var target in targets)
        {
            foreach (Match match in TermPattern.Matches(target.Text))
            {
                var lineIndex = ArchScan.LineIndex(target.Text, match.Index);
                if (ArchScan.IsHistoricalRetirementContext(target.RelativePath, target.Text, lineIndex))
                    continue;

                violations.Add($"{target.RelativePath}:{lineIndex + 1} → \"{match.Value}\"");
            }
        }

        return violations;
    }

    private static IEnumerable<ArchScan.ScanTarget> CollectTargets(string repositoryRoot)
    {
        foreach (var target in ArchScan.EnumerateScanTargets(repositoryRoot, "backend/src", "*.cs"))
            yield return target;

        foreach (var root in MarkdownAndJsonRoots)
        {
            foreach (var target in ArchScan.EnumerateScanTargets(repositoryRoot, root, "*.md"))
                yield return target;
        }

        foreach (var target in ArchScan.EnumerateScanTargets(repositoryRoot, "backend/src", "*.json"))
            yield return target;

        foreach (var fileName in RepoRootMarkdownFiles)
        {
            var absolute = Path.Combine(repositoryRoot, fileName);
            if (File.Exists(absolute))
                yield return new ArchScan.ScanTarget(fileName, File.ReadAllText(absolute));
        }
    }

    // -------------------------------------------------------------------------
    // Doc↔fixture mirror (SC-005) — same document as RetiredPagesWrapperPathRuleTests; both
    // rule classes must agree with it and with each other.
    // -------------------------------------------------------------------------

    [Fact]
    public void ExemptionFixture_MustMirror_TheConventionDocument()
    {
        var documentPath = Path.Combine(ArchScan.FindRepositoryRoot(), "docs", "conventions", "wiki-content-root.md");
        Assert.True(File.Exists(documentPath), $"{documentPath} must exist.");
        var documentText = File.ReadAllText(documentPath);

        var sectionStart = documentText.IndexOf("## Exemption list", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, $"'{documentPath}' must contain an '## Exemption list' section.");
        var sectionEnd = documentText.IndexOf("\n## ", sectionStart + 1, StringComparison.Ordinal);
        var section = sectionEnd > 0 ? documentText[sectionStart..sectionEnd] : documentText[sectionStart..];

        var documented = Regex
            .Matches(section, @"^\| `([A-Za-z0-9_./-]+)` \|", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var fixture = ArchScan.ExemptedDirectorySegments.ToHashSet(StringComparer.Ordinal);
        fixture.Add("Grimoire.AgentEvals/Fixtures/recordings");

        var onlyInDocument = documented.Except(fixture).Order().ToList();
        var onlyInFixture = fixture.Except(documented).Order().ToList();

        Assert.True(
            onlyInDocument.Count == 0 && onlyInFixture.Count == 0,
            "022-align-wiki-structure (SC-005): the exemption list in docs/conventions/wiki-content-root.md " +
            "and the in-test fixture must mirror each other exactly. " +
            $"Only in document: [{string.Join(", ", onlyInDocument)}]; " +
            $"only in fixture: [{string.Join(", ", onlyInFixture)}]");
    }

    // -------------------------------------------------------------------------
    // Red/Green probes (Constitution Principle III).
    // -------------------------------------------------------------------------

    /// <summary>
    /// This is the exact tolerance the old rule's <c>IsPagesPathSegment</c> predicate was
    /// written to grant — its doc comment named <c>wiki.ingest.pages_touched_total</c> by
    /// example as deliberately not misreported. This rule inverts that: the identifier is
    /// exactly what must now be flagged.
    /// </summary>
    [Fact]
    public void Rule_DetectsAViolation_WhenOneIsIntroduced()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.IngestAgent/IngestAgentMetrics.cs",
            "Meter.CreateCounter<long>(\"wiki.ingest.pages_touched_total\", description: \"count\");");

        var violations = Scan([target]);

        Assert.True(violations.Count >= 1, "Expected the synthetic pages_touched_total metric name to be flagged.");
        Assert.Contains(violations, v => v.StartsWith(target.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_DetectsAViolation_InPascalCaseIdentifiers()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactDocument.cs",
            "public IReadOnlyList<string> PagesTouched { get; init; } = [];");

        var violations = Scan([target]);

        Assert.True(violations.Count >= 1, "Expected the synthetic PagesTouched identifier to be flagged.");
    }

    [Fact]
    public void Rule_DetectsAViolation_InCommentProse()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.Hub/Example.cs",
            "// records the pages this run touched\nvoid Example() { }");

        var violations = Scan([target]);

        Assert.True(violations.Count >= 1, "Expected the retired term inside a comment to be flagged — comments are in scope, unlike the path rule.");
    }

    [Fact]
    public void Rule_DoesNotFlag_AnAcceptedRecordDocumentingTheRetirement()
    {
        var text =
            "## Consequence\n" +
            "\n" +
            "(As of 014-wiki-storage-restructure/ADR-017: the `pages/` wrapper is retired — " +
            "wiki pages are now created directly under the content root.)\n";
        var target = new ArchScan.ScanTarget("docs/adr/ADR-016-example.md", text);

        var violations = Scan([target]);

        Assert.True(violations.Count == 0, $"Expected no violations for a historical record; got: {string.Join(", ", violations)}");
    }
}
