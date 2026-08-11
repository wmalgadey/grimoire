using System.Globalization;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Unsafe;

namespace Grimoire.Hub.Cli;

/// <summary>
/// The Hub's single entry point (018-hub-cli-commands, ADR-020): builds the Hub's one
/// composition (<see cref="HubHostComposition.BuildAsync"/>), wires a Spectre
/// <see cref="CommandApp{TDefaultCommand}"/> over it via <see cref="HubCliTypeRegistrar"/> so
/// every command resolves the same coordinators/services the HTTP endpoints use, and
/// registers every catalog entry that already has a command class
/// (<see cref="HubCliCommands.All"/> — later phases add entries by giving them a non-null
/// <c>CommandType</c>; no change needed here).
///
/// <b>Every invocation goes through here</b>, including starting the web server:
/// <see cref="HubRootCommand"/> is the default command, so <c>Program.cs</c> no longer needs
/// a hand-written gate deciding between "CLI" and "web host". The named commands remain
/// one-shot invocations that never bind a port; only the default command runs the server.
///
/// Host construction is deferred until a command is actually about to execute (see
/// <see cref="HubCliTypeRegistrar"/>) — <c>--help</c>, an unknown command name, and a
/// settings-validation failure all resolve without ever building the composition, so
/// FR-011's "--help wins before any startup side effect" guarantee holds even when
/// combined with a bogus path switch (017 precedent). When a command genuinely runs, the
/// built host is disposed before <see cref="RunAsync"/> returns so OpenTelemetry export
/// flushes before the process exits (research.md D8, obligation 1) — the built host is
/// otherwise never started (<c>app.Run()</c>/<c>app.Start()</c> is never called), so
/// nothing but explicit disposal would ever release it.
///
/// <b>Ctrl-C / SIGINT (018-hub-cli-commands T033, FR-016)</b>: this is the first Hub CLI
/// code to wire an OS interrupt signal to a <see cref="CancellationToken"/> — no prior
/// phase needed one. <see cref="Console.CancelKeyPress"/> is subscribed once here, around
/// the whole <c>RunAsync</c> call: the handler sets <c>e.Cancel = true</c> (suppressing
/// the runtime's default "kill the process immediately" behavior) and cancels a
/// process-lifetime <see cref="CancellationTokenSource"/> whose token flows into
/// <see cref="CommandApp.RunAsync(IEnumerable{string}, CancellationToken)"/> and from
/// there into every command's own <c>ExecuteAsync</c> — <see cref="QueryCommand"/> is the
/// only command that currently acts on it (its own blocking wait loop calls
/// <c>QueryRunCoordinator.InterruptAsync</c> and maps to <see cref="CliExitCode.Cancelled"/>
/// itself); every other command either finishes fast enough that Ctrl-C during its own
/// (in-process) wait is not a distinct contract row, or would unwind via this class's
/// existing <see cref="OperationCanceledException"/> catch below as a fallback. Per
/// research D9, this glue is intentionally thin — the actually-tested behavior is
/// <see cref="QueryCommand"/>'s reaction to a cancelled token, exercised directly with a
/// supplied <see cref="CancellationToken"/> in integration tests, not by sending a real
/// SIGINT to a spawned process.
/// </summary>
public static class HubCliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        WebApplication? builtApp = null;
        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
        {
            // Suppress the default "terminate immediately" behavior: the running
            // command's own cancellation-token handling (QueryCommand's interrupt call)
            // must complete before the process actually exits (FR-015/FR-016).
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancelKeyPress;

        var registrar = new HubCliTypeRegistrar(async () =>
        {
            builtApp = await HubHostComposition.BuildAsync(args);
            return builtApp.Services;
        });

        // The server path, handed to HubRootCommand as an instance in the registrar's
        // supplementary container (see HubServerHost): resolving it must not build the host,
        // so the composition happens here, inside the closure, only once the command actually
        // runs. Assigning the same `builtApp` local the registrar's factory uses keeps this
        // path covered by the finally-block's DisposeAsync (D8's OTLP-flush obligation) —
        // whichever of the two ran first.
        registrar.RegisterInstance(typeof(HubServerHost), new HubServerHost(async cancellationToken =>
        {
            builtApp ??= await HubHostComposition.BuildAsync(args);
            builtApp.MapGrimoireEndpoints();
            // WebApplication.RunAsync(string?) shadows the IHost extension that takes a
            // token, so the cast is what lets this class's own cancellation reach the host:
            // WaitForShutdownAsync registers StopApplication on the token. The host's
            // ConsoleLifetime registers for the same OS signals independently, and both
            // routes end in StopApplication — they are additive, not competing.
            await ((IHost)builtApp).RunAsync(cancellationToken);
            return (int)CliExitCode.Success;
        }));

        var cliApp = new CommandApp<HubRootCommand>(registrar);
        // Rendered by Spectre as the root help's DESCRIPTION section — the tagline is part of
        // the generated help now, not a hand-placed Markup line in HubCliHelpProvider.
        cliApp.WithDescription("Grimoire is an AI harness that keeps a wiki current through supervised agents.");
        cliApp.Configure(config =>
        {
            config.SetApplicationName("Grimoire.Hub");
            // Usage/validation/unknown-command failures become C# exceptions we catch and
            // map to CliExitCode.UsageError below, instead of Spectre's own default (a
            // fixed non-zero code untied to the ADR-020 exit-code convention, research D5).
            config.PropagateExceptions();
            // Deterministic, script-parseable English text regardless of the host
            // machine's locale (SC-002/SC-003 message-matching guarantees).
            config.SetApplicationCulture(CultureInfo.InvariantCulture);
            config.SetHelpProvider(new HubCliHelpProvider(config.Settings));

            foreach (var entry in HubCliCommands.All)
            {
                if (entry.CommandType is null)
                {
                    // Catalog entry for a command whose story hasn't landed yet (T007):
                    // still a valid Program.cs dispatch target and root-help listing, just
                    // not yet registered with Spectre.
                    continue;
                }

                config.SafetyOff().AddCommand(entry.Name, entry.CommandType).WithDescription(entry.Description);
            }
        });

        try
        {
            return await cliApp.RunAsync(args, cts.Token);
        }
        catch (CommandAppException ex)
        {
            // ADR-022 quickstart validation finding (Scenario 3/6): Spectre resolves a
            // command's constructor dependencies (and therefore first touches
            // HubHostComposition.BuildAsync, which resolves and validates every runtime
            // path) lazily, inside its own type-resolution machinery — a
            // GrimoirePathValidationException/GrimoirePathConfigurationMissingException
            // thrown there arrives here wrapped in a generic Spectre
            // CommandRuntimeException ("Could not resolve type '<Command>'.") that hides
            // the actual, actionable path-configuration message an operator needs.
            // Unwrap it so a misconfigured --data-dir/--agent-dir/--wiki-dir (or a missing
            // appsettings.json root) reports the same message a server-mode start does,
            // instead of a generic DI failure.
            var pathFailure = UnwrapPathResolutionFailure(ex);
            if (pathFailure is not null)
            {
                Console.Error.WriteLine(pathFailure.Message);
                return (int)CliExitCode.OperationFailed;
            }

            Console.Error.WriteLine(ex.Message);
            return (int)CliExitCode.UsageError;
        }
        catch (OperationCanceledException)
        {
            return (int)CliExitCode.Cancelled;
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
            if (builtApp is not null)
            {
                await builtApp.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Walks <paramref name="exception"/>'s <see cref="Exception.InnerException"/> chain
    /// for a <see cref="Runtime.Paths.GrimoirePathValidationException"/> or
    /// <see cref="Runtime.Paths.GrimoirePathConfigurationMissingException"/> — the two
    /// exception types <see cref="Runtime.Paths.GrimoirePathResolver.Resolve"/> throws.
    /// Spectre's type-resolution machinery is the only place these can end up wrapped
    /// (see the call site above); the exception's own message already names the
    /// location/configuration file and is otherwise unrelated to Spectre's usage-error
    /// vocabulary, so it is surfaced verbatim rather than folded into a usage error.
    /// </summary>
    private static Exception? UnwrapPathResolutionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Runtime.Paths.GrimoirePathValidationException or Runtime.Paths.GrimoirePathConfigurationMissingException)
            {
                return current;
            }
        }

        return null;
    }
}
