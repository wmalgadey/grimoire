namespace Grimoire.Domain.Ingest;

public sealed class IngestTask
{
    private readonly List<string> _pagesTouched = new();

    public IngestTask(string taskId, string sourceRef)
    {
        TaskId = taskId;
        Type = "ingest";
        Agent = "ingest";
        SourceRef = sourceRef;
        Status = IngestTaskStatus.Queued;
        StartedAt = DateTimeOffset.UtcNow;
        Validate();
    }

    public string TaskId { get; }

    public string Type { get; }

    public string Agent { get; }

    public IngestTaskStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string SourceRef { get; }

    public IReadOnlyList<string> PagesTouched => _pagesTouched;

    public string? FailureReason { get; private set; }

    public void TransitionToRunning()
    {
        Status = IngestTaskStatus.Running;
        Validate();
    }

    public void Complete(IEnumerable<string> pagesTouched)
    {
        var pages = pagesTouched?.ToList() ?? new List<string>();
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("Completed task must include at least one touched page.");
        }

        _pagesTouched.Clear();
        _pagesTouched.AddRange(pages);

        Status = IngestTaskStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = null;
        Validate();
    }

    public void Fail(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));
        }

        _pagesTouched.Clear();
        Status = IngestTaskStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = failureReason;
        Validate();
    }

    private void Validate()
    {
        if ((Status == IngestTaskStatus.Queued || Status == IngestTaskStatus.Running) && CompletedAt is not null)
        {
            throw new InvalidOperationException("Non-terminal task cannot have a completed timestamp.");
        }

        if (Status == IngestTaskStatus.Completed && _pagesTouched.Count == 0)
        {
            throw new InvalidOperationException("Completed task must contain touched pages.");
        }

        if (Status == IngestTaskStatus.Failed && string.IsNullOrWhiteSpace(FailureReason))
        {
            throw new InvalidOperationException("Failed task must contain a failure reason.");
        }
    }
}
