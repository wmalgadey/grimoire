using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Grimoire.IngestAgent.Guardrails;

public sealed class GuardrailPolicyLoader
{
    public async Task<GuardrailPolicy> LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Guardrail policy file was not found.", filePath);
        }

        var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var dto = deserializer.Deserialize<GuardrailPolicyDto>(yaml)
            ?? throw new InvalidOperationException("Guardrail policy file is empty or invalid.");

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            throw new InvalidOperationException("Guardrail policy version is required.");
        }

        if (!dto.DenyByDefault)
        {
            throw new InvalidOperationException("Guardrail policy must set deny_by_default to true for autonomous mode.");
        }

        var writePrefixes = (dto.WriteAllowPrefixes ?? []).Select(NormalizePath).Distinct(StringComparer.Ordinal).ToList();
        if (!writePrefixes.Any(p => p.StartsWith("wiki/", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Guardrail policy write_allow_prefixes must include wiki/.");
        }

        var readPaths = (dto.ReadAllowPaths ?? []).Select(NormalizePath).Distinct(StringComparer.Ordinal).ToList();
        var rules = (dto.Rules ?? [])
            .Select(r => new GuardrailRule(
                r.Id ?? "unnamed-rule",
                ParseAction(r.Action),
                NormalizePath(r.PathPrefix ?? string.Empty),
                string.Equals(r.Decision, "allow", StringComparison.OrdinalIgnoreCase),
                string.IsNullOrWhiteSpace(r.Reason) ? "Matched policy rule." : r.Reason.Trim()))
            .ToList();

        return new GuardrailPolicy(dto.Version.Trim(), dto.DenyByDefault, writePrefixes, readPaths, rules);
    }

    private static GuardrailAction ParseAction(string? value)
        => string.Equals(value, "write", StringComparison.OrdinalIgnoreCase) ? GuardrailAction.Write : GuardrailAction.Read;

    private static string NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Guardrail policy paths must be repository-relative.");
        }

        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(seg => seg == ".."))
        {
            throw new InvalidOperationException("Guardrail policy paths cannot include parent traversal segments.");
        }

        return normalized;
    }

    private sealed class GuardrailPolicyDto
    {
        public string? Version { get; set; }
        public bool DenyByDefault { get; set; }
        public List<string>? WriteAllowPrefixes { get; set; }
        public List<string>? ReadAllowPaths { get; set; }
        public List<GuardrailRuleDto>? Rules { get; set; }
    }

    private sealed class GuardrailRuleDto
    {
        public string? Id { get; set; }
        public string? Action { get; set; }
        public string? PathPrefix { get; set; }
        public string? Decision { get; set; }
        public string? Reason { get; set; }
    }
}
