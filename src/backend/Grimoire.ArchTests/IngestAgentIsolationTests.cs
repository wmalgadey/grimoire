using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Grimoire.ArchTests;

public class IngestAgentIsolationTests
{
    [Fact]
    public void T000_IngestAgent_CannotImportFromGrimoireApi()
    {
        var ingestAgentDir = Path.Combine(Directory.GetCurrentDirectory(), "../agents/ingest");

        if (!Directory.Exists(ingestAgentDir))
        {
            return;
        }

        var csFiles = Directory.GetFiles(ingestAgentDir, "*.cs", SearchOption.AllDirectories);
        var violatingFiles = new List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains("using Grimoire.Api") ||
                content.Contains("using Grimoire.Core") ||
                content.Contains("using Grimoire.Infrastructure"))
            {
                violatingFiles.Add(file);
            }
        }

        Assert.Empty(violatingFiles);
    }

    [Fact]
    public void T001_NoLiteralAnthropicApiKeyStrings()
    {
        var srcDir = Path.Combine(Directory.GetCurrentDirectory(), "../../");
        var allSourceFiles = Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".ts") || f.EndsWith(".svelte"))
            .ToList();

        var violatingFiles = new List<(string File, int Line)>();
        var apiKeyPattern = new Regex(@"ANTHROPIC_API_KEY|anthropic_api_key", RegexOptions.IgnoreCase);

        foreach (var file in allSourceFiles)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (apiKeyPattern.IsMatch(lines[i]))
                    {
                        violatingFiles.Add((file, i + 1));
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        Assert.Empty(violatingFiles);
    }
}
