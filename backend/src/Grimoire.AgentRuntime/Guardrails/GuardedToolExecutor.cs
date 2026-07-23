using Grimoire.Domain.Guardrails;
using System.Text;
using System.Text.Json;

namespace Grimoire.AgentRuntime.Guardrails;

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
    private readonly ToolRegistry _registry;
    private readonly IToolCallInstrumentation _instrumentation;
    private readonly List<DeniedActionRecord> _denials = [];
    private readonly List<string> _touchedPaths = [];

    public GuardedToolExecutor(
        SafetyPolicy policy,
        WriteJournal journal,
        string repositoryRoot,
        string? taskId = null,
        ToolRegistry? registry = null,
        IToolCallInstrumentation? instrumentation = null)
    {
        _policy = policy;
        _journal = journal;
        _repositoryRoot = repositoryRoot;
        _taskId = taskId ?? string.Empty;
        _registry = registry ?? ToolRegistry.Default;
        _instrumentation = instrumentation ?? NullToolCallInstrumentation.Instance;
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
        // A tool name this run's registry does not offer is always rejected as unknown —
        // even if a dispatch case for it exists below — so a read-only-configured
        // executor (Grimoire.QueryAgent) can never reach the write branch regardless of
        // what the model requests (ADR-011 R3, FR-011).
        if (!_registry.Supports(toolName))
        {
            return new ToolExecutionResult(true, $"Unknown tool: {toolName}");
        }

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

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.ListFiles, canonical, turn);

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

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.ReadFile, canonical, turn);

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
        _instrumentation.RecordAllowed(_taskId, ToolRegistry.WriteFile, canonical, turn);

        return new ToolExecutionResult(false, $"Written: {relativePath}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private ToolExecutionResult RecordDenial(string action, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        var record = new DeniedActionRecord(action, requestedTarget, canonicalTarget, reason, turn);
        _denials.Add(record);

        _instrumentation.RecordDenied(_taskId, action, requestedTarget, canonicalTarget, reason, turn);

        return new ToolExecutionResult(
            true,
            $"denied: {reason}. This action is outside the safety policy; continue with your remaining allowed work.");
    }

    /// <summary>
    /// Resolves a repo-root-relative (or absolute) path to a canonical absolute path.
    /// Applies lexical normalization and resolves symbolic links for existing
    /// path segments so policy evaluation is performed on the physical target.
    /// </summary>
    private string Canonicalize(string path)
    {
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_repositoryRoot, path));

        return ResolvePhysicalPathInRepository(fullPath);
    }

    private string ResolvePhysicalPathInRepository(string fullPath)
    {
        var canonical = Path.GetFullPath(fullPath);

        if (!IsWithinRepositoryRoot(canonical))
        {
            return canonical;
        }

        var relative = Path.GetRelativePath(_repositoryRoot, canonical);
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = _repositoryRoot;
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);

            if (!TryResolveLinkTarget(current, out var targetPath))
            {
                continue;
            }

            current = targetPath;
            for (var j = i + 1; j < parts.Length; j++)
            {
                current = Path.Combine(current, parts[j]);
            }

            break;
        }

        return Path.GetFullPath(current);
    }

    private bool IsWithinRepositoryRoot(string canonicalTarget)
    {
        var relative = Path.GetRelativePath(_repositoryRoot, canonicalTarget);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool TryResolveLinkTarget(string path, out string resolvedTarget)
    {
        resolvedTarget = string.Empty;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(path);

            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                return false;
            }

            resolvedTarget = Path.GetFullPath(target.FullName);
            return true;
        }
        catch
        {
            return false;
        }
    }

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
