using System.Security.Cryptography;
using System.Text.Json;
using Grimoire.Hub.ContentRoot;

namespace Grimoire.Hub.Conversion;

/// <summary>
/// Persists the original payload and normalized markdown for one accepted ingest submission
/// (data-model.md SourceArtifactSet, contracts/source-artifact-reference.md). Also records a
/// small JSON sidecar of the SourceArtifactSet metadata next to the original payload, so the
/// board/detail view can read provenance independently of the Task Artifact frontmatter (which
/// the Ingest agent later overwrites with its own agent-owned fields).
/// </summary>
public sealed class SourceArtifactStore
{
    private readonly RawStoragePaths _paths;

    public SourceArtifactStore(RawStoragePaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Persists the original payload only (step 1). Returns the path so it can be used as the
    /// MarkItDown conversion input; the metadata sidecar is written once <see cref="PersistNormalizedAsync"/>
    /// completes, so a manifest never references a normalized artifact that doesn't exist yet.
    /// </summary>
    public async Task<string> PersistOriginalAsync(
        string taskId, string extension, byte[] originalBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.OriginalsDir);
        var originalPath = _paths.OriginalPathFor(taskId, extension);
        await File.WriteAllBytesAsync(originalPath, originalBytes, cancellationToken);
        return originalPath;
    }

    /// <summary>
    /// Persists the normalized markdown (step 2) and writes the SourceArtifactSet manifest
    /// (data-model.md SourceArtifactSet, contracts/source-artifact-reference.md).
    /// </summary>
    public async Task<SourceArtifactSet> PersistNormalizedAsync(
        string taskId,
        string originalPath,
        string originalContentType,
        long originalSizeBytes,
        string normalizedMarkdown,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.SourcesDir);
        var normalizedPath = _paths.NormalizedMarkdownPathFor(taskId);
        var checksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedMarkdown))).ToLowerInvariant();

        try
        {
            await File.WriteAllTextAsync(normalizedPath, normalizedMarkdown, System.Text.Encoding.UTF8, cancellationToken);
        }
        catch
        {
            // FR-009/SC-003: a failed write must not leave a partial normalized artifact behind.
            DeletePartialNormalizedArtifact(taskId);
            throw;
        }

        var set = new SourceArtifactSet(
            TaskId: taskId,
            OriginalPath: originalPath,
            OriginalContentType: originalContentType,
            OriginalSizeBytes: originalSizeBytes,
            NormalizedMarkdownPath: normalizedPath,
            NormalizedChecksum: checksum,
            CreatedAt: DateTimeOffset.UtcNow);

        await WriteMetadataAsync(taskId, set, cancellationToken);
        return set;
    }

    /// <summary>
    /// Persists the received content byte-identical as the normalized artifact — the
    /// pass-through path when the document-to-Markdown convert step is disabled
    /// (004 FR-012, SC-004). Checksum is computed over the unmodified bytes.
    /// </summary>
    public async Task<SourceArtifactSet> PersistNormalizedBytesAsync(
        string taskId,
        string originalPath,
        string originalContentType,
        long originalSizeBytes,
        byte[] normalizedBytes,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.SourcesDir);
        var normalizedPath = _paths.NormalizedMarkdownPathFor(taskId);
        var checksum = Convert.ToHexString(SHA256.HashData(normalizedBytes)).ToLowerInvariant();

        try
        {
            await File.WriteAllBytesAsync(normalizedPath, normalizedBytes, cancellationToken);
        }
        catch
        {
            DeletePartialNormalizedArtifact(taskId);
            throw;
        }

        var set = new SourceArtifactSet(
            TaskId: taskId,
            OriginalPath: originalPath,
            OriginalContentType: originalContentType,
            OriginalSizeBytes: originalSizeBytes,
            NormalizedMarkdownPath: normalizedPath,
            NormalizedChecksum: checksum,
            CreatedAt: DateTimeOffset.UtcNow);

        await WriteMetadataAsync(taskId, set, cancellationToken);
        return set;
    }

    /// <summary>
    /// Removes any normalized artifact left behind by a failed conversion/fetch (FR-009, SC-003).
    /// Safe to call even if no file was ever written.
    /// </summary>
    public void DeletePartialNormalizedArtifact(string taskId)
    {
        var normalizedPath = _paths.NormalizedMarkdownPathFor(taskId);
        if (File.Exists(normalizedPath))
        {
            File.Delete(normalizedPath);
        }
    }

    public async Task<SourceArtifactSet?> TryReadMetadataAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var metadataPath = MetadataPathFor(taskId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<SourceArtifactSet>(stream, cancellationToken: cancellationToken);
    }

    private async Task WriteMetadataAsync(string taskId, SourceArtifactSet set, CancellationToken cancellationToken)
    {
        var metadataPath = MetadataPathFor(taskId);
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, set, cancellationToken: cancellationToken);
    }

    private string MetadataPathFor(string taskId) => Path.Combine(_paths.OriginalsDir, $"{taskId}.manifest.json");
}
