namespace Grimoire.Hub.Cli;

/// <summary>
/// The web host's run-until-shutdown behavior, handed to <see cref="HubRootCommand"/> as a
/// closure rather than resolved from the Hub's own container.
///
/// The indirection is load-bearing, not ceremony. <see cref="HubRootCommand"/> needs the built
/// <see cref="Microsoft.AspNetCore.Builder.WebApplication"/> itself — not just its services —
/// to map endpoints and run it, and that object is created by
/// <see cref="HubHostComposition.BuildAsync"/> outside any container. Registering this holder
/// as an instance on <see cref="HubCliTypeRegistrar"/> puts it in the small supplementary
/// container, so constructing <see cref="HubRootCommand"/> resolves it without ever reaching
/// into the (deferred, build-triggering) host provider: the composition is built inside
/// <see cref="RunAsync"/>, at the moment the server actually starts, and never on the
/// <c>--help</c> path.
/// </summary>
internal sealed class HubServerHost
{
    private readonly Func<CancellationToken, Task<int>> _run;

    public HubServerHost(Func<CancellationToken, Task<int>> run) => _run = run;

    public Task<int> RunAsync(CancellationToken cancellationToken) => _run(cancellationToken);
}
