using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Shared Mono.Cecil scan helpers for the ADR-013 rules (D1/D2), plus the text-scan helpers
/// shared by the 022-align-wiki-structure rules (<see cref="RetiredPagesWrapperPathRuleTests"/>,
/// <see cref="WikiContentTerminologyRuleTests"/>) and the repo-root discovery previously
/// duplicated in <c>PagesWrapperRetirementBoundaryRuleTests.FindBackendSrcDirectory</c> and
/// <c>AgentArtifactNamingRuleTests.FindConventionDocument</c>.
/// </summary>
internal static class ArchScan
{
    /// <summary>
    /// A file to scan: its path relative to the repository root, and its full text.
    /// Text is carried explicitly (not re-read from disk by scan logic) so the same scan
    /// function can be exercised against synthetic content in a Red/Green probe without
    /// touching the filesystem.
    /// </summary>
    internal readonly record struct ScanTarget(string RelativePath, string Text);

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until a directory containing
    /// <c>backend/src</c>, <c>backend/tests</c>, and <c>docs</c> is found. All three are
    /// required as the anchor (not just the two <c>backend/*</c> directories the earlier,
    /// narrower <c>FindBackendSrcDirectory</c> used) so that a partial checkout missing
    /// <c>docs/</c> fails loudly here rather than silently scanning zero files there — a
    /// vacuous pass is exactly what Constitution Principle III's Red/Green probe exists to
    /// prevent.
    /// </summary>
    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var srcCandidate = Path.Combine(directory.FullName, "backend", "src");
            var testsCandidate = Path.Combine(directory.FullName, "backend", "tests");
            var docsCandidate = Path.Combine(directory.FullName, "docs");
            if (Directory.Exists(srcCandidate) && Directory.Exists(testsCandidate) && Directory.Exists(docsCandidate))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "A directory containing backend/src, backend/tests, and docs was not found in any " +
            "parent of " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Directory-name segments excluded from every 022-align-wiki-structure scan, mirrored in
    /// docs/conventions/wiki-content-root.md's Exemption list — see the doc↔fixture mirror
    /// assertions in RetiredPagesWrapperPathRuleTests and WikiContentTerminologyRuleTests.
    /// </summary>
    internal static readonly string[] ExemptedDirectorySegments =
    [
        "bin", "obj", "node_modules", ".svelte-kit", ".git", ".grimoire", "frontend",
        "foundational", "ideas",
    ];

    /// <summary>
    /// docs/conventions/wiki-content-root.md is the canonical document defining that "page" is
    /// retired vocabulary (C7) — by nature it must use the word to say so, the same way a
    /// style guide banning a word must print the word. Exempted wholesale rather than
    /// line-by-line; every other live document is expected to comply outright.
    /// </summary>
    internal static readonly string SelfReferentialConventionDocument =
        Path.Combine("docs", "conventions", "wiki-content-root.md");


    /// <summary>
    /// Enumerates files matching <paramref name="searchPatterns"/> under <paramref name="root"/>
    /// (repository-relative directories, e.g. "backend/src", "docs"), excluding
    /// <see cref="ExemptedDirectorySegments"/> and the frozen eval-recording fixtures, and
    /// returns each as a <see cref="ScanTarget"/> with a repository-relative path.
    /// </summary>
    internal static IEnumerable<ScanTarget> EnumerateScanTargets(
        string repositoryRoot, string relativeDirectory, params string[] searchPatterns)
    {
        var absoluteDirectory = Path.Combine(repositoryRoot, relativeDirectory);
        if (!Directory.Exists(absoluteDirectory))
            yield break;

        foreach (var pattern in searchPatterns)
        {
            foreach (var file in Directory.EnumerateFiles(absoluteDirectory, pattern, SearchOption.AllDirectories))
            {
                if (IsExempted(repositoryRoot, file))
                    continue;

                var relativePath = Path.GetRelativePath(repositoryRoot, file);
                yield return new ScanTarget(relativePath, File.ReadAllText(file));
            }
        }
    }

    private static bool IsExempted(string repositoryRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(repositoryRoot, absolutePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Any(s => ExemptedDirectorySegments.Contains(s, StringComparer.Ordinal)))
            return true;

        if (relative.Equals(SelfReferentialConventionDocument, StringComparison.Ordinal))
            return true;

        // Frozen ADR-012 replay transcripts — recorded LLM output, not live instructions.
        if (relative.Replace(Path.DirectorySeparatorChar, '/')
                .Contains("Grimoire.AgentEvals/Fixtures/recordings/", StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>
    /// A line is a historical record of the retired pages/ wrapper's retirement — not a
    /// current-state description — under either of two rules, scoped to docs/adr/ and specs/
    /// (FR-010: accepted decision records and feature specs documenting the retirement as a
    /// past decision are not flagged).
    ///
    /// Rule 1 — retirement-marker proximity: a marker line (containing "retired", "superseded
    /// by", or "As of 014-wiki-storage-restructure") appears within
    /// <see cref="HistoricalContextWindowLines"/> lines AND that marker line itself also
    /// mentions "pages" or "wrapper" — i.e. the marker must be specifically about the pages/
    /// wrapper's retirement, not any other mechanism this codebase has separately retired
    /// (e.g. ADR-014's unrelated "per-turn artifact mechanism retired" — a bare "retired"
    /// mention with no "pages"/"wrapper" on the same line does not qualify). The window is
    /// generous (not ±1 line) because the established pattern puts the marker as a
    /// parenthetical AFTER the illustrative policy.json block it explains — e.g. ADR-015's
    /// `pages/` create-only example runs from its opening `read`/`write` rules through a
    /// closing "(As of 014-wiki-storage-restructure/ADR-017: ... the `pages/` wrapper is
    /// retired ...)" note and a further explanatory sentence, over a dozen lines that are still
    /// one contiguous historical illustration.
    ///
    /// Rule 2 — rejected Considered Option: the line falls between a "## Considered Options"
    /// heading and the next markdown heading of any level, bounded to that section only (not a
    /// flat window) so an unrelated later section is never swept in. A rejected alternative
    /// documents a road not taken at decision time, not a current-state claim, and rewriting it
    /// retroactively would misrepresent what was actually considered (research.md R11:
    /// ADR-006:43, ADR-016:63).
    /// </summary>
    private const int HistoricalContextWindowLines = 20;

    private static readonly Regex MarkdownHeadingPattern = new(@"^#{1,6}\s", RegexOptions.Compiled);

    internal static bool IsHistoricalRetirementContext(string relativePath, string text, int lineIndex)
    {
        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!normalized.StartsWith("docs/adr/", StringComparison.Ordinal) &&
            !normalized.StartsWith("specs/", StringComparison.Ordinal))
            return false;

        var lines = text.Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return false;

        // Rule 1: a nearby line that is BOTH a retirement marker AND specifically about the
        // pages/ wrapper.
        var from = Math.Max(0, lineIndex - HistoricalContextWindowLines);
        var to = Math.Min(lines.Length - 1, lineIndex + HistoricalContextWindowLines);
        for (var i = from; i <= to; i++)
        {
            var line = lines[i];
            var isMarker =
                line.Contains("retired", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("superseded by", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("As of 014-wiki-storage-restructure", StringComparison.Ordinal);
            var isAboutPagesWrapper =
                line.Contains("pages", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("wrapper", StringComparison.OrdinalIgnoreCase);

            if (isMarker && isAboutPagesWrapper)
                return true;
        }

        // Rule 2: inside a "## Considered Options" section (heading to next heading, any level).
        for (var i = 0; i <= lineIndex; i++)
        {
            if (!lines[i].Contains("## Considered Options", StringComparison.Ordinal))
                continue;

            var sectionEnd = lines.Length;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (MarkdownHeadingPattern.IsMatch(lines[j]))
                {
                    sectionEnd = j;
                    break;
                }
            }

            if (lineIndex > i && lineIndex < sectionEnd)
                return true;
        }

        return false;
    }

    internal static int LineIndex(string text, int charIndex)
    {
        var line = 0;
        var limit = Math.Min(charIndex, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    // -------------------------------------------------------------------------
    // Minimal C# tokenizer, ported unchanged from 014-wiki-storage-restructure's
    // PagesWrapperRetirementBoundaryRuleTests and shared by every 022-align-wiki-structure rule
    // that needs to isolate real C# string literals from comments/code (RetiredPagesWrapperPathRuleTests,
    // HarnessSurfaceScopeRuleTests' H2). Distinguishes comments, char literals, and string
    // literals (regular/verbatim/interpolated, including their combinations) from real code
    // without pulling in a Roslyn dependency. Produces (a) a "masked" copy of the source where
    // every comment/char-literal/string-literal span is blanked out (newlines preserved, for
    // line-number accuracy) so symbol lookups only match real code identifiers, and (b) the
    // list of literal text chunks found inside string literals (interpolation holes excluded —
    // their content is code, not literal text).
    // -------------------------------------------------------------------------

    internal static (string MaskedCode, List<(int Index, string Text)> Literals) Tokenize(string text)
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

    internal sealed record CallSite(
        string TopLevelTypeFullName,
        string EffectiveNamespace,
        string Description);

    /// <summary>
    /// Agent host assemblies, discovered by naming pattern (Grimoire.*Agent) from the
    /// test output directory so any future agent host (e.g. Grimoire.LintAgent) is
    /// covered the moment it is referenced by the test projects — without editing this
    /// rule.
    /// </summary>
    internal static IEnumerable<string> AgentHostAssemblyPaths()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var paths = Directory.GetFiles(baseDirectory, "Grimoire.*Agent.dll").OrderBy(p => p, StringComparer.Ordinal).ToList();

        // The two known hosts must be present — an empty scan would pass vacuously.
        Assert.Contains(paths, p => Path.GetFileName(p) == "Grimoire.IngestAgent.dll");
        Assert.Contains(paths, p => Path.GetFileName(p) == "Grimoire.QueryAgent.dll");

        return paths;
    }

    /// <summary>
    /// All call/newobj sites in the assembly whose callee matches one of the
    /// "DeclaringTypeFullName::MethodName" prefixes.
    /// </summary>
    internal static IEnumerable<CallSite> FindCalls(AssemblyDefinition assembly, string[] calleePrefixes)
        => FindSites(assembly, (callee, _) =>
            calleePrefixes.Any(p => $"{callee.DeclaringType.FullName}::{callee.Name}".StartsWith(p, StringComparison.Ordinal)));

    /// <summary>
    /// All constructor references (newobj, or any direct .ctor call) in the assembly
    /// whose constructed type is one of the given full names.
    /// </summary>
    internal static IEnumerable<CallSite> FindConstructions(AssemblyDefinition assembly, string[] constructedTypeFullNames)
        => FindSites(assembly, (callee, _) =>
            callee.Name == ".ctor" &&
            constructedTypeFullNames.Contains(callee.DeclaringType.GetElementType().FullName, StringComparer.Ordinal));

    private static IEnumerable<CallSite> FindSites(
        AssemblyDefinition assembly,
        Func<MethodReference, Instruction, bool> matches)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var (type, topLevel, effectiveNamespace) in module.Types.SelectMany(t => WithTopLevel(t, t, t.Namespace)))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call &&
                            instruction.OpCode != OpCodes.Callvirt &&
                            instruction.OpCode != OpCodes.Newobj)
                            continue;

                        if (instruction.Operand is not MethodReference callee)
                            continue;

                        if (matches(callee, instruction))
                        {
                            yield return new CallSite(
                                topLevel.FullName,
                                effectiveNamespace,
                                $"{type.FullName}.{method.Name} [{effectiveNamespace}] → {callee.DeclaringType.FullName}::{callee.Name}");
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<(TypeDefinition Type, TypeDefinition TopLevel, string EffectiveNamespace)> WithTopLevel(
        TypeDefinition type, TypeDefinition topLevel, string parentNamespace)
    {
        // Nested types (async state machines, closures) inherit the top-level type's
        // namespace and identity for baseline matching.
        var ns = string.IsNullOrEmpty(type.Namespace) ? parentNamespace : type.Namespace;
        yield return (type, topLevel, ns);
        foreach (var nested in type.NestedTypes.SelectMany(n => WithTopLevel(n, topLevel, ns)))
            yield return nested;
    }
}
