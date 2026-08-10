using System.Text.RegularExpressions;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for 022-align-wiki-structure (research.md R8), replacing
/// 014-wiki-storage-restructure's <c>PagesWrapperRetirementBoundaryRuleTests</c>. The retired
/// <c>pages/</c> wrapper concept is retired everywhere — the earlier rule scanned
/// <c>backend/src/**/*.cs</c> only, which is exactly why the agent instruction files still
/// navigated it and the query agent reported a populated wiki as empty.
///
/// This rule scans <c>backend/src/**/*.cs</c>, every agent's <c>Instructions/**/*.md</c> and
/// <c>*.json</c>, <c>docs/**/*.md</c>, and repo-root <c>README.md</c>/<c>CONTRIBUTING.md</c>/
/// <c>CLAUDE.md</c> for a "pages" path-segment string literal or prose token: exactly
/// <c>"pages"</c>, a <c>pages/</c> or <c>/pages</c> boundary, or the retired <c>--pages-dir</c>
/// CLI flag. C# files are tokenized (comment/string-literal-aware, see
/// <see cref="ArchScan.Tokenize"/>) so unrelated identifiers sharing the substring "pages" are
/// not misreported by this rule; that job now belongs to
/// <see cref="WikiContentTerminologyRuleTests"/>, which deliberately does the opposite. Markdown
/// and JSON files are scanned as plain text — there is no meaningful "string literal" concept
/// to isolate there, and the retired path token is unambiguous in prose.
///
/// Two exemptions, both required by FR-010/SC-004: accepted decision records and feature specs
/// that document the retirement as a past decision must pass unmodified (see
/// <see cref="ArchScan.IsHistoricalRetirementContext"/>), and the small set of directories in
/// <see cref="ArchScan.ExemptedDirectorySegments"/> (build output, frozen eval recordings,
/// frontend framework files) are never scanned at all.
/// </summary>
public class RetiredPagesWrapperPathRuleTests
{
    private static readonly string[] MarkdownAndJsonRoots = ["backend/src", "docs"];
    private static readonly string[] RepoRootMarkdownFiles = ["README.md", "CONTRIBUTING.md", "CLAUDE.md"];

    [Fact]
    public void RepositoryText_MustNotReferenceTheRetiredPagesWrapperConcept()
    {
        var repositoryRoot = ArchScan.FindRepositoryRoot();
        var targets = CollectTargets(repositoryRoot);
        var violations = Scan(targets);

        Assert.True(
            violations.Count == 0,
            "022-align-wiki-structure (research.md R8): the retired `pages/` wrapper concept " +
            $"must not be referenced. {violations.Count} violation(s):\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// The scan itself, pure and file-I/O-free — takes already-loaded <see cref="ArchScan.ScanTarget"/>
    /// values and returns violation strings. Extracted so the Red/Green probes below can feed
    /// synthetic content without touching the filesystem or mutating the real repository.
    /// </summary>
    internal static List<string> Scan(IEnumerable<ArchScan.ScanTarget> targets)
    {
        var violations = new List<string>();

        foreach (var target in targets)
        {
            var isCSharp = target.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

            if (isCSharp)
            {
                var (maskedCode, literals) = ArchScan.Tokenize(target.Text);
                _ = maskedCode; // PagesDir symbol scanning is retired — see 014's now-vacuous check; nothing else in the mask matters here.

                foreach (var (index, content) in literals)
                {
                    if (!IsPagesPathSegment(content))
                        continue;

                    var lineIndex = ArchScan.LineIndex(target.Text, index);
                    if (ArchScan.IsHistoricalRetirementContext(target.RelativePath, target.Text, lineIndex))
                        continue;

                    violations.Add($"{target.RelativePath}:{lineIndex + 1} → string literal content \"{content}\"");
                }
            }
            else
            {
                foreach (Match match in PagesTokenPattern.Matches(target.Text))
                {
                    var lineIndex = ArchScan.LineIndex(target.Text, match.Index);
                    if (ArchScan.IsHistoricalRetirementContext(target.RelativePath, target.Text, lineIndex))
                        continue;

                    violations.Add($"{target.RelativePath}:{lineIndex + 1} → \"{match.Value}\"");
                }
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

    /// <summary>
    /// Plain-text match for markdown/JSON: a `pages` path boundary or the retired CLI flag.
    /// Narrower than a bare substring match for the same reason <see cref="IsPagesPathSegment"/>
    /// is — "wiki pages" prose and "pages_touched"-style identifiers must not trip this rule.
    /// </summary>
    private static readonly Regex PagesTokenPattern = new(
        @"(?<![\w/])pages/|/pages(?![\w/])|--pages-dir", RegexOptions.Compiled);

    /// <summary>
    /// A "pages" path segment inside a C# string literal: the literal content is exactly
    /// "pages", contains a "pages/" or "/pages" path boundary, or is the retired "--pages-dir"
    /// CLI flag. Ported unchanged from the 014 rule (research.md R8) — deliberately narrower
    /// than a bare substring match so identifiers like "pages_touched" are not misreported here;
    /// WikiContentTerminologyRuleTests forbids those instead.
    /// </summary>
    private static bool IsPagesPathSegment(string literalContent) =>
        literalContent == "pages" ||
        literalContent.Contains("pages/", StringComparison.Ordinal) ||
        literalContent.Contains("/pages", StringComparison.Ordinal) ||
        literalContent.Contains("--pages-dir", StringComparison.Ordinal);

    // -------------------------------------------------------------------------
    // Doc↔fixture mirror (SC-005): the exemption list lives in
    // docs/conventions/wiki-content-root.md with a justification per entry; this test parses
    // it and fails on any drift between document and in-test fixture, in either direction.
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
        // "Grimoire.AgentEvals/Fixtures/recordings" is documented as one composite entry
        // (IsExempted checks it as a path substring, not a directory-segment set membership).
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
    // Red/Green probes (Constitution Principle III): permanent facts feeding synthetic scan
    // targets, proving the rule detects a violation and correctly exempts a historical record —
    // without mutating the real repository.
    // -------------------------------------------------------------------------

    [Fact]
    public void Rule_DetectsAViolation_WhenOneIsIntroduced()
    {
        var target = new ArchScan.ScanTarget(
            "backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md",
            "Use `list_files(\"pages/\")` to enumerate every article.");

        var violations = Scan([target]);

        Assert.True(violations.Count >= 1, "Expected the synthetic pages/ reference to be flagged.");
        Assert.Contains(violations, v => v.StartsWith(target.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Rule_DoesNotFlag_AnAcceptedRecordDocumentingTheRetirement()
    {
        var text =
            "## Decision\n" +
            "\n" +
            "(As of 014-wiki-storage-restructure/ADR-017: the `pages/` wrapper is retired — " +
            "pathPrefix values become `.`.)\n";
        var target = new ArchScan.ScanTarget("docs/adr/ADR-015-example.md", text);

        var violations = Scan([target]);

        Assert.True(violations.Count == 0, $"Expected no violations for a historical record; got: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Rule_DetectsAViolation_InPlainTextFiles()
    {
        var target = new ArchScan.ScanTarget(
            "docs/conventions/some-doc.md",
            "Articles live under pages/<category>/<slug>.md today.");

        var violations = Scan([target]);

        Assert.True(violations.Count >= 1, "Expected the synthetic pages/ reference in prose to be flagged.");
    }
}
