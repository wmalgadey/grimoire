using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Guardrails;
using System.Diagnostics;
using System.Text.Json;
using System.Linq;

namespace Grimoire.IntegrationTests;

public class PathTraversalTests
{
    [Fact]
    public async Task ReadFile_Denies_TraversalAbsoluteAndSymlinkEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiPages = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiPages);

            var outsideFile = Path.Combine(root, "outside-read.txt");
            await File.WriteAllTextAsync(outsideFile, "outside");

            var symlinkPath = Path.Combine(wikiPages, "outside-link.md");
            File.CreateSymbolicLink(symlinkPath, outsideFile);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "tech") + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var dotDotResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "../outside-read.txt" }),
                turn: 1,
                CancellationToken.None);

            var absoluteResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = outsideFile }),
                turn: 2,
                CancellationToken.None);

            var symlinkResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/outside-link.md" }),
                turn: 3,
                CancellationToken.None);

            Assert.True(dotDotResult.IsError);
            Assert.True(absoluteResult.IsError);
            Assert.True(symlinkResult.IsError);

            Assert.Equal(3, executor.Denials.Count);
            Assert.All(executor.Denials, denial =>
                Assert.True(denial.Reason is "traversal" or "out_of_scope" or "no_rule"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_Denies_TraversalAbsoluteAndSymlinkEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiPages = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiPages);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var symlinkPath = Path.Combine(wikiPages, "outside-link.md");
            File.CreateSymbolicLink(symlinkPath, outsideFile);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(root, "wiki", "tech") + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var dotDotResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "../outside-write.txt", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            var absoluteResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = outsideFile, content = "hijack" }),
                turn: 2,
                CancellationToken.None);

            var symlinkResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/outside-link.md", content = "hijack" }),
                turn: 3,
                CancellationToken.None);

            Assert.True(dotDotResult.IsError);
            Assert.True(absoluteResult.IsError);
            Assert.True(symlinkResult.IsError);

            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));
            Assert.Equal(3, executor.Denials.Count);
            Assert.Empty(journal.JournaledPaths);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ── 027-host-stability (ADR-034 R3, research.md D1-D3): adversarial path variants ──
    // beyond plain ".." traversal and a single-hop symlink escape, already covered above.

    [Fact]
    public async Task WriteFile_ContainsOrDenies_PercentEncodedTraversalVariant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-percent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiTech);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            // "%2e%2e%2F" is never percent-decoded anywhere in the guarded-tool path — it
            // resolves to one literal, oddly-named filename, not a ".." traversal.
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/%2e%2e%2Foutside-write.txt", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            // Whatever the outcome, the real outside file is never touched.
            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));

            if (!result.IsError)
            {
                var literalTarget = Path.Combine(wikiTech, "%2e%2e%2Foutside-write.txt");
                Assert.True(File.Exists(literalTarget));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_ContainsOrDenies_UnicodeConfusableTraversalVariant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-unicode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiTech);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            // U+FF0F FULLWIDTH SOLIDUS is not a path separator to the OS or to .NET's Path
            // APIs — it is never normalized to "/", so this is one literal filename.
            var confusablePath = "wiki/tech/..／..／outside-write.txt";
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = confusablePath, content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));

            if (!result.IsError)
            {
                var literalTarget = Path.Combine(wikiTech, "..／..／outside-write.txt");
                Assert.True(File.Exists(literalTarget));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_Denies_EmbeddedNulByte_WithoutUnhandledException()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-nul-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiTech);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            // An embedded NUL followed by an out-of-root suffix: .NET's Path APIs throw
            // ArgumentException for this — the guarded-tool boundary must turn that into a
            // normal denial, never an unhandled exception out of the tool-call path.
            var maliciousPath = "wiki/tech/ok.md\0../../outside-write.txt";

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = maliciousPath, content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains(executor.Denials, d => d.Reason == "malformed_path");
            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_Denies_ChainedSymlinkThroughResolvedIntermediateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-chain-{Guid.NewGuid():N}");
        var outsideRoot = Path.Combine(Path.GetTempPath(), $"path-traversal-chain-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            var realInner = Path.Combine(wikiTech, "realInner");
            Directory.CreateDirectory(realInner);

            var outsideFile = Path.Combine(outsideRoot, "secret.md");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            // First hop ("dirLink") lands inside the allowed write scope — a naive
            // per-segment walk that stops resolving after its first reparse point would
            // wrongly treat the rest of the path as already-safe. The second hop
            // ("nestedLink"), nested inside that resolved target, is where the real
            // escape happens.
            var dirLink = Path.Combine(wikiTech, "dirLink");
            File.CreateSymbolicLink(dirLink, realInner);
            var nestedLink = Path.Combine(realInner, "nestedLink");
            File.CreateSymbolicLink(nestedLink, outsideFile);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/dirLink/nestedLink", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));
            Assert.Contains(executor.Denials, d => d.Reason is "traversal" or "out_of_scope" or "symlink_loop");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(outsideRoot))
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_ContainsOrDenies_PercentEncodedSegmentThroughSymlinkedIntermediateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-combined-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            var realInner = Path.Combine(wikiTech, "realInner");
            Directory.CreateDirectory(realInner);

            var outsideFile = Path.Combine(root, "outside-write.txt");
            await File.WriteAllTextAsync(outsideFile, "outside-before");

            var dirLink = Path.Combine(wikiTech, "dirLink");
            File.CreateSymbolicLink(dirLink, realInner);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/dirLink/%2e%2e%2Foutside-write.txt", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            Assert.Equal("outside-before", await File.ReadAllTextAsync(outsideFile));

            if (!result.IsError)
            {
                var literalTarget = Path.Combine(realInner, "%2e%2e%2Foutside-write.txt");
                Assert.True(File.Exists(literalTarget));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadFile_Denies_SymlinkChainExceedingHopCap_WithoutUnboundedRecursion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var wikiTech = Path.Combine(root, "wiki", "tech");
            Directory.CreateDirectory(wikiTech);

            var midRoot = Path.Combine(root, "mid");
            Directory.CreateDirectory(midRoot);

            // 41 one-hop directory symlinks (L0..L40), each in its own real directory —
            // each is discovered by a separate recursive resolution step (D2), rather than
            // being pre-collapsed by .NET's own single-call ResolveLinkTarget(returnFinalTarget:
            // true) chain-following, which only flattens symlinks chained directly at the OS
            // level within one segment. This is what actually exercises the 40-hop cap.
            const int hopCount = 41;
            var previousDir = wikiTech;
            for (var i = 0; i < hopCount; i++)
            {
                var targetDir = Path.Combine(midRoot, $"dir{i}");
                Directory.CreateDirectory(targetDir);
                File.CreateSymbolicLink(Path.Combine(previousDir, $"L{i}"), targetDir);
                previousDir = targetDir;
            }

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar, midRoot + Path.DirectorySeparatorChar],
                writePrefixes: [wikiTech + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(policy, journal, root);

            var relativePath = "wiki/tech/" + string.Join("/", Enumerable.Range(0, hopCount).Select(i => $"L{i}")) + "/secret.md";

            var result = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = relativePath }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains(executor.Denials, d => d.Reason == "symlink_loop");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFile_Denies_PostValidationIntermediateDirectorySymlinkSwap()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-write-race-{Guid.NewGuid():N}");
        var attackerDir = Path.Combine(Path.GetTempPath(), $"path-traversal-attacker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(attackerDir);

        try
        {
            var writeRoot = Path.Combine(root, "wiki", "tech");
            var subDir = Path.Combine(writeRoot, "sub");
            Directory.CreateDirectory(subDir);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writePrefixes: [writeRoot + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var writeLocksDir = Path.Combine(root, ".write-locks");
            Directory.CreateDirectory(writeLocksDir);

            // Real ADR-015 write coordination is exercised (not doubled) — the fake only
            // implements the sanctioned IToolCallInstrumentation seam, timing the swap to
            // the one real hook point that runs after policy validation but before the
            // mutating write: guardrails.acquire_write_lock's start.
            var swapInstrumentation = new SymlinkSwappingInstrumentation(subDir, attackerDir);
            var executor = new GuardedToolExecutor(
                policy, journal, root,
                instrumentation: swapInstrumentation,
                writeLocksDir: writeLocksDir);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/sub/file.md", content = "hijack" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains(executor.Denials, d => d.Reason == "revalidation_failed");
            Assert.False(File.Exists(Path.Combine(attackerDir, "file.md")));
            Assert.Empty(Directory.GetFiles(attackerDir));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(attackerDir))
            {
                Directory.Delete(attackerDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteFile_Denies_PostValidationIntermediateDirectorySymlinkSwap()
    {
        var root = Path.Combine(Path.GetTempPath(), $"path-traversal-delete-race-{Guid.NewGuid():N}");
        var attackerDir = Path.Combine(Path.GetTempPath(), $"path-traversal-attacker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(attackerDir);

        try
        {
            var deleteRoot = Path.Combine(root, "wiki", "tech");
            var subDir = Path.Combine(deleteRoot, "sub");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "keep.md"), "original");

            var decoyPath = Path.Combine(attackerDir, "keep.md");
            await File.WriteAllTextAsync(decoyPath, "attacker-owned");

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [Path.Combine(root, "wiki") + Path.DirectorySeparatorChar],
                writeRules: [],
                deleteRules: [new DeleteRule(deleteRoot + Path.DirectorySeparatorChar)]);

            var journal = new WriteJournal();

            // The delete-side hook point: RecordAllowed fires synchronously, immediately
            // after the policy check passes and before any mutating I/O — no lock
            // coordination needed to make this deterministic (delete_file has none).
            var swapInstrumentation = new SymlinkSwappingInstrumentation(subDir, attackerDir);
            var registry = new ToolRegistry([
                ToolRegistry.ListFilesDefinition,
                ToolRegistry.ReadFileDefinition,
                ToolRegistry.DeleteFileDefinition,
            ]);
            var executor = new GuardedToolExecutor(
                policy, journal, root, registry: registry, instrumentation: swapInstrumentation);

            var result = await executor.ExecuteAsync(
                ToolRegistry.DeleteFile,
                JsonSerializer.Serialize(new { path = "wiki/tech/sub/keep.md" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains(executor.Denials, d => d.Reason == "revalidation_failed");
            Assert.True(File.Exists(decoyPath));
            Assert.Equal("attacker-owned", await File.ReadAllTextAsync(decoyPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(attackerDir))
            {
                Directory.Delete(attackerDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Hand-rolled fake implementing the pre-existing <see cref="IToolCallInstrumentation"/>
    /// port (Constitution Principle II: a sanctioned test double for an existing port
    /// interface) — used only to pin, deterministically, the exact moment between policy
    /// validation and the mutating filesystem call at which a real
    /// <see cref="File.CreateSymbolicLink(string, string)"/> swap happens. No production
    /// code is doubled or bypassed: every other step (policy evaluation, write
    /// coordination, the actual write/delete) runs for real.
    /// </summary>
    private sealed class SymlinkSwappingInstrumentation : IToolCallInstrumentation
    {
        private readonly string _directoryToSwap;
        private readonly string _swapTarget;
        private bool _swapped;

        public SymlinkSwappingInstrumentation(string directoryToSwap, string swapTarget)
        {
            _directoryToSwap = directoryToSwap;
            _swapTarget = swapTarget;
        }

        public void RecordAllowed(string taskId, string tool, string target, int turn) => Swap();

        public void RecordDenied(string taskId, string tool, string requestedTarget, string canonicalTarget, string reason, int turn)
        {
        }

        public Activity? StartAcquireWriteLockActivity(string taskId, string path, int turn)
        {
            Swap();
            return null;
        }

        private void Swap()
        {
            if (_swapped)
            {
                return;
            }

            _swapped = true;
            Directory.Delete(_directoryToSwap, recursive: true);
            Directory.CreateSymbolicLink(_directoryToSwap, _swapTarget);
        }
    }
}
