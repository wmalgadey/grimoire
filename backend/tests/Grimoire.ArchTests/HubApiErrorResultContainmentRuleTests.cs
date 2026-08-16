using Mono.Cecil;

namespace Grimoire.ArchTests;

/// <summary>
/// Boundary Rule BR1 for ADR-026 (024-api-error-presentation, T001–T003): every HTTP failure
/// the Hub returns carries one envelope, and the only way to guarantee that is to make
/// <c>Grimoire.Hub.ApiErrors</c> the sole producer of error results. An endpoint that reaches
/// for <c>Results.Conflict(new { reason = ... })</c> directly is how the two ad-hoc shapes this
/// feature removes came to exist in the first place — twenty inline anonymous objects across five
/// endpoint namespaces, no two of them agreeing.
///
/// This is a Boundary Rule rather than a Feature-Scoped Invariant (Constitution Principle III):
/// it states a durable direction — error results flow out through one namespace — that holds
/// however any endpoint family's surface grows, and every future endpoint inherits it without
/// anyone editing this file.
///
/// <para>
/// <b>Why a baseline.</b> Unlike the usual Phase 0 rule, this one is not vacuous on arrival: it is
/// red against the codebase it is introduced into, because every endpoint file violates it until
/// migrated. A rule written after the migration would not have guarded the migration; a rule
/// written before it, without a baseline, would leave CI red for the whole of Phase 3. The
/// remove-only baseline below resolves that the same way ADR-013's N1 legacy-rename ratchet did:
/// the rule is live from the first commit and no *new* violation can enter any namespace, while
/// the listed namespaces are permitted to still contain their pre-migration call sites. Each
/// migration task removes its own entry; T060 asserts the list is empty and deletes the mechanism,
/// after which the rule enforces BR1 outright with no suppression available.
/// </para>
///
/// <para>
/// <b>Red/Green probe (T003).</b> Performed on 2026-08-16 before any feature code existed: a
/// scratch type in <c>Grimoire.Hub.Realtime</c> (a namespace deliberately outside the baseline)
/// calling <c>Results.Conflict(...)</c> turned
/// <see cref="ErrorResultFactories_AreConfinedToTheApiErrorsNamespace"/> red, naming the scratch
/// call site in the failure message; removing the scratch type returned the rule to green. The
/// guard is live, not vacuous.
/// </para>
/// </summary>
public class HubApiErrorResultContainmentRuleTests
{
    private const string ApiErrorsNamespace = "Grimoire.Hub.ApiErrors";

    /// <summary>
    /// The error-producing factories on <c>Results</c>/<c>TypedResults</c>. Success factories
    /// (<c>Ok</c>, <c>Accepted</c>, <c>Created</c>, <c>File</c>, <c>NoContent</c>, <c>Stream</c>,
    /// <c>Text</c>) are deliberately absent — they are not failures and endpoints keep using them
    /// directly.
    ///
    /// <c>Json</c> and <c>StatusCode</c> are dual-use in principle, and are included because in
    /// this codebase they are not dual-use in practice: a survey of every call site found all five
    /// <c>Results.Json</c> uses to be error paths (a 415 validation result, two 500s for a missing
    /// or empty default-user-prompt document, and two bare-<c>reason</c> rejections in
    /// <c>QuerySubmissionEndpoints</c>), and <c>TypedResults</c> to be unused Hub-wide. Including
    /// them costs nothing today and closes the obvious escape hatch: composing an error body by
    /// hand and shipping it through <c>Results.Json(..., statusCode: 409)</c>.
    /// </summary>
    private static readonly string[] _errorResultFactories =
    [
        "Microsoft.AspNetCore.Http.Results::BadRequest",
        "Microsoft.AspNetCore.Http.Results::Conflict",
        "Microsoft.AspNetCore.Http.Results::NotFound",
        "Microsoft.AspNetCore.Http.Results::UnprocessableEntity",
        "Microsoft.AspNetCore.Http.Results::Problem",
        "Microsoft.AspNetCore.Http.Results::ValidationProblem",
        "Microsoft.AspNetCore.Http.Results::Json",
        "Microsoft.AspNetCore.Http.Results::StatusCode",
        "Microsoft.AspNetCore.Http.TypedResults::BadRequest",
        "Microsoft.AspNetCore.Http.TypedResults::Conflict",
        "Microsoft.AspNetCore.Http.TypedResults::NotFound",
        "Microsoft.AspNetCore.Http.TypedResults::UnprocessableEntity",
        "Microsoft.AspNetCore.Http.TypedResults::Problem",
        "Microsoft.AspNetCore.Http.TypedResults::ValidationProblem",
        "Microsoft.AspNetCore.Http.TypedResults::Json",
        "Microsoft.AspNetCore.Http.TypedResults::StatusCode",
    ];

    /// <summary>
    /// The migration baseline is gone (T060). Every endpoint family now routes through
    /// <c>ApiErrorResults</c>, so BR1 enforces containment outright with no suppression mechanism —
    /// the same end state ADR-013's N1 legacy-rename ratchet reached once its renames landed.
    /// The empty array is kept rather than deleted outright so
    /// <see cref="MigrationBaseline_ContainsNoStaleEntries"/> keeps asserting it stays empty.
    /// </summary>
    private static readonly string[] _unmigratedNamespaces = [];

    [Fact]
    public void ErrorResultFactories_AreConfinedToTheApiErrorsNamespace()
    {
        var violations = FindErrorResultCallSites()
            .Where(site => !IsBaselined(site.EffectiveNamespace))
            .Select(site => site.Description)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"ADR-026 BR1: error results must be produced only by {ApiErrorsNamespace}. Every " +
            "failing response carries one envelope (spec 024 FR-004), which an inline " +
            "Results.Conflict/BadRequest/Json cannot produce. Route the failure through " +
            "ApiErrorResults and give it a catalogue entry. Violations:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// The baseline is a migration aid, not a permanent exemption: it may only ever shrink. This
    /// asserts that every namespace still listed genuinely has a violation to excuse, so an entry
    /// whose call sites are gone fails here rather than lingering and silently re-opening the
    /// namespace to new violations later.
    /// </summary>
    [Fact]
    public void MigrationBaseline_ContainsNoStaleEntries()
    {
        var namespacesWithViolations = FindErrorResultCallSites()
            .Select(site => site.EffectiveNamespace)
            .ToHashSet(StringComparer.Ordinal);

        var stale = _unmigratedNamespaces
            .Where(baselined => !namespacesWithViolations.Any(
                ns => ns.Equals(baselined, StringComparison.Ordinal) ||
                      ns.StartsWith(baselined + ".", StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            stale.Count == 0,
            "ADR-026 BR1: the migration baseline is remove-only and must not outlive its " +
            "purpose. These namespaces no longer contain an error-result call site, so their " +
            "baseline entry must be removed (the migration task that cleared them should have " +
            "done so):\n" + string.Join("\n", stale));
    }

    private static bool IsBaselined(string effectiveNamespace)
        => _unmigratedNamespaces.Any(
            baselined => effectiveNamespace.Equals(baselined, StringComparison.Ordinal) ||
                         effectiveNamespace.StartsWith(baselined + ".", StringComparison.Ordinal));

    /// <summary>
    /// Every error-result call site in Grimoire.Hub outside <see cref="ApiErrorsNamespace"/>.
    /// The scan covers the whole assembly rather than only <c>Grimoire.Hub.*</c> namespaces, so a
    /// call site in the global-namespace composition root (<c>HubHostComposition</c>) cannot hide
    /// from it.
    /// </summary>
    private static List<ArchScan.CallSite> FindErrorResultCallSites()
    {
        var assemblyPath = System.Reflection.Assembly.Load("Grimoire.Hub").Location;
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);

        return ArchScan.FindCalls(assembly, _errorResultFactories)
            .Where(site => !site.EffectiveNamespace.Equals(ApiErrorsNamespace, StringComparison.Ordinal) &&
                           !site.EffectiveNamespace.StartsWith(ApiErrorsNamespace + ".", StringComparison.Ordinal))
            .ToList();
    }
}
