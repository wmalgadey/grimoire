using Grimoire.Hub.Runtime.Paths;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Custom root help for the Hub CLI (018-hub-cli-commands, research.md D2/D3/D7, ADR-020):
/// prepends the <c>FigletText("Grimoire")</c> logo and appends a <c>Server options:</c>
/// section generated from <see cref="PathSwitchCatalog.All"/> — preserving 017's
/// single-source-of-truth guarantee (every switch declared once) now that
/// <c>BuildUsageText()</c> is retired.
///
/// Only the ROOT invocation (<c>command is null</c>, confirmed by Spectre's own
/// <c>IHelpProvider.Write(ICommandModel, ICommandInfo?)</c> contract) gets the extra
/// sections; per-command <c>--help</c> falls straight through to Spectre's default
/// rendering — logo-free and compact, per contracts/cli-commands.md's help contract.
/// </summary>
public sealed class HubCliHelpProvider : HelpProvider
{
    public HubCliHelpProvider(ICommandAppSettings settings)
        : base(settings)
    {
    }

    public override IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
    {
        var items = base.Write(model, command).ToList();
        if (command is not null)
        {
            // Per-command help (e.g. `submit-source --help`): logo-free, unmodified.
            return items;
        }

        items.Insert(0, new FigletText("Grimoire"));

        items.Add(new Markup("\n[bold]Server options:[/]\n"));
        var column = PathSwitchCatalog.All.Max(s => s.Name.Length) + 2;
        foreach (var pathSwitch in PathSwitchCatalog.All)
        {
            items.Add(new Markup($"  {pathSwitch.Name.PadRight(column)}{Markup.Escape(pathSwitch.Description)}\n"));
        }

        return items;
    }
}
