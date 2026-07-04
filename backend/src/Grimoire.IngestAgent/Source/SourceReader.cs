using System.Net.Http;

namespace Grimoire.IngestAgent.Source;

public sealed class SourceReader
{
    private readonly HttpClient _httpClient;

    public SourceReader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ReadSourceResult> ReadAsync(string sourceKind, string sourceRef, string? pastedText, CancellationToken cancellationToken)
    {
        return sourceKind switch
        {
            "file" => await ReadFileAsync(sourceRef, cancellationToken),
            "url" => await ReadUrlAsync(sourceRef, cancellationToken),
            "pasted_text" => ReadPastedText(sourceRef, pastedText),
            _ => throw new ArgumentException($"Unsupported source kind: {sourceKind}"),
        };
    }

    private static async Task<ReadSourceResult> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return string.IsNullOrWhiteSpace(content)
            ? throw new InvalidOperationException("Source is empty.")
            : new ReadSourceResult(content, path);
    }

    private async Task<ReadSourceResult> ReadUrlAsync(string url, CancellationToken cancellationToken)
    {
        var content = await _httpClient.GetStringAsync(url, cancellationToken);
        return string.IsNullOrWhiteSpace(content)
            ? throw new InvalidOperationException("Source URL returned empty content.")
            : new ReadSourceResult(content, url);
    }

    private static ReadSourceResult ReadPastedText(string sourceRef, string? pastedText)
    {
        return string.IsNullOrWhiteSpace(pastedText)
            ? throw new InvalidOperationException("Pasted text source is empty.")
            : new ReadSourceResult(pastedText, sourceRef);
    }
}
