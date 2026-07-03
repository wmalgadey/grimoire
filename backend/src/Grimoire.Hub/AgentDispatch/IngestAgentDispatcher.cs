using System.Diagnostics;

namespace Grimoire.Hub.AgentDispatch;

public sealed class IngestAgentDispatcher
{
    private readonly LocalSecretsLoader _secretsLoader;
    private readonly string _agentProjectPath;

    public IngestAgentDispatcher(LocalSecretsLoader secretsLoader, string agentProjectPath)
    {
        _secretsLoader = secretsLoader;
        _agentProjectPath = agentProjectPath;
    }

    public async Task<int> DispatchAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(_agentProjectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--task-id");
        startInfo.ArgumentList.Add(request.TaskId);
        startInfo.ArgumentList.Add("--source-ref");
        startInfo.ArgumentList.Add(request.SourceRef);
        startInfo.ArgumentList.Add("--source-kind");
        startInfo.ArgumentList.Add(request.SourceKind);
        startInfo.ArgumentList.Add("--pages-dir");
        startInfo.ArgumentList.Add(request.PagesDir);
        startInfo.ArgumentList.Add("--tasks-dir");
        startInfo.ArgumentList.Add(request.TasksDir);
        startInfo.ArgumentList.Add("--index-path");
        startInfo.ArgumentList.Add(request.IndexPath);
        startInfo.ArgumentList.Add("--log-path");
        startInfo.ArgumentList.Add(request.LogPath);

        var authToken = _secretsLoader.GetAnthropicAuthToken();
        // Explicitly remove both the legacy key name and the current token name so
        // neither leaks into the child process from the parent environment (ADR-004).
        // The token is only present in the child if it was explicitly loaded from the
        // local secrets file.
        startInfo.Environment.Remove("ANTHROPIC_API_KEY");
        startInfo.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            startInfo.Environment["ANTHROPIC_AUTH_TOKEN"] = authToken;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ingest agent process.");

        if (request.SourceKind == "pasted_text" && !string.IsNullOrWhiteSpace(request.PastedText))
        {
            await process.StandardInput.WriteAsync(request.PastedText);
        }

        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        _ = await stdOutTask;
        var stdErr = await stdErrTask;

        return process.ExitCode > 1 && !string.IsNullOrWhiteSpace(stdErr)
            ? throw new InvalidOperationException($"Ingest agent crashed: {stdErr}")
            : process.ExitCode;
    }
}
