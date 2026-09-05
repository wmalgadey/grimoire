using Microsoft.Extensions.DependencyInjection;
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
///
/// <b>Command/settings types are constructed via <see cref="ActivatorUtilities"/>, not
/// the supplementary container's own activation</b> (018-hub-cli-commands T017 fix):
/// Spectre's <c>TypeRegistrarExtensions.RegisterDependencies</c> calls
/// <see cref="Register"/> with <c>service == implementation</c> for every command and
/// settings type in the model (<c>LintRunCommand</c>, <c>IngestSubmitSourceCommand</c>, …).
/// Handing those straight to <c>IServiceCollection.AddSingleton(Type, Type)</c> would
/// make the supplementary container responsible for resolving that command's OWN
/// constructor dependencies (e.g. <c>LintRunCoordinator</c>) — dependencies that live
/// only in the built host's real container — and
/// <c>Microsoft.Extensions.DependencyInjection</c>'s default provider throws
/// <c>InvalidOperationException</c> immediately upon activation when a constructor
/// parameter isn't registered in the SAME container, rather than returning
/// <see langword="null"/> so a fallback lookup could run. (Verified directly: every
/// non-help command invocation failed with Spectre's own
/// <c>"Could not resolve type '…'."</c> before this fix.) Command/settings types are
/// therefore tracked separately (<see cref="_activatedTypes"/>) and constructed via
/// <c>ActivatorUtilities.CreateInstance(this, type)</c> against the resolver itself
/// (which implements <see cref="IServiceProvider"/>) — so each constructor parameter is
/// resolved through the SAME fallback-then-host chain <see cref="Resolve"/> already
/// uses, letting a multi-constructor command (like <c>LintRunCommand</c>'s production
/// vs. test-seam constructors) fall back to whichever constructor's parameters are
/// actually satisfiable, exactly like <c>ActivatorUtilities</c> does for ASP.NET Core
/// controllers.
/// </summary>
internal sealed class HubCliTypeRegistrar : ITypeRegistrar
{
    private readonly Lazy<Task<IServiceProvider>> _hostServices;
    private readonly IServiceCollection _fallbackServices = new ServiceCollection();
    private readonly List<Type> _activatedTypes = [];

    public HubCliTypeRegistrar(Func<Task<IServiceProvider>> hostFactory)
    {
        _hostServices = new Lazy<Task<IServiceProvider>>(hostFactory);
    }

    public ITypeResolver Build() =>
        new HubCliTypeResolver(_hostServices, _fallbackServices.BuildServiceProvider(), _activatedTypes);

    public void Register(Type service, Type implementation)
    {
        if (service == implementation)
        {
            _activatedTypes.Add(implementation);
            return;
        }

        _fallbackServices.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation) => _fallbackServices.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) => _fallbackServices.AddSingleton(service, _ => factory());

    private sealed class HubCliTypeResolver : ITypeResolver, IServiceProvider, IServiceProviderIsService, IDisposable
    {
        private readonly Lazy<Task<IServiceProvider>> _hostServices;
        private readonly ServiceProvider _fallbackServices;
        private readonly IReadOnlyCollection<Type> _activatedTypes;

        public HubCliTypeResolver(
            Lazy<Task<IServiceProvider>> hostServices, ServiceProvider fallbackServices, IReadOnlyCollection<Type> activatedTypes)
        {
            _hostServices = hostServices;
            _fallbackServices = fallbackServices;
            _activatedTypes = activatedTypes;
        }

        public object? Resolve(Type? type) => type is null ? null : GetService(type);

        /// <summary>
        /// 018-hub-cli-commands T036 (quickstart validation finding): every
        /// <see cref="ServiceProvider"/> — including <see cref="_fallbackServices"/>, the
        /// small supplementary container — automatically answers
        /// <c>GetService(typeof(IServiceProviderIsService))</c> with ITS OWN
        /// implementation, which only reflects Spectre's minimal registrations there
        /// (never the real host's services). Left unhandled, that shadows the correct
        /// answer this class itself provides below (<c>true</c> for everything this
        /// resolver's real fallback-then-host chain can attempt) — the fallback
        /// container's own answer would win via the <c>_fallbackServices.GetService</c>
        /// check further down, BEFORE the host is ever consulted.
        /// <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(IServiceProvider,Type,object[])"/>
        /// calls <c>provider.GetService&lt;IServiceProviderIsService&gt;()</c> once per
        /// command construction to decide, per constructor parameter, whether it counts as
        /// a resolvable service or must have a default value — the shadowed (narrow)
        /// answer wrongly says "no" for every real command dependency (only registered in
        /// the host, built lazily), throwing
        /// "Constructor marked with ActivatorUtilitiesConstructorAttribute does not accept
        /// all given argument types" (or, pre-<see cref="ActivatorUtilitiesConstructorAttribute"/>,
        /// "Multiple constructors accepting all given argument types have been found") for
        /// every command with more than one constructor — invisible to every integration
        /// test because they all construct commands directly, bypassing
        /// <see cref="ActivatorUtilities"/> entirely; only a real out-of-process invocation
        /// (quickstart.md, T040) exercises this path. Special-casing the request here (an
        /// explicit reference-equality check ahead of the fallback-container check) routes
        /// it to this class's own <see cref="IsService"/> instead.
        /// </summary>
        public bool IsService(Type serviceType) => true;

        /// <summary>
        /// Also this resolver's own <see cref="IServiceProvider"/> implementation, so
        /// <c>ActivatorUtilities.CreateInstance(this, …)</c> below can recurse through
        /// the identical fallback-then-host chain for a command/settings type's own
        /// constructor parameters.
        /// </summary>
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProviderIsService))
            {
                return this;
            }

            var fromFallback = _fallbackServices.GetService(serviceType);
            if (fromFallback is not null)
            {
                return fromFallback;
            }

            if (_activatedTypes.Contains(serviceType))
            {
                return ActivatorUtilities.CreateInstance(this, serviceType);
            }

            // Only reached for a type Spectre's own registrations don't satisfy — i.e. a
            // real command's constructor dependency. Safe to block: console apps have no
            // SynchronizationContext, and this runs once per process (Lazy caches the
            // completed Task on every later call).
            var hostServices = _hostServices.Value.GetAwaiter().GetResult();
            return hostServices.GetService(serviceType);
        }

        // The host's IServiceProvider is owned and disposed by HubCliApp (via
        // WebApplication.DisposeAsync, D8's OTLP-flush obligation) — only the
        // supplementary fallback container belongs to this resolver.
        public void Dispose() => _fallbackServices.Dispose();
    }
}
