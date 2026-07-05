using Grimoire.Domain.Guardrails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace Grimoire.IngestAgent.Guardrails;

/// <summary>
/// Result of executing one guarded tool call.
/// </summary>
public sealed record ToolExecutionResult(
    bool IsError,
    string Content);

/// <summary>
/// Mediates every tool call from the agent loop: canonicalize target → evaluate
/// safety policy → deny (record, emit telemetry, return is_error) or allow
/// (journal for writes, execute, record touched path, emit telemetry).
/// Contracts per <c>contracts/guarded-tools.md</c>.
/// </summary>
public sealed class GuardedToolExecutor
{
    private readonly SafetyPolicy _policy;
    private readonly WriteJournal _journal;
    private readonly string _repositoryRoot;
    private readonly string _taskId;
    private readonly ILogger<GuardedToolExecutor> _logger;
    private readonly List<DeniedActionRecord> _denials = [];
    private readonly List<string> _touchedPaths = [];

    public GuardedToolExecutor(
        SafetyPolicy policy,
        WriteJournal journal,
        string repositoryRoot,
        string? taskId = null,
        ILogger<GuardedToolExecutor>? logger = null)
    {
        _policy = policy;
        _journal = journal;
        _repositoryRoot = repositoryRoot;
        _taskId = taskId ?? string.Empty;
        _logger = logger ?? NullLogger<GuardedToolExecutor>.Instance;
    }

    /// <summary>All policy denials that occurred during the run so far.</summary>
    public IReadOnlyList<DeniedActionRecord> Denials => _denials;

    /// <summary>All file paths successfully written during the run so far.</summary>
    public IReadOnlyList<string> TouchedPaths => _touchedPaths;

    /// <summary>
    /// Executes one tool call, applying policy, journaling, and telemetry.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string inputJson,
        int turn,
        CancellationToken cancellationToken)
    {
        switch (toolName)
        {
            case ToolRegistry.ListFiles:
                return await ExecuteListFilesAsync(inputJson, turn, cancellationToken);
            case ToolRegistry.ReadFile:
                return await ExecuteReadFileAsync(inputJson, turn, cancellationToken);
            case ToolRegistry.WriteFile:
                return await ExecuteWriteFileAsync(inputJson, turn, cancellationToken);
            default:
                return new ToolExecutionResult(true, $"Unknown tool: {toolName}");
        }
    }

    // ── list_files ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteListFilesAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: false);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.ListFiles, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        EmitAllowed(ToolRegistry.ListFiles, canonical, turn);
        IngestAgentMetrics.RecordToolCall(ToolRegistry.ListFiles, "allowed");

        if (!Directory.Exists(canonical))
        {
            return new ToolExecutionResult(true, $"Directory not found: {relativePath}");
        }

        var entries = new StringBuilder();
        foreach (var dir in Directory.GetDirectories(canonical).OrderBy(d => d, StringComparer.Ordinal))
        {
            entries.AppendLine(Path.GetRelativePath(_repositoryRoot, dir).Replace('\\', '/') + "/");
        }
        foreach (var file in Directory.GetFiles(canonical).OrderBy(f => f, StringComparer.Ordinal))
        {
            entries.AppendLine(Path.GetRelativePath(_repositoryRoot, file).Replace('\\', '/'));
        }

        return new ToolExecutionResult(false, entries.ToString().TrimEnd());
    }

    // ── read_file ────────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteReadFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: false);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.ReadFile, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        EmitAllowed(ToolRegistry.ReadFile, canonical, turn);
        IngestAgentMetrics.RecordToolCall(ToolRegistry.ReadFile, "allowed");

        if (!File.Exists(canonical))
        {
            return new ToolExecutionResult(true, $"File not found: {relativePath}");
        }

        var content = await File.ReadAllTextAsync(canonical, Encoding.UTF8, cancellationToken);
        return new ToolExecutionResult(false, content);
    }

    // ── write_file ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteWriteFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        if (!TryGetStringProperty(inputJson, "content", out var content))
        {
            return new ToolExecutionResult(true, "Missing required property: content");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: true);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.WriteFile, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        // Executor obligations (contract order):
        // 1. Journal prior state.
        await _journal.RecordAsync(canonical, cancellationToken);

        // 2. Create parent dirs inside the write scope.
        var parentDir = Path.GetDirectoryName(canonical);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        // 3. Atomic write via temp + rename within the same directory.
        var tempPath = canonical + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, canonical, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }

        // 4. Record touched path, emit telemetry.
        _touchedPaths.Add(canonical);
        EmitAllowed(ToolRegistry.WriteFile, canonical, turn);
        IngestAgentMetrics.RecordToolCall(ToolRegistry.WriteFile, "allowed");

        return new ToolExecutionResult(false, $"Written: {relativePath}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private ToolExecutionResult RecordDenial(string action, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        var record = new DeniedActionRecord(action, requestedTarget, canonicalTarget, reason, turn);
        _denials.Add(record);

        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.tool_call");
        span?.SetTag("task_id", _taskId);
        span?.SetTag("tool", action);
        span?.SetTag("target", canonicalTarget);
        span?.SetTag("requested_target", requestedTarget);
        span?.SetTag("decision", "denied");
        span?.SetTag("turn", turn);

        IngestAgentMetrics.RecordToolCall(action, "denied");
        IngestAgentMetrics.RecordActionDenied(action, reason);
        IngestAgentLogEvents.LogToolDenied(_logger, _taskId, action, canonicalTarget, reason, turn);

        return new ToolExecutionResult(
            true,
            $"denied: {reason}. This action is outside the safety policy; continue with your remaining allowed work.");
    }

    private void EmitAllowed(string tool, string target, int turn)
    {
        using var span = IngestAgentTracing.ActivitySource.StartActivity("ingest_agent.tool_call");
        span?.SetTag("task_id", _taskId);
        span?.SetTag("tool", tool);
        span?.SetTag("target", target);
        span?.SetTag("decision", "allowed");
        span?.SetTag("turn", turn);
        IngestAgentLogEvents.LogToolAllowed(_logger, _taskId, tool, target, turn);
    }

    /// <summary>
    /// Resolves a repo-root-relative (or absolute) path to a canonical absolute path.
    /// Handles lexical normalization (for example <c>..</c>) via
    /// <see cref="Path.GetFullPath"/>; symlinks are not resolved here.
    /// </summary>
    private string Canonicalize(string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_repositoryRoot, path));

    private static bool TryGetStringProperty(string json, string propertyName, out string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException) { }

        value = string.Empty;
        return false;
    }
}
