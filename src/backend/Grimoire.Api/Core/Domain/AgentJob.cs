using System.Text.Json.Serialization;

namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Aggregate root representing a job queued for or being executed by an agent.
/// Enforces state machine transitions: Pending → Running → (Completed | Failed)
/// </summary>
public class AgentJob
{
    /// <summary>
    /// Unique identifier for this job.
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; }

    /// <summary>
    /// Agent ID this job is assigned to.
    /// </summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; }

    /// <summary>
    /// JSON payload to be processed by the agent.
    /// </summary>
    [JsonPropertyName("payload")]
    public string Payload { get; }

    /// <summary>
    /// Current job status.
    /// </summary>
    [JsonPropertyName("status")]
    public JobStatus Status { get; private set; }

    /// <summary>
    /// When this job was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; }

    /// <summary>
    /// When the agent started processing this job, or null if not yet started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// When the job completed successfully, or null if not completed.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// When the job failed, or null if not failed.
    /// </summary>
    [JsonPropertyName("failedAt")]
    public DateTime? FailedAt { get; private set; }

    /// <summary>
    /// Error message if the job failed, or null if successful.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Creates a new job in Pending state or from persisted state.
    /// </summary>
    public AgentJob(string jobId, string agentId, string payload, JobStatus? status = null,
        DateTime? createdAt = null, DateTime? startedAt = null, DateTime? completedAt = null,
        DateTime? failedAt = null, string? errorMessage = null)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Status = status ?? JobStatus.Pending;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        FailedAt = failedAt;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Transitions job to Running state.
    /// </summary>
    public void Start()
    {
        if (Status != JobStatus.Pending)
            throw new InvalidOperationException($"Cannot start job in {Status} state. Must be Pending.");

        Status = JobStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Transitions job to Completed state.
    /// </summary>
    public void Complete()
    {
        if (Status != JobStatus.Running)
            throw new InvalidOperationException($"Cannot complete job in {Status} state. Must be Running.");

        if (StartedAt == null)
            throw new InvalidOperationException("Cannot complete a job that was never started (StartedAt is null).");

        Status = JobStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Transitions job to Failed state with an error message.
    /// </summary>
    public void Fail(string reason)
    {
        if (Status != JobStatus.Running && Status != JobStatus.Pending)
            throw new InvalidOperationException($"Cannot fail job in {Status} state. Must be Pending or Running.");

        Status = JobStatus.Failed;
        FailedAt = DateTime.UtcNow;
        ErrorMessage = reason ?? "Unknown error";
    }
}
