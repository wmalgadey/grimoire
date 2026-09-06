using System.Security.Cryptography;
using System.Text;
using Grimoire.AgentRuntime.Core.Adapters.Replay;

namespace Grimoire.EvalRunner.Recording;

/// <summary>
/// Computes the staleness fingerprint set for one scenario (research.md R4): the shared
/// foundation document (029-shared-foundation-prompt, ADR-053), the agent instruction
/// surface (ADR-007), the guardrail policy (ADR-006), the fixture tree, the scenario
/// definition, and — for judge-scored scenarios — the judge prompt template. Model
/// identity is provenance, not a staleness input.
/// </summary>
public static class Fingerprints
{
    public const string FoundationPromptKey = "foundation_prompt";
    public const string SystemPromptKey = "system_prompt";
    public const string DefaultUserPromptKey = "default_user_prompt";
    public const string PolicyKey = "policy";
    public const string FixtureKey = "fixture";
    public const string ScenarioDefinitionKey = "scenario_definition";
    public const string JudgePromptKey = "judge_prompt";

    public static IReadOnlyDictionary<string, string> Compute(
        string foundationPromptPath,
        string systemPromptPath,
        string? defaultUserPromptPath,
        string policyPath,
        string fixtureRoot,
        string scenarioDefinitionSerialization,
        string? judgePromptTemplate)
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FoundationPromptKey] = HashFile(foundationPromptPath),
            [SystemPromptKey] = HashFile(systemPromptPath),
            [PolicyKey] = HashFile(policyPath),
            [FixtureKey] = HashDirectory(fixtureRoot),
            [ScenarioDefinitionKey] = RecordingSerialization.Hash(scenarioDefinitionSerialization),
        };

        // Query scenarios have no default-user-prompt document at all (research.md R1 of
        // 008-query-agent, the user's Query Prompt is always supplied per turn) — omit
        // the fingerprint entirely rather than hashing a path that doesn't exist.
        if (defaultUserPromptPath is not null)
        {
            fingerprints[DefaultUserPromptKey] = HashFile(defaultUserPromptPath);
        }

        if (judgePromptTemplate is not null)
        {
            fingerprints[JudgePromptKey] = RecordingSerialization.Hash(judgePromptTemplate);
        }

        return fingerprints;
    }

    private static string HashFile(string path)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>
    /// Canonical hash of a directory tree: sorted relative paths (with '/' separators)
    /// each paired with the file's content hash, hashed together.
    /// </summary>
    private static string HashDirectory(string root)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            builder.Append(relative).Append('\u0000').Append(HashFile(file)).Append('\u0001');
        }

        return RecordingSerialization.Hash(builder.ToString());
    }
}
