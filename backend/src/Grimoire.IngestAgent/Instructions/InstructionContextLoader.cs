using System.Security.Cryptography;
using System.Text;

namespace Grimoire.IngestAgent.Instructions;

public sealed record InstructionContextSnapshot(
    string ClaudePath,
    IReadOnlyList<string> SkillPaths,
    string ContentHash,
    string Status);

public sealed class InstructionContextLoader
{
    private const string DefaultSkillName = "ingest-wiki-structure";

    public async Task<InstructionContextSnapshot> LoadAsync(AgentCliOptions options, CancellationToken cancellationToken)
    {
        var instructionSetRoot = Path.Combine(options.InstructionsRoot, "backend", "src", "Grimoire.IngestAgent", "InstructionSet");
        var claudePath = Path.Combine(instructionSetRoot, "CLAUDE.md");

        var skillPaths = options.SkillPaths.Count > 0
            ? options.SkillPaths.ToList()
            : [Path.Combine(instructionSetRoot, ".claude", "skills", options.SkillName ?? DefaultSkillName, "SKILL.md")];

        var contentBuilder = new StringBuilder();
        var status = "loaded";

        if (!File.Exists(claudePath))
        {
            status = "missing";
        }
        else
        {
            contentBuilder.Append(await File.ReadAllTextAsync(claudePath, cancellationToken));
        }

        foreach (var path in skillPaths)
        {
            if (!File.Exists(path))
            {
                status = "missing";
                continue;
            }

            contentBuilder.Append(await File.ReadAllTextAsync(path, cancellationToken));
        }

        var hash = ComputeHash(contentBuilder.ToString());
        return new InstructionContextSnapshot(claudePath, skillPaths, hash, status);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
