using System.Diagnostics;
using Grimoire.Hub.Conversion;
using Grimoire.Hub.IngestSubmission;

namespace Grimoire.Hub.IngestSubmission.Adapters.MarkItDown;

/// <summary>
/// Process-invocation adapter around the MarkItDown CLI (research.md Decision 1; ADR-010
/// P2): the single conversion entrypoint for PDF/Office documents and fetched URL
/// content. Markdown files are passed through as-is by the caller and never routed
/// through this adapter (FR-004).
/// </summary>
public sealed class MarkItDownConverter : IMarkdownConverter
{
    private readonly MarkItDownOptions _options;

    public MarkItDownConverter(MarkItDownOptions options)
    {
        _options = options;
    }

    public async Task<MarkItDownConversionResult> ConvertAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        try
        {
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var rawReason = string.IsNullOrWhiteSpace(stderr) ? $"markitdown exited with code {process.ExitCode}" : stderr;
                return new MarkItDownConversionResult(false, null, ConversionFailureClassifier.Classify(rawReason));
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return new MarkItDownConversionResult(false, null, "markitdown produced no content");
            }

            return new MarkItDownConversionResult(true, stdout, null);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new MarkItDownConversionResult(false, null, $"markitdown conversion timed out after {_options.Timeout.TotalSeconds:0}s");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new MarkItDownConversionResult(false, null, $"markitdown could not be started: {ex.Message}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort: the process may have exited between the check and the kill.
        }
    }
}
