namespace Grimoire.Hub.QueryDispatch;

/// <summary>
/// FR-017/ADR-011: the configurable global limit on concurrently running Query Turns,
/// fully decoupled from Ingest's single-agent slot. Bound from the top-level
/// <c>Grimoire:QueryConcurrencyLimit</c> configuration key (same options-binding
/// convention as the existing <c>Grimoire:*</c> settings, e.g. <c>GrimoirePathOptions</c>).
/// </summary>
public sealed class QueryConcurrencyOptions
{
    public const string SectionName = "Grimoire";

    public int QueryConcurrencyLimit { get; set; } = 3;
}
