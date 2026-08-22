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
        CancellationToken cancellationToken = default,
        SourceSubmissionMetadata? submission = null)
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
            CreatedAt: DateTimeOffset.UtcNow,
            Title: ExtractTitle(normalizedMarkdown),
            OriginalFileName: submission?.OriginalFileName,
            SourceUrl: submission?.SourceUrl);

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
        CancellationToken cancellationToken = default,
        SourceSubmissionMetadata? submission = null)
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
            CreatedAt: DateTimeOffset.UtcNow,
            // Byte-identical pass-through: the bytes ARE the normalized markdown, so the
            // heading is read from them exactly as on the converted path.
            Title: ExtractTitle(TryDecodeUtf8(normalizedBytes)),
            OriginalFileName: submission?.OriginalFileName,
            SourceUrl: submission?.SourceUrl);

        await WriteMetadataAsync(taskId, set, cancellationToken);
        return set;
    }

    /// <summary>
    /// 023-task-ui-improvements T020 (FR-003, research.md R4): the first ATX <c>#</c>
    /// heading of the normalized markdown, trimmed and capped at
    /// <see cref="TitleMaxLength"/>. Only a level-1 heading counts — a document that opens
    /// with a subsection heading has not told us its title — and only leading <c>#</c>
    /// markers are stripped, never the rest of the line's markup: this is a display label
    /// lifted verbatim from content the pipeline already produced, not an interpretation
    /// of it (Principle V).
    /// </summary>
    public const int TitleMaxLength = 120;

    internal static string? ExtractTitle(string? normalizedMarkdown)
    {
        if (string.IsNullOrWhiteSpace(normalizedMarkdown))
        {
            return null;
        }

        foreach (var rawLine in normalizedMarkdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line[2..].Trim();
            if (heading.Length == 0)
            {
                continue;
            }

            return heading.Length > TitleMaxLength ? heading[..TitleMaxLength] : heading;
        }

        return null;
    }

    /// <summary>
    /// Pass-through content is whatever the source was; a payload that is not valid UTF-8
    /// simply has no readable heading, which the fallback chain already covers.
    /// </summary>
    private static string? TryDecodeUtf8(byte[] bytes)
    {
        try
        {
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    /// <summary>
    /// Reads the manifest sidecar, or returns null when there is not (yet) a readable one.
    /// <para>
    /// "Not yet readable" and "not there" are deliberately the same answer. Six call sites read
    /// this — three of them endpoint handlers serving the board and detail views — and all of
    /// them already treat null as "conversion has not produced a manifest", which is exactly
    /// what a half-written manifest means. Surfacing the partial state as an exception instead
    /// turned a routine board poll during the conversion window into a 500.
    /// </para>
    /// </summary>
    public async Task<SourceArtifactSet?> TryReadMetadataAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var metadataPath = MetadataPathFor(taskId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync<SourceArtifactSet>(stream, cancellationToken: cancellationToken);
        }
        catch (IOException)
        {
            // Held exclusively by the writer, or vanished between the Exists check and the open.
            return null;
        }
        catch (JsonException)
        {
            // Truncated or otherwise incomplete content.
            return null;
        }
    }

    /// <summary>
    /// Writes the manifest sidecar atomically: serialize to a temporary file alongside it, then
    /// move it into place. `File.Create` truncates in place and holds the handle exclusively for
    /// the duration of serialization, so a concurrent reader saw either a locked file or a
    /// partially written one. A move is atomic on both supported platforms, so a reader now sees
    /// the previous manifest or the new one, never a half of either.
    /// </summary>
    private async Task WriteMetadataAsync(string taskId, SourceArtifactSet set, CancellationToken cancellationToken)
    {
        var metadataPath = MetadataPathFor(taskId);
        var tempPath = $"{metadataPath}.{Guid.NewGuid():n}.tmp";

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, set, cancellationToken: cancellationToken);
            }

            File.Move(tempPath, metadataPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private string MetadataPathFor(string taskId) => Path.Combine(_paths.OriginalsDir, $"{taskId}.manifest.json");
}
