using System.Security.Cryptography;
using System.Text;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>One instruction file loaded into the agent's system prompt (FR-012).</summary>
public sealed record InstructionFile(string Path, string Content, string Sha256);

/// <summary>The full loaded instruction set for a run.</summary>
public sealed record LoadedInstructionSet(IReadOnlyList<InstructionFile> Files)
{
    /// <summary>
    /// Concatenates all instruction file contents for inclusion in the system prompt.
    /// Each file is separated by a header so the model can reason about file boundaries.
    /// </summary>
    public string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        foreach (var file in Files)
        {
            sb.AppendLine($"<!-- {file.Path} -->");
            sb.AppendLine(file.Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Failure record returned by <see cref="InstructionSetLoader"/>.</summary>
public sealed record InstructionSetLoadFailure(string Reason);

/// <summary>
/// Loads the instruction set (CLAUDE.md + skills/*/SKILL.md) verbatim from the
/// instructions directory. Fail-closed: missing, unreadable, or effectively empty
/// CLAUDE.md ⇒ load failure result (FR-003, SC-003).
/// </summary>
public sealed class InstructionSetLoader
{
    /// <summary>
    /// Loads the instruction set from <paramref name="instructionsDir"/>.
    /// Returns either a loaded set or a human-readable failure reason.
    /// </summary>
    public async Task<OneOf<LoadedInstructionSet, InstructionSetLoadFailure>> LoadAsync(
        string instructionsDir,
        CancellationToken cancellationToken)
    {
        var claudePath = Path.Combine(instructionsDir, "CLAUDE.md");

        if (!File.Exists(claudePath))
        {
            return new InstructionSetLoadFailure(
                $"CLAUDE.md not found at '{claudePath}'. Cannot start a run without agent operating rules.");
        }

        string claudeContent;
        try
        {
            claudeContent = await File.ReadAllTextAsync(claudePath, Encoding.UTF8, cancellationToken);
        }
        catch (Exception ex)
        {
            return new InstructionSetLoadFailure(
                $"Cannot read CLAUDE.md at '{claudePath}': {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(claudeContent))
        {
            return new InstructionSetLoadFailure(
                $"CLAUDE.md at '{claudePath}' is empty or whitespace-only. Cannot start a run without agent operating rules.");
        }

        var files = new List<InstructionFile>
        {
            ToInstructionFile(claudePath, claudeContent),
        };

        // Load every skills/*/SKILL.md present in the instruction directory.
        var skillsDir = Path.Combine(instructionsDir, "skills");
        if (Directory.Exists(skillsDir))
        {
            var skillFiles = Directory
                .GetDirectories(skillsDir)
                .Select(d => Path.Combine(d, "SKILL.md"))
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            foreach (var skillPath in skillFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(skillPath, Encoding.UTF8, cancellationToken);
                    files.Add(ToInstructionFile(skillPath, content));
                }
                catch (Exception ex)
                {
                    return new InstructionSetLoadFailure(
                        $"Cannot read skill file '{skillPath}': {ex.Message}");
                }
            }
        }

        return new LoadedInstructionSet(files);
    }

    private static InstructionFile ToInstructionFile(string path, string content)
    {
        var sha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new InstructionFile(path, content, sha256);
    }
}
