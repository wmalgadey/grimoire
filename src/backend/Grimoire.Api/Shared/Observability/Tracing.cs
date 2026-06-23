using System.Diagnostics;

namespace Grimoire.Api.Shared.Observability;

public static class HubTracing
{
    public static readonly ActivitySource Source = new("Grimoire.Hub", "1.0.0");

    public static Activity? StartRegisterAgent(string agentId, string agentName) =>
        Source.StartActivity("hub.register_agent")
            ?.SetTag("agent_id", agentId)
            .SetTag("agent_name", agentName);

    public static Activity? StartStartAgent(string agentId) =>
        Source.StartActivity("hub.start_agent")
            ?.SetTag("agent_id", agentId);

    public static Activity? StartStopAgent(string agentId) =>
        Source.StartActivity("hub.stop_agent")
            ?.SetTag("agent_id", agentId);

    public static Activity? StartHealthCheck(int agentCount) =>
        Source.StartActivity("hub.health_check")
            ?.SetTag("agent_count", agentCount);

    public static Activity? StartDispatchJob(string jobId, string agentId) =>
        Source.StartActivity("hub.dispatch_job")
            ?.SetTag("job_id", jobId)
            .SetTag("agent_id", agentId);

    public static Activity? StartRecoverState(int agentsCount, int jobsCount) =>
        Source.StartActivity("hub.recover_state")
            ?.SetTag("agents_count", agentsCount)
            .SetTag("jobs_count", jobsCount);
}
