namespace Grimoire.Domain.Ingest;

public enum IngestTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}
