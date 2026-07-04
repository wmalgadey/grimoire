namespace Grimoire.Domain.Ingest;

public sealed record PageDecision(PageDecisionAction Action, string TargetPagePath, string Reason, string Category);
