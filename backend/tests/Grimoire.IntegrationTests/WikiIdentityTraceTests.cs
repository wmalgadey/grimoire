using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T053 (029-shared-foundation-prompt, US2, FR-011/FR-013a): the <c>hub.wiki_identity.wizard</c>
/// root span and its <c>hub.wiki_identity.persist</c> child — name, parent/child linkage,
/// and required attributes — obtained from the real <see cref="WikiIdentityCommand"/>
/// running against a real temp data root, never a test-only provider (Principle IV; the
/// failure mode features 002/003 shipped). Mirrors <c>IngestSubmissionTraceTests</c>'/
/// <c>PathTracingContractTests</c>' raw <see cref="ActivityListener"/> idiom on the same
/// real, shared <c>Grimoire.Hub</c> <see cref="ActivitySource"/> the command emits to.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class WikiIdentityTraceTests
{
    [Fact]
    public async Task Default_StartsWizardSpan_RootParented_WithAnswerOutcomeAndInteractiveAttributes()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-trace-default-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);

        try
        {
            var (exitCode, _) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, @default: true);
            Assert.Equal(0, exitCode);

            var wizard = Assert.Single(spans.Where(a => a.OperationName == "hub.wiki_identity.wizard"));
            Assert.Null(wizard.ParentId);
            Assert.Equal("default", wizard.GetTagItem("answer"));
            Assert.Equal("default_kept", wizard.GetTagItem("outcome"));
            Assert.NotNull(wizard.GetTagItem("interactive"));

            Assert.Empty(spans.Where(a => a.OperationName == "hub.wiki_identity.persist"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FromFile_StartsPersistSpan_AsChildOfWizardSpan_WithSha256ReplacedExistingAndResolvedPathAttributes()
    {
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => spans.Enqueue(a),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"wiki-identity-trace-persist-{Guid.NewGuid():N}");
        var paths = TestResolvedGrimoirePathsFactory.Create(root);
        var draftedPath = Path.Combine(Path.GetTempPath(), $"wiki-identity-trace-drafted-{Guid.NewGuid():N}.md");

        try
        {
            await File.WriteAllTextAsync(draftedPath, "# Drafted\nFor the trace test.\n");

            var (exitCode, _) = await WikiIdentityCommandTestHarness.RunSetAsync(paths, fromFile: draftedPath);
            Assert.Equal(0, exitCode);

            var wizard = Assert.Single(spans.Where(a => a.OperationName == "hub.wiki_identity.wizard"));
            Assert.Null(wizard.ParentId);
            Assert.Equal("hand-back", wizard.GetTagItem("answer"));
            Assert.Equal("document_persisted", wizard.GetTagItem("outcome"));

            var persist = Assert.Single(spans.Where(a => a.OperationName == "hub.wiki_identity.persist"));
            Assert.Equal(wizard.SpanId.ToHexString(), persist.ParentSpanId.ToHexString());
            Assert.Equal(wizard.TraceId.ToHexString(), persist.TraceId.ToHexString());
            Assert.NotNull(persist.GetTagItem("sha256"));
            Assert.Equal(false, persist.GetTagItem("replaced_existing"));
            Assert.Equal(paths.InstanceFoundationPromptPath, persist.GetTagItem("resolved_path"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(draftedPath))
            {
                File.Delete(draftedPath);
            }
        }
    }
}
