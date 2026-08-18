namespace Grimoire.Hub;

/// <summary>
/// <c>GET /api/version</c> — the version of the Hub answering the request.
///
/// <para>
/// ADR-027 gave the Hub a truthful version number and surfaced it on the CLI help screen, where
/// only someone with shell access to the host can read it. The frontend's connection indicator
/// needs the same fact for the server it is actually connected to, so that "connected" can say
/// <em>connected to what</em> — after a redeploy the browser may still be running the previous
/// bundle against a new Hub, and a version in the hover panel is what makes that visible.
/// </para>
///
/// <para>
/// Deliberately unauthenticated, dependency-free and constant: it reads
/// <see cref="HubVersion.Current"/> and nothing else, so it stays answerable when the content
/// root, the database or a dispatch path is broken — which is precisely when an operator asks
/// what is running. It has no failure path and therefore no <c>Grimoire.Hub.ApiErrors</c>
/// envelope (ADR-026 BR1 constrains error results only).
/// </para>
///
/// <para>
/// In the assembly root next to <see cref="HubVersion"/> rather than in an endpoint-family
/// namespace: the fact it serves belongs to the process, not to the ingest, query or lint
/// surface (ADR-013 namespace ownership map).
/// </para>
/// </summary>
public static class HubVersionEndpoints
{
    public static RouteGroupBuilder MapHubVersionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", () => Results.Ok(new { version = HubVersion.Current }));
        return group;
    }
}
