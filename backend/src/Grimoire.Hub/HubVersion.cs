using System.Reflection;

namespace Grimoire.Hub;

/// <summary>
/// The running Hub's own version — the single place the process answers "which build am I?".
///
/// <para>
/// Read from <see cref="AssemblyInformationalVersionAttribute"/>, the assembly's most
/// descriptive version, stamped by <c>backend/Directory.Build.props</c> from GitVersion's
/// <c>$(Version)</c> (ADR-027), falling back to the assembly version if the attribute is ever
/// absent. Everything after a '+' is dropped: the build metadata GitVersion appends is a commit
/// sha, and what an operator wants named here is the release, not the build.
/// </para>
///
/// <para>
/// Two surfaces read it — the CLI's root-help logo block
/// (<see cref="Cli.HubCliHelpProvider"/>) and <c>GET /api/version</c>
/// (<see cref="HubVersionEndpoints"/>), which is how the frontend's connection indicator names
/// the server it is talking to. They share this property rather than each reflecting for
/// themselves, so an operator reading the version off the web UI and off <c>--help</c> can never
/// be told two different things.
/// </para>
///
/// <para>
/// Lives in the assembly root alongside <see cref="HubTracing"/>/<see cref="HubMetrics"/>: it is
/// cross-agent hosting infrastructure, owned by no agent (ADR-013 namespace ownership map).
/// </para>
/// </summary>
public static class HubVersion
{
    private static readonly Lazy<string> _current = new(() =>
    {
        var assembly = typeof(HubVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString()
            : informational;

        return version?.Split('+')[0] ?? string.Empty;
    });

    /// <summary>
    /// The version string, or the empty string if this assembly carries no version at all.
    /// Computed once — the attribute cannot change while the process runs.
    /// </summary>
    public static string Current => _current.Value;
}
