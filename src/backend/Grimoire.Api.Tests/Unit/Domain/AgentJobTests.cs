using Grimoire.Api.Agents.Models;
using Xunit;

namespace Grimoire.Api.Tests.Unit.Domain;

public class AgentJobTests
{
    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    [Fact]
    public void NewJob_HasPendingStatus()
    {
        var job = new AgentJob("job-1", "agent-01", "{\"op\":\"ingest\"}");
        Assert.Equal(JobStatus.Pending, job.Status);
    }

    [Fact]
    public void NewJob_CreatedAtIsSet()
    {
        var before = DateTime.UtcNow;
        var job = new AgentJob("job-1", "agent-01", "{}");
        var after = DateTime.UtcNow;

        Assert.InRange(job.CreatedAt, before, after);
    }

    [Fact]
    public void NewJob_StartedAtCompletedAtFailedAtAreNull()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");

        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.FailedAt);
        Assert.Null(job.ErrorMessage);
    }

    [Fact]
    public void NewJob_ExplicitCreatedAt_IsUsed()
    {
        var ts = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var job = new AgentJob("job-1", "agent-01", "{}", createdAt: ts);

        Assert.Equal(ts, job.CreatedAt);
    }

    [Fact]
    public void Constructor_NullJobId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentJob(null!, "agent-01", "{}"));
    }

    [Fact]
    public void Constructor_NullAgentId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentJob("job-1", null!, "{}"));
    }

    [Fact]
    public void Constructor_NullPayload_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentJob("job-1", "agent-01", null!));
    }

    [Fact]
    public void Constructor_PersistedState_RestoresAllFields()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var started = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);

        var job = new AgentJob(
            "job-99", "agent-01", "{}", JobStatus.Running, created, started);

        Assert.Equal("job-99", job.JobId);
        Assert.Equal("agent-01", job.AgentId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(created, job.CreatedAt);
        Assert.Equal(started, job.StartedAt);
    }

    // -------------------------------------------------------------------------
    // Start()
    // -------------------------------------------------------------------------

    [Fact]
    public void Start_FromPending_TransitionsToRunning()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");

        job.Start();

        Assert.Equal(JobStatus.Running, job.Status);
    }

    [Fact]
    public void Start_SetsStartedAt()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        var before = DateTime.UtcNow;

        job.Start();

        var after = DateTime.UtcNow;
        Assert.NotNull(job.StartedAt);
        Assert.InRange(job.StartedAt!.Value, before, after);
    }

    [Fact]
    public void Start_FromRunning_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();

        var ex = Assert.Throws<InvalidOperationException>(() => job.Start());
        Assert.Contains("Pending", ex.Message);
    }

    [Fact]
    public void Start_FromCompleted_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        job.Complete();

        Assert.Throws<InvalidOperationException>(() => job.Start());
    }

    [Fact]
    public void Start_FromFailed_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Fail("some reason");  // can fail from Pending

        Assert.Throws<InvalidOperationException>(() => job.Start());
    }

    // -------------------------------------------------------------------------
    // Complete()
    // -------------------------------------------------------------------------

    [Fact]
    public void Complete_FromRunning_TransitionsToCompleted()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();

        job.Complete();

        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public void Complete_SetsCompletedAt()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        var before = DateTime.UtcNow;

        job.Complete();

        var after = DateTime.UtcNow;
        Assert.NotNull(job.CompletedAt);
        Assert.InRange(job.CompletedAt!.Value, before, after);
    }

    [Fact]
    public void Complete_FromPending_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");

        var ex = Assert.Throws<InvalidOperationException>(() => job.Complete());
        Assert.Contains("Running", ex.Message);
    }

    [Fact]
    public void Complete_FromCompleted_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        job.Complete();

        Assert.Throws<InvalidOperationException>(() => job.Complete());
    }

    [Fact]
    public void Complete_FromFailed_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        job.Fail("error");

        Assert.Throws<InvalidOperationException>(() => job.Complete());
    }

    // -------------------------------------------------------------------------
    // Fail(reason)
    // -------------------------------------------------------------------------

    [Fact]
    public void Fail_FromPending_TransitionsToFailed()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");

        job.Fail("cancelled before start");

        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public void Fail_FromRunning_TransitionsToFailed()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();

        job.Fail("runtime error");

        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public void Fail_SetsErrorMessage()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();

        job.Fail("out of memory");

        Assert.Equal("out of memory", job.ErrorMessage);
    }

    [Fact]
    public void Fail_SetsFailedAt()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        var before = DateTime.UtcNow;

        job.Fail("error");

        var after = DateTime.UtcNow;
        Assert.NotNull(job.FailedAt);
        Assert.InRange(job.FailedAt!.Value, before, after);
    }

    [Fact]
    public void Fail_FromCompleted_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        job.Complete();

        var ex = Assert.Throws<InvalidOperationException>(() => job.Fail("too late"));
        // Exception message should mention the invalid state
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Fail_FromAlreadyFailed_ThrowsInvalidOperationException()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();
        job.Fail("first failure");

        Assert.Throws<InvalidOperationException>(() => job.Fail("second failure"));
    }

    [Fact]
    public void Fail_NullReason_SetsDefaultErrorMessage()
    {
        var job = new AgentJob("job-1", "agent-01", "{}");
        job.Start();

        // Passing null should not blow up and should set a default message
        job.Fail(null!);

        Assert.NotNull(job.ErrorMessage);
        Assert.NotEmpty(job.ErrorMessage!);
    }

    // -------------------------------------------------------------------------
    // State machine integrity: properties stay consistent after transitions
    // -------------------------------------------------------------------------

    [Fact]
    public void FullHappyPath_PendingRunningCompleted_PropertiesConsistent()
    {
        var job = new AgentJob("job-1", "agent-01", "{\"op\":\"query\"}");

        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);

        job.Start();
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.Null(job.CompletedAt);

        job.Complete();
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.ErrorMessage);
    }

    [Fact]
    public void FailPath_PendingRunningFailed_PropertiesConsistent()
    {
        var job = new AgentJob("job-2", "agent-01", "{}");

        job.Start();
        job.Fail("network timeout");

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.NotNull(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.NotNull(job.FailedAt);
        Assert.Equal("network timeout", job.ErrorMessage);
    }
}
