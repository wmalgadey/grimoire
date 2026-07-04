namespace Grimoire.Hub.OperationalState;

public sealed record OperationalTaskState(string TaskId, string Status, int? ProcessId, DateTimeOffset UpdatedAt);
