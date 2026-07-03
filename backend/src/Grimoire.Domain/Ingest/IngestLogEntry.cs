namespace Grimoire.Domain.Ingest;

public sealed record IngestLogEntry(DateTimeOffset Timestamp, string Operation, string Outcome, string TaskRef);
