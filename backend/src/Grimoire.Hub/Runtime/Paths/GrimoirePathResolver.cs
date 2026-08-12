using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Startup validation failure for a runtime path location (FR-013, SC-006/SC-007). Carries
/// the logical location name, the raw configured value, and the resolved absolute path so
/// the message names exactly what is wrong (Constitution IV / plan.md ## Observability).
/// </summary>
public sealed class GrimoirePathValidationException(string location, string configuredValue, string resolvedPath, string reason)
    : Exception($"{location}: configured '{configuredValue}' resolved to '{resolvedPath}' — {reason}")
{
    public string Location { get; } = location;
    public string ConfiguredValue { get; } = configuredValue;
    public string ResolvedPath { get; } = resolvedPath;
    public string Reason { get; } = reason;
}

/// <summary>
/// Startup failure when one of the four mandatory roots (<c>Data:Dir</c>, <c>Wiki:Dir</c>,
/// <c>Agent:Dir</c>, <c>Memory:Dir</c>) is absent from every configuration tier (ADR-022
/// FR-006/SC-004, amended by ADR-024). The versioned <c>appsettings.json</c> is the sole
/// source of default paths — there is no code-level fallback — so this is a distinct,
/// named failure from <see cref="GrimoirePathValidationException"/> rather than a
/// location that merely fails to resolve. <paramref name="missingKeys"/> carries full key
/// paths (e.g. <c>Grimoire:Paths:Memory:Dir</c>), not bare field names.
/// </summary>
public sealed class GrimoirePathConfigurationMissingException(string configurationFile, IReadOnlyList<string> missingKeys)
    : Exception($"{configurationFile}: missing required configuration key(s) {string.Join(", ", missingKeys)}.")
{
    public string ConfigurationFile { get; } = configurationFile;
    public IReadOnlyList<string> MissingKeys { get; } = missingKeys;
}

/// <summary>
/// The single composition point for every runtime location (ADR-022, amended by
/// ADR-024): resolves <see cref="GrimoirePathOptions"/> against their documented
/// anchors, records each location's effective configuration source, validates required
/// inputs (fail-fast), auto-creates writable data locations, and reports the result via
/// <see cref="GrimoirePathLogEvents"/>. No other production type may read the process's
/// ambient working directory or install directory (enforced by
/// RuntimePathsBoundaryRuleTests).
/// </summary>
public static class GrimoirePathResolver
{
    /// <summary>
    /// The only sanctioned read of the process working directory in the whole
    /// application; everything else that needs it (e.g. submitted relative source
    /// paths) goes through this property instead of calling
    /// <see cref="Directory.GetCurrentDirectory"/> directly.
    /// </summary>
    public static string CurrentWorkingDirectory => Directory.GetCurrentDirectory();

    /// <summary>
    /// The only sanctioned read of the process's install/build-output directory (017-
    /// hub-help-usage). Its sole remaining consumer is pinning
    /// <c>WebApplicationBuilder.ContentRootPath</c> in <c>HubHostComposition</c> so
    /// <c>appsettings.json</c> loads from beside <c>Grimoire.Hub.dll</c> regardless of the
    /// launching working directory — no runtime path anchors here any more (ADR-022).
    /// </summary>
    public static string ProcessBaseDirectory => AppContext.BaseDirectory;

    public static ResolvedGrimoirePaths Resolve(GrimoirePathOptions options, IConfiguration configuration, ILogger logger)
    {
        var configRoot = configuration as IConfigurationRoot
            ?? throw new ArgumentException(
                "Configuration must be an IConfigurationRoot to determine each location's effective source.",
                nameof(configuration));

        // Mandatory-configuration gate (FR-006/SC-004): the four roots must each carry a
        // non-empty value before anything is resolved or touched on disk. appsettings.json
        // is the sole source of default paths — there is no code-level fallback tier.
        // Reported as full key paths (Grimoire:Paths:Memory:Dir), not bare field names, so
        // the message names something an operator can grep for verbatim in the file.
        var missingRootKeys = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Data.Dir)) missingRootKeys.Add("Grimoire:Paths:Data:Dir");
        if (string.IsNullOrWhiteSpace(options.Wiki.Dir)) missingRootKeys.Add("Grimoire:Paths:Wiki:Dir");
        if (string.IsNullOrWhiteSpace(options.Agent.Dir)) missingRootKeys.Add("Grimoire:Paths:Agent:Dir");
        if (string.IsNullOrWhiteSpace(options.Memory.Dir)) missingRootKeys.Add("Grimoire:Paths:Memory:Dir");
        if (missingRootKeys.Count > 0)
        {
            HubMetrics.RecordPathResolutionFailure("configuration_missing");
            GrimoirePathLogEvents.LogConfigurationMissing(logger, "appsettings.json", missingRootKeys);
            throw new GrimoirePathConfigurationMissingException("appsettings.json", missingRootKeys);
        }

        var dataDir = ResolveAgainst(options.Data.Dir, CurrentWorkingDirectory);
        var wikiDir = ResolveAgainst(options.Wiki.Dir, CurrentWorkingDirectory);
        // Anchored at cwd, same as DataDir/WikiDir — NOT at the resolved DataDir (reviewer
        // confirmation, PR #55): relocating --data-dir must never silently drag the agent
        // runtime along with it. The default literal value (".grimoire/agents" in
        // appsettings.json) spells the nesting out explicitly instead of relying on an
        // anchor-level special case.
        var agentDir = ResolveAgainst(options.Agent.Dir, CurrentWorkingDirectory);
        // Anchored at cwd, independent of the other three roots (022-memory-directory-root,
        // ADR-024) — never nested beneath DataDir/WikiDir/AgentDir unless an operator
        // explicitly configures it that way.
        var memoryDir = ResolveAgainst(options.Memory.Dir, CurrentWorkingDirectory);

        var rawDir = ResolveAgainst(options.Data.RawDir, dataDir);
        var stateDbPath = ResolveAgainst(options.Data.StateDb, dataDir);
        var writeLocksDir = ResolveAgainst(options.Data.WriteLocksDir, dataDir);
        // The four bookkeeping sub-paths anchor at MemoryDir, not WikiDir, as of
        // 022-memory-directory-root (ADR-024 N-A) — only their anchor moved; their
        // configured values and internal per-record layout are unchanged (FR-010).
        var tasksDir = ResolveAgainst(options.Memory.TasksDir, memoryDir);
        var conversationsDir = ResolveAgainst(options.Memory.ConversationsDir, memoryDir);
        var findingsDir = ResolveAgainst(options.Memory.FindingsDir, memoryDir);
        var remediationTasksDir = ResolveAgainst(options.Memory.RemediationTasksDir, memoryDir);
        var secretsFilePath = ResolveAgainst(options.SecretsFile, CurrentWorkingDirectory);

        var indexPath = Path.Combine(wikiDir, "index.md");
        var logPath = Path.Combine(wikiDir, "log.md");
        // 018-hub-cli-commands (ADR-020): fixed filename under the already-resolved data
        // directory, same treatment as indexPath/logPath above — no GrimoirePathOptions
        // field, no switch, no Locations entry (not independently configurable, not a
        // required input).
        var lintPidPath = Path.Combine(dataDir, GrimoirePathOptions.DefaultLintPidFileName);
        var rawOriginalsDir = Path.Combine(rawDir, "originals");
        var rawSourcesDir = Path.Combine(rawDir, "sources");

        var ingest = BuildAgentRuntimePaths(agentDir, "ingest", GrimoirePathOptions.DefaultAgentWorkerFileName, hasDefaultUserPrompt: true);
        var query = BuildAgentRuntimePaths(agentDir, "query", GrimoirePathOptions.DefaultQueryAgentWorkerFileName, hasDefaultUserPrompt: false);
        var lint = BuildAgentRuntimePaths(agentDir, "lint", GrimoirePathOptions.DefaultLintAgentWorkerFileName, hasDefaultUserPrompt: false);

        var locations = new List<PathLocation>
        {
            BuildLocation("data_dir", "Data:Dir", options.Data.Dir, dataDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("wiki_dir", "Wiki:Dir", options.Wiki.Dir, wikiDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("agent_dir", "Agent:Dir", options.Agent.Dir, agentDir, PathLocationKind.RequiredInput, configRoot),
            BuildLocation("memory_dir", "Memory:Dir", options.Memory.Dir, memoryDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("raw_dir", "Data:RawDir", options.Data.RawDir, rawDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("state_db", "Data:StateDb", options.Data.StateDb, stateDbPath, PathLocationKind.WritableData, configRoot),
            BuildLocation("write_locks_dir", "Data:WriteLocksDir", options.Data.WriteLocksDir, writeLocksDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("tasks_dir", "Memory:TasksDir", options.Memory.TasksDir, tasksDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("conversations_dir", "Memory:ConversationsDir", options.Memory.ConversationsDir, conversationsDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("findings_dir", "Memory:FindingsDir", options.Memory.FindingsDir, findingsDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("remediation_tasks_dir", "Memory:RemediationTasksDir", options.Memory.RemediationTasksDir, remediationTasksDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("secrets_file", "SecretsFile", options.SecretsFile, secretsFilePath, PathLocationKind.RequiredInput, configRoot),
        };

        // Validate required inputs — fail fast, before any writable location is touched.
        ValidateAgentDirectory(logger, options.Agent.Dir, agentDir);
        ValidateAgentRuntime(logger, "ingest", ingest);
        ValidateAgentRuntime(logger, "query", query);
        ValidateAgentRuntime(logger, "lint", lint);
        ValidateRequiredFile(logger, "secrets_file", options.SecretsFile, secretsFilePath);

        // Auto-create writable data locations.
        CreateDirectoryIfMissing(logger, "data_dir", options.Data.Dir, dataDir);
        CreateDirectoryIfMissing(logger, "wiki_dir", options.Wiki.Dir, wikiDir);
        CreateDirectoryIfMissing(logger, "memory_dir", options.Memory.Dir, memoryDir);
        CreateDirectoryIfMissing(logger, "raw_dir", options.Data.RawDir, rawDir);
        CreateDirectoryIfMissing(logger, "raw_originals_dir", options.Data.RawDir, rawOriginalsDir);
        CreateDirectoryIfMissing(logger, "raw_sources_dir", options.Data.RawDir, rawSourcesDir);
        CreateDirectoryIfMissing(logger, "write_locks_dir", options.Data.WriteLocksDir, writeLocksDir);
        CreateDirectoryIfMissing(logger, "tasks_dir", options.Memory.TasksDir, tasksDir);
        CreateDirectoryIfMissing(logger, "conversations_dir", options.Memory.ConversationsDir, conversationsDir);
        CreateDirectoryIfMissing(logger, "findings_dir", options.Memory.FindingsDir, findingsDir);
        CreateDirectoryIfMissing(logger, "remediation_tasks_dir", options.Memory.RemediationTasksDir, remediationTasksDir);
        var stateDbDir = Path.GetDirectoryName(stateDbPath);
        if (!string.IsNullOrEmpty(stateDbDir))
        {
            CreateDirectoryIfMissing(logger, "state_db_dir", options.Data.StateDb, stateDbDir);
        }

        var resolved = new ResolvedGrimoirePaths(
            DataDir: dataDir,
            WikiDir: wikiDir,
            AgentDir: agentDir,
            MemoryDir: memoryDir,
            RawOriginalsDir: rawOriginalsDir,
            RawSourcesDir: rawSourcesDir,
            StateDbPath: stateDbPath,
            WriteLocksDir: writeLocksDir,
            LintPidPath: lintPidPath,
            TasksDir: tasksDir,
            ConversationsDir: conversationsDir,
            FindingsDir: findingsDir,
            RemediationTasksDir: remediationTasksDir,
            IndexPath: indexPath,
            LogPath: logPath,
            SecretsFilePath: secretsFilePath,
            Ingest: ingest,
            Query: query,
            Lint: lint,
            Locations: locations);

        GrimoirePathLogEvents.LogPathsResolved(logger, resolved);
        return resolved;
    }

    /// <summary>
    /// Derives one agent type's complete runtime layout under the (already-resolved)
    /// agent directory — a fixed, non-configurable subfolder structure per the agent
    /// build contract (FR-008, data-model.md §2): <c>&lt;AgentDir&gt;/&lt;agentId&gt;/</c>
    /// holds the worker DLL at its root and instruction documents under
    /// <c>Instructions/</c>.
    /// </summary>
    private static AgentRuntimePaths BuildAgentRuntimePaths(string agentDir, string agentId, string workerFileName, bool hasDefaultUserPrompt)
    {
        var dir = Path.Combine(agentDir, agentId);
        var workerPath = Path.Combine(dir, workerFileName);
        var instructionsDir = Path.Combine(dir, "Instructions");
        var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
        var policyPath = Path.Combine(instructionsDir, "policy.json");
        var defaultUserPromptPath = hasDefaultUserPrompt ? Path.Combine(instructionsDir, "default-user-prompt.md") : null;

        return new AgentRuntimePaths(dir, workerPath, instructionsDir, systemPromptPath, policyPath, defaultUserPromptPath);
    }

    private static string ResolveAgainst(string? configuredValue, string anchor)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return Path.GetFullPath(anchor);
        }

        return Path.IsPathRooted(configuredValue)
            ? Path.GetFullPath(configuredValue)
            : Path.GetFullPath(Path.Combine(anchor, configuredValue));
    }

    private static PathLocation BuildLocation(
        string name, string configKeySuffix, string? configuredValue, string resolvedPath,
        PathLocationKind kind, IConfigurationRoot configRoot)
    {
        var key = $"{GrimoirePathOptions.SectionName}:{configKeySuffix}";
        var source = DetermineSource(configRoot.Providers, key);
        var displayValue = string.IsNullOrWhiteSpace(configuredValue) ? "(default)" : configuredValue;
        return new PathLocation(name, displayValue, resolvedPath, kind, source);
    }

    private static string DetermineSource(IEnumerable<IConfigurationProvider> providers, string key)
    {
        foreach (var provider in providers.Reverse())
        {
            if (!provider.TryGet(key, out var value) || string.IsNullOrEmpty(value))
                continue;

            return provider switch
            {
                CommandLineConfigurationProvider => "command-line",
                EnvironmentVariablesConfigurationProvider => "environment",
                JsonConfigurationProvider => "config-file",
                _ => "config-file",
            };
        }

        // Unreachable in practice: the mandatory-configuration gate above already fails
        // fast before this point for any of the four roots, and every other configured
        // key ships a non-empty value in the versioned appsettings.json.
        return "config-file";
    }

    /// <summary>
    /// <c>agent_dir</c> gets a distinct check beyond "directory exists": present but
    /// empty fails with a reason that names the directory, not an individual file inside
    /// it (FR-013/SC-007, data-model.md §5).
    /// </summary>
    private static void ValidateAgentDirectory(ILogger logger, string? configuredValue, string agentDir)
    {
        var displayValue = string.IsNullOrWhiteSpace(configuredValue) ? "(default)" : configuredValue;

        if (File.Exists(agentDir))
        {
            Fail(logger, "agent_dir", displayValue, agentDir, "expected a directory but found a file.", "agent_directory_empty");
        }

        if (!Directory.Exists(agentDir))
        {
            Fail(logger, "agent_dir", displayValue, agentDir, "required directory does not exist.", "agent_directory_empty");
        }

        if (!Directory.EnumerateFileSystemEntries(agentDir).Any())
        {
            Fail(logger, "agent_dir", displayValue, agentDir, "agent directory contains no agent runtime.", "agent_directory_empty");
        }
    }

    /// <summary>
    /// Validates one agent type's subfolder, instruction documents, and worker DLL — all
    /// derived from <c>agent_dir</c>, so their "configured value" is displayed as such
    /// rather than looked up independently (FR-008: only <c>--agent-dir</c> is
    /// configurable, not any file beneath it).
    /// </summary>
    private static void ValidateAgentRuntime(ILogger logger, string agentId, AgentRuntimePaths paths)
    {
        const string derivedFromAgentDir = "(derived from agent_dir)";

        ValidateRequiredDirectory(logger, $"{agentId}_dir", derivedFromAgentDir, paths.Dir);
        ValidateRequiredDirectory(logger, $"{agentId}_instructions_dir", derivedFromAgentDir, paths.InstructionsDir);
        ValidateRequiredFile(logger, $"{agentId}_system_prompt", derivedFromAgentDir, paths.SystemPromptPath);
        ValidateRequiredFile(logger, $"{agentId}_policy", derivedFromAgentDir, paths.PolicyPath);
        if (paths.DefaultUserPromptPath is not null)
        {
            ValidateRequiredFile(logger, $"{agentId}_default_user_prompt", derivedFromAgentDir, paths.DefaultUserPromptPath);
        }

        ValidateRequiredWorkerFile(logger, $"{agentId}_agent_worker", derivedFromAgentDir, paths.WorkerPath);
    }

    private static void ValidateRequiredFile(ILogger logger, string location, string? configuredValue, string resolvedPath)
    {
        var displayValue = string.IsNullOrWhiteSpace(configuredValue) ? "(default)" : configuredValue;
        if (Directory.Exists(resolvedPath))
        {
            Fail(logger, location, displayValue, resolvedPath, "expected a file but found a directory.");
        }

        if (!File.Exists(resolvedPath))
        {
            Fail(logger, location, displayValue, resolvedPath, "required file does not exist.");
        }
    }

    /// <summary>
    /// A missing agent worker DLL gets a distinct, actionable reason (FR-020/FR-021):
    /// the hub never builds one itself, so the message tells the operator what to run.
    /// </summary>
    private static void ValidateRequiredWorkerFile(ILogger logger, string location, string configuredValue, string resolvedPath)
    {
        if (Directory.Exists(resolvedPath))
        {
            Fail(logger, location, configuredValue, resolvedPath, "expected a file but found a directory.");
        }

        if (!File.Exists(resolvedPath))
        {
            var workerFileName = Path.GetFileName(resolvedPath);
            Fail(logger, location, configuredValue, resolvedPath,
                $"{workerFileName} not found in the agent directory. Build first: dotnet build backend/Grimoire.slnx");
        }
    }

    private static void ValidateRequiredDirectory(ILogger logger, string location, string? configuredValue, string resolvedPath)
    {
        var displayValue = string.IsNullOrWhiteSpace(configuredValue) ? "(default)" : configuredValue;
        if (File.Exists(resolvedPath))
        {
            Fail(logger, location, displayValue, resolvedPath, "expected a directory but found a file.");
        }

        if (!Directory.Exists(resolvedPath))
        {
            Fail(logger, location, displayValue, resolvedPath, "required directory does not exist.");
        }
    }

    private static void Fail(ILogger logger, string location, string configuredValue, string resolvedPath, string reason, string metricReason = "location_invalid")
    {
        HubMetrics.RecordPathResolutionFailure(metricReason);
        GrimoirePathLogEvents.LogValidationFailed(logger, location, configuredValue, resolvedPath, reason);
        throw new GrimoirePathValidationException(location, configuredValue, resolvedPath, reason);
    }

    private static void CreateDirectoryIfMissing(ILogger logger, string location, string? configuredValue, string resolvedPath)
    {
        if (Directory.Exists(resolvedPath))
            return;

        var displayValue = string.IsNullOrWhiteSpace(configuredValue) ? "(default)" : configuredValue;

        if (File.Exists(resolvedPath))
        {
            Fail(logger, location, displayValue, resolvedPath, "expected a directory but found a file.");
        }

        Directory.CreateDirectory(resolvedPath);
        GrimoirePathLogEvents.LogLocationCreated(logger, location, resolvedPath);
    }
}
