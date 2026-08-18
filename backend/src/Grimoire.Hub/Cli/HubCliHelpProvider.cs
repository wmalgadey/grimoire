using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Custom root help for the Hub CLI (018-hub-cli-commands, research.md D2/D3/D7, ADR-020):
/// a centered <c>FigletText</c> "Grimoire" logo above Spectre's generated help, and
/// operational guidance below it. Nothing in between is hand-rendered.
///
/// The path switches used to be one of those hand-rendered pieces: Spectre builds the
/// <c>OPTIONS:</c> section from <c>command?.Parameters</c>, and without a default command the
/// root invocation passes <see langword="null"/> there, so <c>--data-dir</c> and friends could
/// only be shown as a hand-padded block spliced in by searching the rendered output for
/// <c>"COMMANDS:"</c>. <see cref="HubRootCommand"/> — the default command, deriving from
/// <see cref="HubPathSettings"/> — removed the need: the root help now receives its parameters
/// and renders them in the real options grid, aligned with <c>-h, --help</c> and wrapped by
/// Spectre. Same for the tagline, which is the default command's description
/// (<see cref="HubCliApp"/>) and therefore Spectre's own <c>DESCRIPTION:</c> section.
///
/// Only the ROOT invocation gets the logo and the guidance. With a default command registered,
/// "root" is no longer <c>command is null</c> — Spectre passes the default command's
/// <see cref="ICommandInfo"/> — so the check is <c>IsDefaultCommand</c>. Per-command
/// <c>--help</c> falls straight through to Spectre's default rendering — logo-free and
/// compact, per contracts/cli-commands.md's help contract.
/// </summary>
public sealed class HubCliHelpProvider : HelpProvider
{
    /// <summary>
    /// The "DOS Rebel" FIGfont, embedded in this assembly (see the
    /// <c>EmbeddedResource</c> item in Grimoire.Hub.csproj) rather than loaded from disk:
    /// root <c>--help</c> must render before any path resolution has happened, so it can
    /// have no on-disk dependency. Spectre ships exactly one built-in font, so a custom
    /// look requires supplying the <c>.flf</c> ourselves.
    ///
    /// Loaded once and cached — <see cref="FigletFont"/> is immutable and parsing the
    /// ~12 KB file on every help invocation would be pointless work.
    ///
    /// Note that Spectre's FIGfont parser does not accept every <c>.flf</c> in the
    /// wild (the widely distributed <c>big.flf</c>, for one, throws "Unknown index for
    /// FIGlet character" on load). Any replacement font MUST be verified by actually
    /// rendering it, not merely by dropping the file in.
    /// </summary>
    private static readonly Lazy<FigletFont> LogoFont = new(() =>
    {
        const string ResourceName = "Grimoire.Hub.Cli.Fonts.dos-rebel.flf";
        using var stream = typeof(HubCliHelpProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Figlet font '{ResourceName}' is missing from the Grimoire.Hub assembly.");
        return FigletFont.Load(stream);
    });

    /// <summary>
    /// The Hub's own version, shown under the logo — <see cref="HubVersion.Current"/>, the same
    /// string <c>GET /api/version</c> serves to the frontend's connection indicator, so the two
    /// surfaces can never disagree about which build is running.
    ///
    /// Deliberately read from there rather than through Spectre's own
    /// <c>ICommandModel.ApplicationVersion</c>: populating that (via
    /// <c>SetApplicationVersion</c>) would also add a <c>-v, --version</c> option to every
    /// help screen — a separate decision from showing the version in the logo block.
    /// </summary>
    private static string LogoVersion => HubVersion.Current;

    /// <summary>
    /// The build configuration, but only when it is NOT a release build — worth flagging in
    /// the help header so an operator can tell at a glance that this binary is not one.
    /// Read from <see cref="AssemblyConfigurationAttribute"/>, which the SDK stamps from
    /// <c>$(Configuration)</c>.
    ///
    /// <see langword="null"/> both for Release and for a missing attribute: an unknown
    /// configuration is not evidence of a debug build, and claiming one would be worse than
    /// saying nothing.
    /// </summary>
    private static readonly Lazy<string?> NonReleaseConfiguration = new(() =>
    {
        var configuration = typeof(HubCliHelpProvider).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;

        return string.IsNullOrWhiteSpace(configuration)
            || string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase)
                ? null
                : configuration;
    });

    public HubCliHelpProvider(ICommandAppSettings settings)
        : base(settings)
    {
    }

    public override IEnumerable<IRenderable> GetHeader(ICommandModel model, ICommandInfo? command)
    {
        if (!IsRootHelp(command))
        {
            yield break;
        }

        yield return new Align(new FigletText(LogoFont.Value, "Grimoire"), HorizontalAlignment.Center);

        // FigletText's last line carries no trailing line break, so without these the next
        // block would start on the logo's own last line.
        yield return new Markup("\n");
        if (BuildVersionLine() is { } versionLine)
        {
            yield return new Align(new Markup(versionLine), HorizontalAlignment.Center);
            yield return new Markup("\n\n");
        }
    }

    public override IEnumerable<IRenderable> GetFooter(ICommandModel model, ICommandInfo? command)
    {
        if (!IsRootHelp(command))
        {
            yield break;
        }

        yield return new Markup("\n[bold]How to start the server:[/]\n");
        yield return new Markup("  • [deepskyblue1]Grimoire.Hub[/] with no command (runs the HTTP server until stopped)\n");
        yield return new Markup("  • The options above apply to every command and override data, agent runtime, or wiki roots\n");
    }

    /// <summary>
    /// The line under the logo: the Hub's version, plus a marker when this is not a release
    /// build. Returns <see langword="null"/> when neither is known, so the header stays the
    /// bare logo rather than rendering an empty line.
    /// </summary>
    private static string? BuildVersionLine()
    {
        var parts = new List<string>(capacity: 2);

        if (LogoVersion.Length > 0)
        {
            parts.Add($"[italic]Version {Markup.Escape(LogoVersion)}[/]");
        }

        if (NonReleaseConfiguration.Value is { } configuration)
        {
            parts.Add($"[italic yellow]{Markup.Escape(configuration)} build[/]");
        }

        return parts.Count == 0 ? null : string.Join(" [grey]·[/] ", parts);
    }

    /// <summary>
    /// True for the root invocation. Spectre hands the default command's
    /// <see cref="ICommandInfo"/> to the root help (<c>CommandExecutor</c> renders help for
    /// the parsed leaf, which is <see cref="HubRootCommand"/> when no command was named); the
    /// <see langword="null"/> case is kept because Spectre still uses it when no command tree
    /// could be parsed at all.
    /// </summary>
    private static bool IsRootHelp(ICommandInfo? command) => command is null || command.IsDefaultCommand;
}
