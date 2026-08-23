using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Grimoire.ArchTests;

/// <summary>
/// Structural rule for minimal-API handler DI parameters (issue #59). ASP.NET Core's
/// <c>RequestDelegateFactory</c> infers an unannotated complex-type handler parameter as
/// <c>[FromBody]</c> whenever it cannot statically confirm the type's DI-service status —
/// a confirmation that depends on exactly which host composes the container, so the same
/// handler can build cleanly in production and throw in a leaner test host (or vice versa).
/// On a GET/DELETE endpoint that throw happens eagerly, at route-matcher-build time, for
/// every host that maps the endpoint group — not only for requests that reach the handler.
/// #56/#57 hit exactly this: <c>GetDefaultsAsync</c> gained a <c>ResolvedGrimoirePaths</c>
/// parameter without <c>[FromServices]</c> and broke 12 tests across 5 unrelated fixtures.
///
/// Rather than relying on the inference succeeding by accident, this rule requires every
/// minimal-API handler parameter to say what it is: a recognized route/query/framework
/// type (<see cref="_exemptParameterTypeFullNames"/>), a request-body DTO named by the
/// project's own <c>*Request</c> convention, or an explicit ASP.NET Core binding attribute
/// (<c>[FromServices]</c> most commonly). See
/// docs/conventions/minimal-api-service-parameters.md for the convention this enforces.
///
/// Handler discovery: every method reached by an <c>ldftn</c> instruction inside a
/// <c>Map*(RouteGroupBuilder, ...)</c> / <c>Map*(WebApplication, ...)</c> mapping method in
/// <c>Grimoire.Hub</c> — i.e. the actual delegate registered with
/// <c>MapGet</c>/<c>MapPost</c>/... — rather than a naming heuristic over handler methods
/// themselves, so a private helper that merely looks like a handler is never mistaken for
/// one and a handler registered under an unconventional name is never missed.
///
/// Red/Green probed 2026-08-23: a temporary unannotated
/// <c>OperationalStateRepository stateRepository</c> parameter added to
/// <c>LintSubmissionEndpoints.GetLatestAsync</c> was confirmed to fail this test, then
/// removed.
/// </summary>
public class MinimalApiServiceParameterRuleTests
{
    private static readonly string[] _bindingAttributeFullNames =
    [
        "Microsoft.AspNetCore.Mvc.FromServicesAttribute",
        "Microsoft.AspNetCore.Mvc.FromBodyAttribute",
        "Microsoft.AspNetCore.Mvc.FromRouteAttribute",
        "Microsoft.AspNetCore.Mvc.FromQueryAttribute",
        "Microsoft.AspNetCore.Mvc.FromHeaderAttribute",
        "Microsoft.AspNetCore.Mvc.FromFormAttribute",
        "Microsoft.AspNetCore.Http.AsParametersAttribute",
    ];

    /// <summary>
    /// Types the minimal-API pipeline binds from the route/query/framework surface, never
    /// from the DI container — mirrored in
    /// docs/conventions/minimal-api-service-parameters.md. Value types are matched by their
    /// unwrapped (non-nullable) full name.
    /// </summary>
    private static readonly string[] _exemptParameterTypeFullNames =
    [
        "System.String",
        "System.Boolean",
        "System.Int32",
        "System.Int64",
        "System.Guid",
        "System.Threading.CancellationToken",
        "Microsoft.AspNetCore.Http.HttpRequest",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.AspNetCore.Http.HttpResponse",
        "Microsoft.AspNetCore.Http.IFormFile",
        "Microsoft.AspNetCore.Http.IFormFileCollection",
        "Microsoft.Extensions.Logging.ILoggerFactory",
        "System.Security.Claims.ClaimsPrincipal",
        "System.IO.Stream",
    ];

    private static readonly string[] _mappingMethodFirstParameterTypeNames = ["RouteGroupBuilder", "WebApplication"];

    [Fact]
    public void EndpointHandlers_ServiceTypedParameters_CarryExplicitBindingAttribute()
    {
        var hubAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Grimoire.Hub.dll");
        using var assembly = AssemblyDefinition.ReadAssembly(hubAssemblyPath);

        var handlers = FindEndpointHandlers(assembly).ToList();

        // An empty scan would pass vacuously — the Hub maps a known, non-trivial set of
        // route groups; if this ever comes back empty, the mapping-method detection below
        // is broken, not the codebase.
        Assert.NotEmpty(handlers);

        var violations = new List<string>();
        foreach (var handler in handlers)
        {
            foreach (var parameter in handler.Parameters)
            {
                if (IsExempt(parameter) || HasBindingAttribute(parameter))
                    continue;

                violations.Add(
                    $"{handler.DeclaringType.FullName}.{handler.Name}({parameter.ParameterType.FullName} {parameter.Name})");
            }
        }

        Assert.True(violations.Count == 0,
            "Minimal-API handler parameters below are DI-service types (not route/query/body/" +
            "well-known-framework) without an explicit binding attribute. Without [FromServices], " +
            "ASP.NET Core's service inference can silently land on [FromBody] instead depending on " +
            "which host composes the DI container, throwing at route-matcher-build time for " +
            "GET/DELETE endpoints (issue #59). Add [FromServices] (or the appropriate explicit " +
            "attribute):\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// Every method reached by an <c>ldftn</c> from a route-group/app mapping method — the
    /// exact set of delegates <c>MapGet</c>/<c>MapPost</c>/... register as handlers.
    /// </summary>
    private static IEnumerable<MethodDefinition> FindEndpointHandlers(AssemblyDefinition assembly)
    {
        var handlers = new HashSet<MethodDefinition>();

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (!IsMappingMethod(method) || !method.HasBody)
                        continue;

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode == OpCodes.Ldftn &&
                            instruction.Operand is MethodReference targetRef &&
                            targetRef.Resolve() is { } target)
                        {
                            handlers.Add(target);
                        }
                    }
                }
            }
        }

        return handlers;
    }

    private static bool IsMappingMethod(MethodDefinition method)
        => method.IsStatic &&
           method.Name.StartsWith("Map", StringComparison.Ordinal) &&
           method.Parameters.Count > 0 &&
           _mappingMethodFirstParameterTypeNames.Contains(method.Parameters[0].ParameterType.Name);

    private static bool IsExempt(ParameterDefinition parameter)
    {
        var type = parameter.ParameterType;

        var unwrapped = type is GenericInstanceType { ElementType.FullName: "System.Nullable`1" } nullable
            ? nullable.GenericArguments[0]
            : type;

        if (_exemptParameterTypeFullNames.Contains(unwrapped.FullName))
            return true;

        // Project convention (docs/conventions/minimal-api-service-parameters.md): a
        // request-body DTO is named "...Request" and lives beside its endpoint file — it is
        // never DI-registered, so the pipeline's implicit [FromBody] inference is correct
        // for it by construction.
        return unwrapped.Name.EndsWith("Request", StringComparison.Ordinal);
    }

    private static bool HasBindingAttribute(ParameterDefinition parameter)
        => parameter.CustomAttributes.Any(a => _bindingAttributeFullNames.Contains(a.AttributeType.FullName));
}
