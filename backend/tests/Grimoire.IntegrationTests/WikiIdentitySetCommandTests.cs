using Grimoire.Hub.Cli;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040-T045 (029-shared-foundation-prompt, US2, ADR-053, contracts/wiki-identity-cli.md):
/// <see cref="WikiIdentityCommand"/>'s <c>set</c> action against a real temp data root and
/// the real <c>ResolveEffectiveFoundationPrompt</c> resolution path — no test double beyond
/// the harness's direct-construction idiom (<see cref="WikiIdentityCommandTestHarness"/>,
/// mirroring <c>HubCliCommandTests</c>).
/// </summary>
public class WikiIdentitySetCommandTests
{
    [Fact]
    public async Task Default_CreatesNoFile_AndResolutionStillReportsDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-default-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            var (exitCode, stdout) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, @default: true);

            Assert.Equal((int)CliExitCode.Success, exitCode);
            Assert.Contains("Nothing was written", stdout, StringComparison.Ordinal);
            Assert.False(File.Exists(paths.InstanceFoundationPromptPath));

            var resolved = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);
            Assert.Equal("default", resolved.Source);
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
    public async Task Default_WithAnInstanceDocumentAlreadyInPlace_ReportsItInsteadOfAssertingTheDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-default-instance-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var instanceContent = "# A Specialised Wiki\nThis instance's current identity.\n";

        try
        {
            await File.WriteAllTextAsync(paths.InstanceFoundationPromptPath, instanceContent);

            var (exitCode, stdout) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, @default: true);

            Assert.Equal((int)CliExitCode.Success, exitCode);
            Assert.Contains("already in effect", stdout, StringComparison.Ordinal);
            Assert.Contains("A Specialised Wiki", stdout, StringComparison.Ordinal);
            Assert.Contains("Nothing was written", stdout, StringComparison.Ordinal);
            Assert.Equal(instanceContent, await File.ReadAllTextAsync(paths.InstanceFoundationPromptPath));

            var resolved = paths.ResolveEffectiveFoundationPrompt(paths.Ingest);
            Assert.Equal("instance", resolved.Source);
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
    public async Task Specialised_PrintsBriefContainingTheDescriptionVerbatim_AndWritesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-specialised-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            const string description = "A wiki that tracks nothing but home-lab Kubernetes runbooks.";
            var (exitCode, stdout) = await WikiIdentityCommandTestHarness.RunSetAsync(
                paths, specialised: true, description: description);

            Assert.Equal((int)CliExitCode.Success, exitCode);
            Assert.Contains(description, stdout, StringComparison.Ordinal);
            Assert.Contains("wiki-identity set --from-file", stdout, StringComparison.Ordinal);
            Assert.False(File.Exists(paths.InstanceFoundationPromptPath));
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
    public async Task FromFile_PersistsByteForByte_AndTheNextResolutionOperatesUnderIt_WithNoRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-from-file-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var draftedPath = Path.Combine(Path.GetTempPath(), $"wiki-identity-drafted-{Guid.NewGuid():N}.md");
        var draftedContent = "# A Specialised Wiki\nThis is the drafted foundation document, verbatim.\n";

        try
        {
            await File.WriteAllTextAsync(draftedPath, draftedContent);

            var (exitCode, stdout) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath);

            Assert.Equal((int)CliExitCode.Success, exitCode);
            Assert.Contains("persisted", stdout, StringComparison.Ordinal);
            Assert.Equal(draftedContent, await File.ReadAllTextAsync(paths.InstanceFoundationPromptPath));

            // No restart between persisting and the next dispatch: the very next resolution
            // call already sees it (FR-013a, SC-003) — same in-process paths instance, no
            // cache to invalidate.
            var resolved = paths.ResolveEffectiveFoundationPrompt(paths.Query);
            Assert.Equal("instance", resolved.Source);
            Assert.Equal(paths.InstanceFoundationPromptPath, resolved.Path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(draftedPath))
            {
                File.Delete(draftedPath);
            }
        }
    }

    [Fact]
    public async Task FromFile_WithoutReplace_RefusesAndLeavesBytesUnchanged_WithReplace_Replaces()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-replace-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var originalContent = "# Original\nThe instance's current identity.\n";
        var draftedPath = Path.Combine(Path.GetTempPath(), $"wiki-identity-drafted-{Guid.NewGuid():N}.md");

        try
        {
            await File.WriteAllTextAsync(paths.InstanceFoundationPromptPath, originalContent);
            await File.WriteAllTextAsync(draftedPath, "# Replacement\nA new identity.\n");

            var (refusedExitCode, refusedStdout) =
                await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath);

            Assert.Equal((int)CliExitCode.StateConflict, refusedExitCode);
            Assert.Contains("already exists", refusedStdout, StringComparison.Ordinal);
            Assert.Equal(originalContent, await File.ReadAllTextAsync(paths.InstanceFoundationPromptPath));

            var (replacedExitCode, replacedStdout) =
                await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath, replace: true);

            Assert.Equal((int)CliExitCode.Success, replacedExitCode);
            Assert.Contains("replaced existing", replacedStdout, StringComparison.Ordinal);
            Assert.Equal("# Replacement\nA new identity.\n", await File.ReadAllTextAsync(paths.InstanceFoundationPromptPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(draftedPath))
            {
                File.Delete(draftedPath);
            }
        }
    }

    /// <summary>
    /// FR-015/FR-016/SC-006: a missing answer fails immediately, naming what to supply, and
    /// changes nothing — asserted via the same <see cref="WikiIdentitySettings.Validate"/>
    /// path Spectre itself calls before <c>ExecuteAsync</c> ever runs, so the command body
    /// never touches the filesystem. "Identical with and without a terminal attached" is
    /// structural rather than a branch to exercise twice: <see cref="WikiIdentityCommand"/>
    /// reads <c>Console.IsInputRedirected</c> exactly once, only to tag the wizard span
    /// (T049) — no code path in the command or its settings validation branches on it, so
    /// there is no interactive-vs-non-interactive behavior to diverge in the first place.
    /// </summary>
    [Fact]
    public async Task MissingAnswer_FailsImmediately_NamingWhatToSupply_ChangesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-missing-answer-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            var (exitCode, message) = await WikiIdentityCommandTestHarness.RunSetAsync(paths);

            Assert.Equal((int)CliExitCode.UsageError, exitCode);
            Assert.Contains("--default", message, StringComparison.Ordinal);
            Assert.Contains("--specialised", message, StringComparison.Ordinal);
            Assert.Contains("--from-file", message, StringComparison.Ordinal);
            Assert.False(File.Exists(paths.InstanceFoundationPromptPath));
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
    public async Task InstanceDocument_SurvivesAFreshResolutionInstance_MimickingARestart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-restart-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var draftedPath = Path.Combine(Path.GetTempPath(), $"wiki-identity-drafted-{Guid.NewGuid():N}.md");

        try
        {
            await File.WriteAllTextAsync(draftedPath, "# Persisted\nSurvives a restart.\n");
            var (exitCode, _) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath);
            Assert.Equal((int)CliExitCode.Success, exitCode);

            var beforeRestart = paths.ResolveEffectiveFoundationPrompt(paths.Lint);

            // A fresh ResolvedGrimoirePaths over the same data root, exactly as a fresh
            // process would build one on startup (FR-017/SC-007) — resolution holds no
            // in-memory cache to lose, so this must observe the identical document.
            var afterRestart = TestResolvedGrimoirePathsFactory.Create(root).ResolveEffectiveFoundationPrompt(paths.Lint);

            Assert.Equal("instance", afterRestart.Source);
            Assert.Equal(beforeRestart.Sha256, afterRestart.Sha256);
            Assert.Equal(beforeRestart.Path, afterRestart.Path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(draftedPath))
            {
                File.Delete(draftedPath);
            }
        }
    }
}
