using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural boundary rule for 014-wiki-storage-restructure (research.md R1/R8): the
/// <c>pages/</c> wrapper concept is retired. <c>GrimoirePathResolver</c>,
/// <c>GrimoirePathOptions</c>, and <c>ResolvedGrimoirePaths</c> are the only files
/// permitted to own the <c>PagesDir</c> concept during the rename sweep (Phase 3/US1);
/// every other production source file must neither contain a "pages" path-segment
/// string literal (e.g. <c>"pages"</c>, <c>"pages/..."</c>, <c>"...pages"</c>, or the
/// <c>--pages-dir</c> CLI flag) nor reference a <c>PagesDir</c> symbol.
///
/// Unlike <see cref="RuntimePathsBoundaryRuleTests"/> and
/// <see cref="RetiredQueryRunsLocationRuleTests"/> (Mono.Cecil IL scans over compiled
/// assemblies), this rule scans the <c>backend/src/**/*.cs</c> source tree directly —
/// a plain text scan (with a small comment/string-literal-aware tokenizer, no new
/// package dependency), the mechanism needed to name violating *files* (not just IL
/// call sites) before the rename sweep exists. The tokenizer distinguishes real string
/// literals from comments and char literals so that unrelated identifiers sharing the
/// substring "pages" (e.g. the task-artifact field <c>pages_touched</c>, the metric
/// <c>wiki.ingest.pages_touched_total</c>, or prose like "wiki pages") are not
/// misreported.
///
/// RED today (T001): production files under <c>backend/src</c> still reference the
/// retired concept. T019 (Phase 3/US1) reruns this test to confirm GREEN once every
/// consumer is repointed at <c>ContentRoot</c> — the Green half of this Red/Green
/// probe (Constitution Principle III).
/// </summary>
public class PagesWrapperRetirementBoundaryRuleTests
{
    private static readonly Regex PagesDirSymbolPattern = new(@"\bPagesDir\b", RegexOptions.Compiled);

    private static readonly string[] AllowedRelativeFilePaths =
    [
        Path.Combine("Grimoire.Hub", "Runtime", "Paths", "GrimoirePathResolver.cs"),
        Path.Combine("Grimoire.Hub", "Runtime", "Paths", "GrimoirePathOptions.cs"),
        Path.Combine("Grimoire.Hub", "Runtime", "Paths", "ResolvedGrimoirePaths.cs"),
    ];

    [Fact]
    public void ProductionSourceFiles_MustNotReferenceTheRetiredPagesWrapperConcept()
    {
        var srcDir = FindBackendSrcDirectory();
        var allowedFullPaths = AllowedRelativeFilePaths
            .Select(relative => Path.GetFullPath(Path.Combine(srcDir, relative)))
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<string>();
        var violatingFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateProductionSourceFiles(srcDir))
        {
            if (allowedFullPaths.Contains(file))
                continue;

            var text = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(srcDir, file);
            var (maskedCode, literals) = Tokenize(text);

            foreach (var (index, content) in literals)
            {
                if (!IsPagesPathSegment(content))
                    continue;

                violations.Add($"{relativePath}:{LineNumber(text, index)} → string literal content \"{content}\"");
                violatingFiles.Add(relativePath);
            }

            foreach (Match match in PagesDirSymbolPattern.Matches(maskedCode))
            {
                violations.Add($"{relativePath}:{LineNumber(text, match.Index)} → PagesDir symbol reference");
                violatingFiles.Add(relativePath);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"014-wiki-storage-restructure (research.md R1/R8): the retired `pages/` wrapper " +
            $"concept must not be referenced outside GrimoirePathResolver.cs/GrimoirePathOptions.cs/" +
            $"ResolvedGrimoirePaths.cs. {violations.Count} violation(s) across {violatingFiles.Count} " +
            $"file(s):\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// A "pages" path segment: the literal content is exactly "pages", contains a
    /// "pages/" or "/pages" path boundary, or is the internal Hub↔agent-process
    /// "--pages-dir" CLI flag (research.md R1). Deliberately narrower than a bare
    /// substring match so unrelated identifiers like "pages_touched" (task-artifact
    /// field), "wiki.ingest.pages_touched_total" (metric name), or prose ("wiki
    /// pages") are not misreported — none of those contain a "/" adjacent to "pages",
    /// equal "pages" exactly, or contain "--pages-dir".
    /// </summary>
    private static bool IsPagesPathSegment(string literalContent) =>
        literalContent == "pages" ||
        literalContent.Contains("pages/", StringComparison.Ordinal) ||
        literalContent.Contains("/pages", StringComparison.Ordinal) ||
        literalContent.Contains("--pages-dir", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateProductionSourceFiles(string srcDir) =>
        Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcludedBuildOutputPath(f));

    private static bool IsExcludedBuildOutputPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj");
    }

    private static int LineNumber(string text, int index)
    {
        var line = 1;
        var limit = Math.Min(index, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private static string FindBackendSrcDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var srcCandidate = Path.Combine(directory.FullName, "backend", "src");
            var testsCandidate = Path.Combine(directory.FullName, "backend", "tests");
            if (Directory.Exists(srcCandidate) && Directory.Exists(testsCandidate))
                return srcCandidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "backend/src (with a sibling backend/tests) not found in any parent of " + AppContext.BaseDirectory);
    }

    // -------------------------------------------------------------------------
    // Minimal C# tokenizer: distinguishes comments, char literals, and string
    // literals (regular/verbatim/interpolated, including their combinations) from
    // real code, without pulling in a Roslyn dependency. Produces (a) a "masked"
    // copy of the source where every comment/char-literal/string-literal span is
    // blanked out (newlines preserved, for line-number accuracy) so symbol lookups
    // only match real code identifiers, and (b) the list of literal text chunks
    // found inside string literals (interpolation holes excluded — their content is
    // code, not literal text) for path-segment pattern matching.
    // -------------------------------------------------------------------------

    private static (string MaskedCode, List<(int Index, string Text)> Literals) Tokenize(string text)
    {
        var masked = text.ToCharArray();
        var literals = new List<(int, string)>();
        var n = text.Length;
        var i = 0;

        void Blank(int from, int to)
        {
            for (var k = from; k < to && k < n; k++)
            {
                if (masked[k] != '\n')
                    masked[k] = ' ';
            }
        }

        while (i < n)
        {
            var c = text[i];

            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                var start = i;
                while (i < n && text[i] != '\n') i++;
                Blank(start, i);
                continue;
            }

            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(i + 2, n);
                Blank(start, i);
                continue;
            }

            if (c == '\'')
            {
                var start = i;
                i++;
                if (i < n && text[i] == '\\')
                {
                    i += 2;
                }
                else if (i < n)
                {
                    i++;
                }

                if (i < n && text[i] == '\'') i++;
                Blank(start, i);
                continue;
            }

            var verbatim = false;
            var interpolated = false;
            var prefixLen = 0;

            if (c == '@' && i + 1 < n && text[i + 1] == '"')
            {
                verbatim = true;
                prefixLen = 1;
            }
            else if (c == '@' && i + 2 < n && text[i + 1] == '$' && text[i + 2] == '"')
            {
                verbatim = true;
                interpolated = true;
                prefixLen = 2;
            }
            else if (c == '$' && i + 1 < n && text[i + 1] == '@' && i + 2 < n && text[i + 2] == '"')
            {
                verbatim = true;
                interpolated = true;
                prefixLen = 2;
            }
            else if (c == '$' && i + 1 < n && text[i + 1] == '"')
            {
                interpolated = true;
                prefixLen = 1;
            }

            if (prefixLen > 0 || c == '"')
            {
                var start = i;
                i = ScanString(text, i + prefixLen, verbatim, interpolated, literals);
                Blank(start, i);
                continue;
            }

            i++;
        }

        return (new string(masked), literals);
    }

    /// <summary>
    /// Scans a string literal body starting at the opening quote index; returns the
    /// index just past the closing quote. Interpolation holes (<c>{...}</c>) are
    /// skipped opaquely (brace-depth tracked, not re-tokenized) — their content is
    /// code, not literal text, and this codebase's interpolations are simple
    /// expressions with no nested string literals containing unbalanced braces.
    /// </summary>
    private static int ScanString(
        string text, int quoteIndex, bool verbatim, bool interpolated, List<(int Index, string Text)> literals)
    {
        var n = text.Length;
        var i = quoteIndex + 1;
        var chunkStart = i;
        var sb = new StringBuilder();
        var depth = 0;

        void FlushChunk()
        {
            if (sb.Length > 0)
            {
                literals.Add((chunkStart, sb.ToString()));
                sb.Clear();
            }
        }

        while (i < n)
        {
            var c = text[i];

            if (depth > 0)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        chunkStart = i + 1;
                        i++;
                        continue;
                    }
                }

                i++;
                continue;
            }

            if (!verbatim && c == '\\' && i + 1 < n)
            {
                sb.Append(c).Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (verbatim && c == '"' && i + 1 < n && text[i + 1] == '"')
            {
                sb.Append('"');
                i += 2;
                continue;
            }

            if (c == '"')
            {
                i++;
                break;
            }

            if (interpolated && c == '{' && i + 1 < n && text[i + 1] == '{')
            {
                sb.Append('{');
                i += 2;
                continue;
            }

            if (interpolated && c == '}' && i + 1 < n && text[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            if (interpolated && c == '{')
            {
                FlushChunk();
                depth = 1;
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        FlushChunk();
        return i;
    }
}
