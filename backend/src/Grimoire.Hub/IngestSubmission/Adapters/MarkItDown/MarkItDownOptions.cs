namespace Grimoire.Hub.IngestSubmission.Adapters.MarkItDown;

/// <summary>
/// Configuration surface for the MarkItDown conversion adapter (research.md Decision 1).
/// The executable is an external prerequisite (see quickstart.md); no conversion logic lives here.
/// </summary>
public sealed record MarkItDownOptions(string ExecutablePath, TimeSpan Timeout)
{
    public const string DefaultExecutablePath = "markitdown";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static MarkItDownOptions FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var executablePath = configuration["MarkItDown:ExecutablePath"] ?? DefaultExecutablePath;
        var timeoutSeconds = configuration.GetValue<double?>("MarkItDown:TimeoutSeconds");
        var timeout = timeoutSeconds.HasValue ? TimeSpan.FromSeconds(timeoutSeconds.Value) : DefaultTimeout;
        return new MarkItDownOptions(executablePath, timeout);
    }
}
