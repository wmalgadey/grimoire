using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.Runtime.Paths;

/// <summary>
/// Startup validation failure for a runtime path location (FR-006, SC-002). Carries the
/// logical location name, the raw configured value, and the resolved absolute path so
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
/// The single composition point for every runtime location (ADR-009): resolves
/// <see cref="GrimoirePathOptions"/> against their documented anchors, records each
/// location's effective configuration source, validates required inputs (fail-fast),
/// auto-creates writable data locations, and reports the result via
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

    public static ResolvedGrimoirePaths Resolve(GrimoirePathOptions options, IConfiguration configuration, ILogger logger)
    {
        var configRoot = configuration as IConfigurationRoot
            ?? throw new ArgumentException(
                "Configuration must be an IConfigurationRoot to determine each location's effective source.",
                nameof(configuration));

        var baseDir = ResolveAgainst(options.BaseDir, CurrentWorkingDirectory, string.Empty);
        var contentRoot = ResolveAgainst(options.ContentRoot, baseDir, GrimoirePathOptions.DefaultContentRootDirName);
        var dataDir = ResolveAgainst(options.DataDir, baseDir, GrimoirePathOptions.DefaultDataDirName);

        var rawDir = ResolveAgainst(options.RawDir, dataDir, GrimoirePathOptions.DefaultRawDirName);
        var stateDbPath = ResolveAgainst(options.StateDb, dataDir, GrimoirePathOptions.DefaultStateDbRelativePath);
        var secretsFilePath = ResolveAgainst(options.SecretsFile, dataDir, GrimoirePathOptions.DefaultSecretsFileName);
        var instructionsDir = ResolveAgainst(options.InstructionsDir, dataDir, GrimoirePathOptions.DefaultInstructionsDirRelativePath);

        var agentWorkerPath = ResolveAgainst(options.AgentWorker, AppContext.BaseDirectory, GrimoirePathOptions.DefaultAgentWorkerFileName);

        var pagesDir = Path.Combine(contentRoot, "pages");
        var tasksDir = Path.Combine(contentRoot, "tasks");
        var indexPath = Path.Combine(contentRoot, "index.md");
        var logPath = Path.Combine(contentRoot, "log.md");
        var rawOriginalsDir = Path.Combine(rawDir, "originals");
        var rawSourcesDir = Path.Combine(rawDir, "sources");
        var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
        var defaultUserPromptPath = Path.Combine(instructionsDir, "default-user-prompt.md");
        var policyPath = Path.Combine(instructionsDir, "policy.json");

        var locations = new List<PathLocation>
        {
            BuildLocation("base_dir", "BaseDir", options.BaseDir, baseDir, PathLocationKind.RequiredInput, configRoot),
            BuildLocation("data_dir", "DataDir", options.DataDir, dataDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("content_root", "ContentRoot", options.ContentRoot, contentRoot, PathLocationKind.WritableData, configRoot),
            BuildLocation("raw_dir", "RawDir", options.RawDir, rawDir, PathLocationKind.WritableData, configRoot),
            BuildLocation("state_db", "StateDb", options.StateDb, stateDbPath, PathLocationKind.WritableData, configRoot),
            BuildLocation("secrets_file", "SecretsFile", options.SecretsFile, secretsFilePath, PathLocationKind.RequiredInput, configRoot),
            BuildLocation("instructions_dir", "InstructionsDir", options.InstructionsDir, instructionsDir, PathLocationKind.RequiredInput, configRoot),
            BuildLocation("agent_worker", "AgentWorker", options.AgentWorker, agentWorkerPath, PathLocationKind.RequiredInput, configRoot),
        };

        // Validate required inputs — fail fast, before any writable location is touched.
        ValidateRequiredDirectory(logger, "base_dir", options.BaseDir, baseDir);
        ValidateRequiredFile(logger, "secrets_file", options.SecretsFile, secretsFilePath);
        ValidateRequiredDirectory(logger, "instructions_dir", options.InstructionsDir, instructionsDir);
        ValidateRequiredFile(logger, "system_prompt", options.InstructionsDir, systemPromptPath);
        ValidateRequiredFile(logger, "default_user_prompt", options.InstructionsDir, defaultUserPromptPath);
        ValidateRequiredFile(logger, "policy", options.InstructionsDir, policyPath);
        ValidateRequiredFile(logger, "agent_worker", options.AgentWorker, agentWorkerPath);

        // Auto-create writable data locations.
        CreateDirectoryIfMissing(logger, "data_dir", dataDir);
        CreateDirectoryIfMissing(logger, "content_root", contentRoot);
        CreateDirectoryIfMissing(logger, "pages_dir", pagesDir);
        CreateDirectoryIfMissing(logger, "tasks_dir", tasksDir);
        CreateDirectoryIfMissing(logger, "raw_dir", rawDir);
        CreateDirectoryIfMissing(logger, "raw_originals_dir", rawOriginalsDir);
        CreateDirectoryIfMissing(logger, "raw_sources_dir", rawSourcesDir);
        var stateDbDir = Path.GetDirectoryName(stateDbPath);
        if (!string.IsNullOrEmpty(stateDbDir))
        {
            CreateDirectoryIfMissing(logger, "state_db_dir", stateDbDir);
        }

        var resolved = new ResolvedGrimoirePaths(
            BaseDir: baseDir,
            DataDir: dataDir,
            ContentRoot: contentRoot,
            PagesDir: pagesDir,
            TasksDir: tasksDir,
            IndexPath: indexPath,
            LogPath: logPath,
            RawOriginalsDir: rawOriginalsDir,
            RawSourcesDir: rawSourcesDir,
            StateDbPath: stateDbPath,
            SecretsFilePath: secretsFilePath,
            InstructionsDir: instructionsDir,
            SystemPromptPath: systemPromptPath,
            DefaultUserPromptPath: defaultUserPromptPath,
            PolicyPath: policyPath,
            AgentWorkerPath: agentWorkerPath,
            Locations: locations);

        GrimoirePathLogEvents.LogPathsResolved(logger, resolved);
        return resolved;
    }

    private static string ResolveAgainst(string? configuredValue, string anchor, string defaultRelative)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return Path.IsPathRooted(configuredValue)
                ? Path.GetFullPath(configuredValue)
                : Path.GetFullPath(Path.Combine(anchor, configuredValue));
        }

        return Path.GetFullPath(Path.Combine(anchor, defaultRelative));
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

        return "default";
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

    private static void Fail(ILogger logger, string location, string configuredValue, string resolvedPath, string reason)
    {
        GrimoirePathLogEvents.LogValidationFailed(logger, location, configuredValue, resolvedPath, reason);
        throw new GrimoirePathValidationException(location, configuredValue, resolvedPath, reason);
    }

    private static void CreateDirectoryIfMissing(ILogger logger, string location, string resolvedPath)
    {
        if (Directory.Exists(resolvedPath))
            return;

        Directory.CreateDirectory(resolvedPath);
        GrimoirePathLogEvents.LogLocationCreated(logger, location, resolvedPath);
    }
}
