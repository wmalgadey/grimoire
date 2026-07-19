using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Hermetic stand-in for <see cref="IMarkdownConverter"/>: no subprocess, a scriptable
/// success/failure result returned for every call.
/// </summary>
public sealed class FakeMarkdownConverter : IMarkdownConverter
{
    private readonly MarkItDownConversionResult _result;

    public List<string> ConvertedPaths { get; } = [];

    public FakeMarkdownConverter(MarkItDownConversionResult result)
    {
        _result = result;
    }

    public static FakeMarkdownConverter Succeeding(string markdown) =>
        new(new MarkItDownConversionResult(true, markdown, null));

    public static FakeMarkdownConverter Failing(string failureReason) =>
        new(new MarkItDownConversionResult(false, null, failureReason));

    public Task<MarkItDownConversionResult> ConvertAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        lock (ConvertedPaths)
        {
            ConvertedPaths.Add(inputPath);
        }

        return Task.FromResult(_result);
    }
}
