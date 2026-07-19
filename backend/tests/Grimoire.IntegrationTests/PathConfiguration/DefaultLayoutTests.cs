using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// T015t (US2, SC-003/FR-004) — with zero path configuration, the content root resolves
/// to <c>&lt;cwd&gt;/wiki</c> and every internal data location beneath
/// <c>&lt;cwd&gt;/data</c>; overriding a single location leaves every other default intact
/// (US2 acceptance scenario 2).
/// </summary>
public class DefaultLayoutTests
{
    [Fact]
    public void ZeroConfiguration_ResolvesContentRootAndDataLocations_BeneathProcessWorkingDirectory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(cwd);

            // getcwd() resolves symlinks (macOS temp dirs: /var/folders → /private/var/
            // folders) and the resolver derives its base from the process CWD, so the
            // expectations must be built from the same canonical form.
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);
            var configRoot = new ConfigurationBuilder().Build();

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            Assert.Equal(Path.GetFullPath(cwd), resolved.BaseDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "wiki")), resolved.ContentRoot);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "raw", "originals")), resolved.RawOriginalsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "raw", "sources")), resolved.RawSourcesDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "state", "operational-state.db")), resolved.StateDbPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", ".env")), resolved.SecretsFilePath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "agents", "ingest")), resolved.InstructionsDir);

            // The wiki content root and the consolidated data directory never nest inside
            // one another (research R2/plan.md): the wiki can be its own independent
            // git repository.
            Assert.DoesNotContain(resolved.DataDir, resolved.ContentRoot, StringComparison.Ordinal);
            Assert.DoesNotContain(resolved.ContentRoot, resolved.DataDir, StringComparison.Ordinal);

            foreach (var location in resolved.Locations)
            {
                Assert.Equal("default", location.Source);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
        }
    }

    [Fact]
    public void OverridingOneLocation_LeavesEveryOtherDefaultIntact()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"grimoire-default-layout-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        var originalCwd = Directory.GetCurrentDirectory();
        var externalContentRoot = Path.Combine(Path.GetTempPath(), $"grimoire-external-wiki-{Guid.NewGuid():N}");

        try
        {
            Directory.SetCurrentDirectory(cwd);

            // Same canonicalization as ZeroConfiguration_… above (macOS /var symlink).
            cwd = Directory.GetCurrentDirectory();

            var options = PathConfigurationTestHelpers.SeedRequiredInputsForZeroConfig(cwd);

            // Route the override through configuration (not a direct field assignment) so
            // PathLocation.Source correctly attributes it, exactly as Program.cs's own
            // Bind() call would.
            var configRoot = new ConfigurationBuilder()
                .AddInMemoryCollection([new("Grimoire:Paths:ContentRoot", externalContentRoot)])
                .Build();
            configRoot.GetSection(GrimoirePathOptions.SectionName).Bind(options);

            var resolved = GrimoirePathResolver.Resolve(options, configRoot, NullLogger.Instance);

            // The overridden location took the override...
            Assert.Equal(Path.GetFullPath(externalContentRoot), resolved.ContentRoot);

            // ...while every other location still falls back to its documented default
            // beneath the (unconfigured) base directory (US2 acceptance scenario 2).
            Assert.Equal(Path.GetFullPath(cwd), resolved.BaseDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data")), resolved.DataDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "raw", "originals")), resolved.RawOriginalsDir);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "state", "operational-state.db")), resolved.StateDbPath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", ".env")), resolved.SecretsFilePath);
            Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "data", "agents", "ingest")), resolved.InstructionsDir);

            var contentRootLocation = resolved.Locations.Single(l => l.Name == "content_root");
            Assert.Equal("default", resolved.Locations.Single(l => l.Name == "data_dir").Source);
            Assert.NotEqual("default", contentRootLocation.Source);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
            if (Directory.Exists(externalContentRoot))
            {
                Directory.Delete(externalContentRoot, recursive: true);
            }
        }
    }
}
