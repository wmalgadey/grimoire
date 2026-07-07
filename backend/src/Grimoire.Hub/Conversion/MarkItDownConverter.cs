using System.Diagnostics;

namespace Grimoire.Hub.Conversion;

public sealed record MarkItDownConversionResult(bool Success, string? Markdown, string? FailureReason);

/// <summary>
/// Process-invocation adapter around the MarkItDown CLI (research.md Decision 1): the single
/// conversion entrypoint for PDF/Office documents and fetched URL content. Markdown files are
/// passed through as-is by the caller and never routed through this adapter (FR-004).
/// </summary>
public sealed class MarkItDownConverter
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

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new MarkItDownConversionResult(false, null, string.IsNullOrWhiteSpace(stderr) ? $"markitdown exited with code {process.ExitCode}" : stderr.Trim());
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
