using System.Text;
using System.Text.Json;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.ContentRoot;
using Xunit;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The manifest sidecar is written while conversion runs and read by six call sites, three of
/// them endpoint handlers serving the board and detail views. A reader arriving inside that
/// window used to get an <see cref="IOException"/> ("the process cannot access the file")
/// because the writer held the handle exclusively, or a <see cref="JsonException"/> from
/// content that was only half serialized.
///
/// <para>
/// The contract these tests pin is ours, not the filesystem's: an unreadable manifest reads as
/// "no manifest yet" — the same answer callers already handle — instead of throwing out through
/// a board poll.
/// </para>
/// </summary>
public class SourceArtifactManifestReadTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"grimoire-manifest-read-{Guid.NewGuid():n}");

    private readonly SourceArtifactStore _store;
    private readonly string _originalsDir;

    public SourceArtifactManifestReadTests()
    {
        _originalsDir = Path.Combine(_root, "raw", "originals");
        Directory.CreateDirectory(_originalsDir);
        _store = new SourceArtifactStore(
            new RawStoragePaths(_originalsDir, Path.Combine(_root, "raw", "sources")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ManifestPathFor(string taskId) =>
        Path.Combine(_originalsDir, $"{taskId}.manifest.json");

    [Fact]
    public async Task ManifestHeldExclusivelyByTheWriter_ReadsAsNoManifest_RatherThanThrowing()
    {
        const string taskId = "2026-08-22-ingest-locked";

        // Exactly the handle `File.Create` takes while it serializes: exclusive, no sharing.
        using var writerHandle = new FileStream(
            ManifestPathFor(taskId), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var manifest = await _store.TryReadMetadataAsync(taskId);

        Assert.Null(manifest);
    }

    [Fact]
    public async Task PartiallyWrittenManifest_ReadsAsNoManifest_RatherThanThrowing()
    {
        const string taskId = "2026-08-22-ingest-truncated";

        // A manifest caught mid-serialization: valid JSON prefix, no closing brace.
        await File.WriteAllTextAsync(
            ManifestPathFor(taskId), "{\"TaskId\":\"" + taskId + "\",\"OriginalPath\":\"/tmp/x.md\",", Encoding.UTF8);

        var manifest = await _store.TryReadMetadataAsync(taskId);

        Assert.Null(manifest);
    }

    /// <summary>
    /// The guard on the guard: a tolerant read must not become a silent one. If the catch is
    /// ever broadened to swallow every exception, or the read path breaks, this goes red while
    /// the two tests above stay green.
    /// </summary>
    [Fact]
    public async Task ReadableManifest_IsStillReturned_NotSwallowedByTheTolerantRead()
    {
        const string taskId = "2026-08-22-ingest-complete";
        var written = new SourceArtifactSet(
            TaskId: taskId,
            OriginalPath: Path.Combine(_originalsDir, $"{taskId}.md"),
            OriginalContentType: "text/markdown",
            OriginalSizeBytes: 22,
            NormalizedMarkdownPath: Path.Combine(_root, "raw", "sources", $"{taskId}.md"),
            NormalizedChecksum: new string('a', 64),
            CreatedAt: new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero),
            OriginalFileName: "Retention policy draft.md");

        await File.WriteAllTextAsync(ManifestPathFor(taskId), JsonSerializer.Serialize(written));

        var manifest = await _store.TryReadMetadataAsync(taskId);

        Assert.NotNull(manifest);
        Assert.Equal(written.OriginalPath, manifest!.OriginalPath);
        Assert.Equal("Retention policy draft.md", manifest.OriginalFileName);
    }
}
