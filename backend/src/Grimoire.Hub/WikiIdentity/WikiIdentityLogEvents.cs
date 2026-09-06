using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Grimoire.Hub.WikiIdentity;

/// <summary>
/// Structured log events for the wiki-identity wizard (plan.md ## Observability >
/// Structured Log Events, T047/T048). None of these start their own span — each is emitted
/// while <see cref="Activity.Current"/> is the wizard span (<c>hub.wiki_identity.wizard</c>)
/// or, for the two persist-time events, its <c>hub.wiki_identity.persist</c> child — so
/// tagging the current activity is enough to correlate, mirroring
/// <c>GrimoirePathLogEvents.LogFoundationResolved</c>'s "no span of its own" pattern.
/// </summary>
public static class WikiIdentityLogEvents
{
    private static readonly EventId DefaultKeptEvent = new(1, "wiki_identity_default_kept");
    private static readonly EventId BriefEmittedEvent = new(2, "wiki_identity_brief_emitted");
    private static readonly EventId DocumentPersistedEvent = new(3, "wiki_identity_document_persisted");
    private static readonly EventId ReplaceRefusedEvent = new(4, "wiki_identity_replace_refused");

    /// <summary>The wizard completes with the operator choosing the shipped default (FR-012).</summary>
    public static void LogDefaultKept(ILogger logger, string outcome)
    {
        TagCurrent("wiki_identity_default_kept", "Information", ("outcome", outcome));

        logger.LogInformation(DefaultKeptEvent,
            "Wiki identity wizard: default kept. outcome={outcome}", outcome);
    }

    /// <summary>The wizard produces a drafting brief from the operator's description (FR-013).</summary>
    public static void LogBriefEmitted(ILogger logger, int descriptionLength, int briefLength)
    {
        TagCurrent("wiki_identity_brief_emitted", "Information",
            ("description_length", descriptionLength), ("brief_length", briefLength));

        logger.LogInformation(BriefEmittedEvent,
            "Wiki identity wizard: drafting brief emitted. description_length={description_length} brief_length={brief_length}",
            descriptionLength, briefLength);
    }

    /// <summary>The wizard writes an instance document (FR-013a).</summary>
    public static void LogDocumentPersisted(ILogger logger, string sha256, int bytes, bool replacedExisting)
    {
        TagCurrent("wiki_identity_document_persisted", "Information",
            ("sha256", sha256), ("bytes", bytes), ("replaced_existing", replacedExisting));

        logger.LogInformation(DocumentPersistedEvent,
            "Wiki identity wizard: instance document persisted. sha256={sha256} bytes={bytes} replaced_existing={replaced_existing}",
            sha256, bytes, replacedExisting);
    }

    /// <summary>A re-run would have replaced an existing document without an explicit decision (FR-014).</summary>
    public static void LogReplaceRefused(ILogger logger, string existingSha256, string reason)
    {
        TagCurrent("wiki_identity_replace_refused", "Warning",
            ("existing_sha256", existingSha256), ("reason", reason));

        logger.LogWarning(ReplaceRefusedEvent,
            "Wiki identity wizard: replace refused. existing_sha256={existing_sha256} reason={reason}",
            existingSha256, reason);
    }

    private static void TagCurrent(string eventName, string level, params (string Key, object? Value)[] fields)
    {
        var current = Activity.Current;
        current?.SetTag("signal_type", "log");
        current?.SetTag("event_name", eventName);
        current?.SetTag("level", level);
        foreach (var (key, value) in fields)
        {
            current?.SetTag(key, value);
        }
    }
}
