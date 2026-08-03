using Spectre.Console.Cli;

namespace Grimoire.Hub.Cli;

/// <summary>
/// Bridges Spectre.Console.Cli's DI hook to the Hub's own composition
/// (018-hub-cli-commands, ADR-020 D1/D3: one composition point, <see cref="HubHostComposition.BuildAsync"/>).
/// Command classes are resolved from the built <see cref="WebApplication"/>'s
/// <see cref="IServiceProvider"/> — the same coordinators/services the HTTP endpoints
/// resolve — so a command constructor asking for e.g. <c>LintRunCoordinator</c> gets the
/// identical singleton instance the (never-mapped) HTTP handler would have used.
///
/// <b>Host construction is deferred to the first resolution</b> (<paramref name="hostFactory"/>
/// in the constructor, wrapped in a <see cref="Lazy{T}"/>): Spectre only calls
/// <see cref="ITypeResolver.Resolve"/> once it is about to actually construct the matched
/// command for execution — never for <c>--help</c> rendering, an unknown-command parse
/// failure, or a settings-validation failure. Building the full composition eagerly (path
/// resolution included — <c>GrimoirePathResolver.Resolve</c> requires every configured
/// path to exist) would defeat FR-011's "--help wins before any startup side effect"
/// guarantee the moment a bogus path switch is combined with <c>--help</c> (017
/// precedent, <c>HubHelpUsageTests.Help_CombinedWithBogusBaseDir_StillWinsAndExitsZero</c>).
/// Deferring means the host is built if and only if a real command is about to run.
///
/// Spectre also performs a handful of its own internal registrations while configuring
/// the <c>CommandApp</c> — notably <c>SetHelpProvider(IHelpProvider)</c>, which registers
/// the instance so <c>IEnumerable&lt;IHelpProvider&gt;</c> resolves it, and Spectre queries
/// that on <i>every</i> invocation, including <c>--help</c> and validation failures that
/// never reach a command. Those registrations go into a small supplementary container
/// built once in <see cref="Build"/>. Resolution therefore checks the supplementary
/// container <b>first</b> and only reaches into the (deferred, build-triggering) host
/// provider as a fallback — the reverse of a typical composite resolver — so framework
/// queries like the one above never force the full composition just to render help.
/// </summary>
internal sealed class HubCliTypeRegistrar : ITypeRegistrar
{
    private readonly Lazy<Task<IServiceProvider>> _hostServices;
    private readonly IServiceCollection _fallbackServices = new ServiceCollection();

    public HubCliTypeRegistrar(Func<Task<IServiceProvider>> hostFactory)
    {
        _hostServices = new Lazy<Task<IServiceProvider>>(hostFactory);
    }

    public ITypeResolver Build() => new HubCliTypeResolver(_hostServices, _fallbackServices.BuildServiceProvider());

    public void Register(Type service, Type implementation) => _fallbackServices.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => _fallbackServices.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => _fallbackServices.AddSingleton(service, _ => factory());

    private sealed class HubCliTypeResolver : ITypeResolver, IDisposable
    {
        private readonly Lazy<Task<IServiceProvider>> _hostServices;
        private readonly ServiceProvider _fallbackServices;

        public HubCliTypeResolver(Lazy<Task<IServiceProvider>> hostServices, ServiceProvider fallbackServices)
        {
            _hostServices = hostServices;
            _fallbackServices = fallbackServices;
        }

        public object? Resolve(Type? type)
        {
            if (type is null)
            {
                return null;
            }

            var fromFallback = _fallbackServices.GetService(type);
            if (fromFallback is not null)
            {
                return fromFallback;
            }

            // Only reached for a type Spectre's own registrations don't satisfy — i.e. a
            // real command's constructor dependency. Safe to block: console apps have no
            // SynchronizationContext, and this runs once per process (Lazy caches the
            // completed Task on every later call).
            var hostServices = _hostServices.Value.GetAwaiter().GetResult();
            return hostServices.GetService(type);
        }

        // The host's IServiceProvider is owned and disposed by HubCliApp (via
        // WebApplication.DisposeAsync, D8's OTLP-flush obligation) — only the
        // supplementary fallback container belongs to this resolver.
        public void Dispose() => _fallbackServices.Dispose();
    }
}
