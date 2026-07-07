using Grimoire.Hub.ContentRoot;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// HTTP endpoints for ingest submission and board data (contracts/ingest-submission-api.md).
/// Routes are added incrementally per user story: POST (T025, US1), GET list (T042, US2),
/// GET detail (T043, US2).
/// </summary>
public static class IngestSubmissionEndpoints
{
    public static RouteGroupBuilder MapIngestSubmissionEndpoints(
        this RouteGroupBuilder group,
        string repoRoot,
        ContentRootPaths contentPaths,
        string envPath,
        string agentProjectPath)
    {
        return group;
    }
}
