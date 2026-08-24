namespace Grimoire.EvalRunner.Providers;

/// <summary>
/// Local dev convenience for the `capture` subcommand: fills unset provider env vars from
/// `data/.env` before <see cref="EvalProviderResolver"/> runs. Mirrors
/// Grimoire.Hub's LocalSecretsLoader parsing but is generic over variable names so it stays
/// in sync with whatever the resolver reads. Never overrides a variable already set in the
/// process environment — a real shell export or CI repository secret always wins — and is a
/// no-op when the file is absent, which is always true in CI and production.
/// </summary>
public static class LocalEnvFile
{
    public static void ApplyIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var (name, value) = (parts[0], parts[1].Trim('"'));
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
