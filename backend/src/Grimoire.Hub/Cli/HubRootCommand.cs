using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// The Hub's default command: running <c>Grimoire.Hub</c> with no command name starts the web
/// server. Every other command in <see cref="HubCliCommands.All"/> is a one-shot invocation
/// that exits when its work is done; serving HTTP is what happens when no such command is
/// named, so it belongs here rather than in a hand-written dispatch gate ahead of Spectre.
///
/// Registering a default command has a second, deliberate effect: Spectre's
/// <c>HelpProvider</c> builds the <c>OPTIONS:</c> section from <c>command?.Parameters</c>, and
/// the root help invocation passes the DEFAULT command's parameters where it would otherwise
/// pass <see langword="null"/>. Deriving from <see cref="HubPathSettings"/> therefore makes
/// <c>--data-dir</c>/<c>--agent-dir</c>/<c>--wiki-dir</c> render in the real options grid of
/// the root help — aligned with <c>-h, --help</c>, styled and wrapped by Spectre — replacing
/// the hand-padded block this class's introduction deleted from
/// <see cref="HubCliHelpProvider"/>.
///
/// Unlike the other commands this one takes no Hub services: <see cref="HubServerHost"/> comes
/// from <see cref="HubCliTypeRegistrar"/>'s supplementary container, so constructing this
/// command never triggers the deferred host build. The composition happens inside
/// <see cref="HubServerHost.RunAsync"/>, i.e. only once the server is genuinely starting.
/// </summary>
internal sealed class HubRootCommand : AsyncCommand<HubPathSettings>
{
    private readonly HubServerHost _server;

    public HubRootCommand(HubServerHost server) => _server = server;

    /// <summary>
    /// The path switches bound into <paramref name="settings"/> are deliberately not read:
    /// exactly as documented on <see cref="HubPathSettings"/>, they exist so Spectre's parser
    /// accepts them and its help lists them, while the values that actually govern path
    /// resolution flow through <c>HubHostComposition</c>'s configuration composition. Reading
    /// them here would create the second source of truth that class exists to avoid.
    /// </summary>
    protected override Task<int> ExecuteAsync(
        CommandContext context, HubPathSettings settings, CancellationToken cancellationToken)
        => _server.RunAsync(cancellationToken);
}
