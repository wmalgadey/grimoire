using Grimoire.Hub.Runtime.Paths;
using Grimoire.Hub.WikiIdentity;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T033 + T051 + T052 (029-shared-foundation-prompt, US1/US2, FR-018): every one of the
/// five Structured Log Events rows plan.md declares for this feature, in one place — name,
/// level, and every mandatory field. T052 folds T033's <c>wiki_identity_foundation_resolved</c>
/// assertion (previously in <c>FoundationPromptObservabilityTests</c>) together with the
/// wizard's four events, verified proportionately to its stakes (Principle II): a
/// deterministic harness contract, exercised via each log-events helper called directly —
/// mirrors <c>LintRemediationObservabilityTests</c>'s direct-call <see cref="CaptureLogger{T}"/>
/// pattern.
/// </summary>
public class WikiIdentityLoggingContractTests
{
    [Fact]
    public void FoundationResolved_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<WikiIdentityLoggingContractTests>();

        GrimoirePathLogEvents.LogFoundationResolved(
            logger, agentId: "query", source: "instance", resolvedPath: "/data/foundation-prompt.md", sha256: "abc123");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_foundation_resolved"));
        Assert.Equal(LogLevel.Information, entry.Level);

        Assert.True(entry.Fields.ContainsKey("agent_id"), "Missing mandatory field 'agent_id'.");
        Assert.True(entry.Fields.ContainsKey("source"), "Missing mandatory field 'source'.");
        Assert.True(entry.Fields.ContainsKey("resolved_path"), "Missing mandatory field 'resolved_path'.");
        Assert.True(entry.Fields.ContainsKey("sha256"), "Missing mandatory field 'sha256'.");

        Assert.Equal("query", entry.Fields["agent_id"]?.ToString());
        Assert.Equal("instance", entry.Fields["source"]?.ToString());
        Assert.Equal("/data/foundation-prompt.md", entry.Fields["resolved_path"]?.ToString());
        Assert.Equal("abc123", entry.Fields["sha256"]?.ToString());
    }

    [Fact]
    public void DefaultKept_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<WikiIdentityLoggingContractTests>();

        WikiIdentityLogEvents.LogDefaultKept(logger, outcome: "default_kept");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_default_kept"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("outcome"), "Missing mandatory field 'outcome'.");
        Assert.Equal("default_kept", entry.Fields["outcome"]?.ToString());
    }

    [Fact]
    public void BriefEmitted_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<WikiIdentityLoggingContractTests>();

        WikiIdentityLogEvents.LogBriefEmitted(logger, descriptionLength: 42, briefLength: 512);

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_brief_emitted"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("description_length"), "Missing mandatory field 'description_length'.");
        Assert.True(entry.Fields.ContainsKey("brief_length"), "Missing mandatory field 'brief_length'.");
        Assert.Equal("42", entry.Fields["description_length"]?.ToString());
        Assert.Equal("512", entry.Fields["brief_length"]?.ToString());
    }

    [Fact]
    public void DocumentPersisted_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<WikiIdentityLoggingContractTests>();

        WikiIdentityLogEvents.LogDocumentPersisted(logger, sha256: "def456", bytes: 1024, replacedExisting: true);

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_document_persisted"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(entry.Fields.ContainsKey("sha256"), "Missing mandatory field 'sha256'.");
        Assert.True(entry.Fields.ContainsKey("bytes"), "Missing mandatory field 'bytes'.");
        Assert.True(entry.Fields.ContainsKey("replaced_existing"), "Missing mandatory field 'replaced_existing'.");
        Assert.Equal("def456", entry.Fields["sha256"]?.ToString());
        Assert.Equal("1024", entry.Fields["bytes"]?.ToString());
        Assert.Equal("True", entry.Fields["replaced_existing"]?.ToString());
    }

    [Fact]
    public void ReplaceRefused_EmitsExpectedNameLevelAndAllMandatoryFields()
    {
        var logger = new CaptureLogger<WikiIdentityLoggingContractTests>();

        WikiIdentityLogEvents.LogReplaceRefused(
            logger, existingSha256: "ghi789", reason: "an instance document already exists and --replace was not given");

        var entry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki_identity_replace_refused"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.True(entry.Fields.ContainsKey("existing_sha256"), "Missing mandatory field 'existing_sha256'.");
        Assert.True(entry.Fields.ContainsKey("reason"), "Missing mandatory field 'reason'.");
        Assert.Equal("ghi789", entry.Fields["existing_sha256"]?.ToString());
        Assert.Equal(
            "an instance document already exists and --replace was not given", entry.Fields["reason"]?.ToString());
    }
}
