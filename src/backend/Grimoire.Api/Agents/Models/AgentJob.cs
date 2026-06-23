using System.Text.Json.Serialization;

namespace Grimoire.Api.Agents.Models;

public class AgentJob
{
    [JsonPropertyName("jobId")]
    public string JobId { get; }

    [JsonPropertyName("agentId")]
    public string AgentId { get; }

    [JsonPropertyName("payload")]
    public string Payload { get; }

    [JsonPropertyName("status")]
    public JobStatus Status { get; private set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; private set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; private set; }

    [JsonPropertyName("failedAt")]
    public DateTime? FailedAt { get; private set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; private set; }

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

    public void Start()
    {
        if (Status != JobStatus.Pending)
            throw new InvalidOperationException($"Cannot start job in {Status} state. Must be Pending.");
        Status = JobStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (Status != JobStatus.Running)
            throw new InvalidOperationException($"Cannot complete job in {Status} state. Must be Running.");
        if (StartedAt == null)
            throw new InvalidOperationException("Cannot complete a job that was never started.");
        Status = JobStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        if (Status != JobStatus.Running && Status != JobStatus.Pending)
            throw new InvalidOperationException($"Cannot fail job in {Status} state. Must be Pending or Running.");
        Status = JobStatus.Failed;
        FailedAt = DateTime.UtcNow;
        ErrorMessage = reason ?? "Unknown error";
    }
}
