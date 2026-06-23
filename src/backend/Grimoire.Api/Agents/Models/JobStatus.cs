namespace Grimoire.Api.Agents.Models;

/// <summary>Job lifecycle states: Pending → Running → (Completed | Failed)</summary>
public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
