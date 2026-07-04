namespace Grimoire.IngestAgent.Synthesis;

public sealed record SynthesisResult(
	string Title,
	string Summary,
	string Category,
	string Content,
	IReadOnlyList<SynthesizedWikiPage> PlannedPages);

public sealed record SynthesizedWikiPage(
	string Kind,
	string Title,
	string Summary,
	string Category,
	string Content,
	IReadOnlyList<string> InboundLinks);
