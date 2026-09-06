using Grimoire.AgentRuntime.Composition;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040/T041 (026-guarded-tool-surface, US2, FR-019/FR-020, SC-009/SC-010).
///
/// T042 (a policy declaring <c>frontmatter-only</c> still loads, ADR-031 R5) needs no new
/// test here — <see cref="PolicyLoaderFrontmatterOnlyModeTests"/> (013-lint-agent) already
/// covers it verbatim and nothing about this feature changes that parser case.
/// </summary>
public class LintPolicyIdentityTests
{
    // ── T040: policy identity (version + hash) is recorded on every run ────────────────
    // PolicyLoaderDeleteRuleTests (T007/T008) already proves a v2-shaped policy with a
    // `delete` section parses into a SafetyPolicy that behaves correctly; it never asserts
    // the Identity record itself. IngestGovernanceIdentityTests proves the same for a v1
    // Ingest-shaped policy. Neither covers a v2, delete-scope-bearing policy's identity —
    // the shape Lint's own policy.json becomes once the eval-recapture layer flips it.

    [Fact]
    public async Task V2PolicyWithDeleteScope_LoadsWithAPopulatedIdentity_AndAnEditChangesTheHash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-policy-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            const string original = """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}],
                  "delete": [{"pathPrefix": "."}]
                }
                """;
            await File.WriteAllTextAsync(policyPath, original);

            var loader = new PolicyLoader(root);
            var firstResult = await loader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(firstResult.IsFirst(out var firstLoaded));

            Assert.Equal(2, firstLoaded!.Identity.Version);
            Assert.Equal(policyPath, firstLoaded.Identity.Path);
            Assert.False(string.IsNullOrWhiteSpace(firstLoaded.Identity.Sha256));

            // Editing the content (adding an excludePrefix) changes the recorded hash —
            // the identity is a real content fingerprint, not a static per-version stamp.
            const string edited = """
                {
                  "version": 2,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}],
                  "delete": [{"pathPrefix": ".", "excludePrefixes": ["index.md"]}]
                }
                """;
            await File.WriteAllTextAsync(policyPath, edited);
            var secondResult = await loader.LoadAsync(policyPath, CancellationToken.None);
            Assert.True(secondResult.IsFirst(out var secondLoaded));

            Assert.NotEqual(firstLoaded.Identity.Sha256, secondLoaded!.Identity.Sha256);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ── T041: a missing/unparseable policy fails the run before any wiki change ────────
    // PolicyMisconfigurationTests already proves PolicyLoader itself fails closed
    // (agent-agnostic). What is untested anywhere is the other half of SC-010's claim —
    // that AgentHost's fixed sequence never reaches an agent's ExecuteAsync (the only
    // place a GuardedToolExecutor, and therefore any wiki write or delete, can be
    // constructed) once policy load fails. This exercises that sequencing directly and
    // generically (AgentHost is not agent-specific), which is exactly what every one of
    // Lint's three intent handlers relies on for FR-020.

    [Fact]
    public async Task AgentHost_PolicyLoadFailure_NeverInvokesExecuteAsync_NoWikiChangeIsPossible()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-host-policy-fail-closed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var foundationPromptPath = Path.Combine(root, "foundation-prompt.md");
            await File.WriteAllTextAsync(foundationPromptPath, "You are part of a wiki-maintaining agent team.");
            var systemPromptPath = Path.Combine(root, "system-prompt.md");
            await File.WriteAllTextAsync(systemPromptPath, "You are a test agent.");
            var missingPolicyPath = Path.Combine(root, "does-not-exist-policy.json");

            var profile = new AgentProfile(
                AgentName: "test",
                ServiceName: "Grimoire.Test",
                ActivitySourceName: "Grimoire.Test",
                MeterName: "Grimoire.Test",
                RunSpanName: "test_agent.run",
                CorrelationAttribute: "run_id",
                ToolRegistry: ToolRegistry.Default,
                RequiredInstructionDocuments: new HashSet<InstructionDocument> { InstructionDocument.SystemPrompt },
                ModelEnvVarNames: new ModelEnvVarNames("TEST_MODEL", "TEST_BASE_URL", "TEST_MAX_TOKENS"));

            var host = new AgentHost(profile);
            var run = new AgentHostRun(
                WikiRoot: root,
                FoundationPromptPath: foundationPromptPath,
                SystemPromptPath: systemPromptPath,
                PolicyPath: missingPolicyPath,
                HeartbeatSeconds: 30);

            var intent = new NeverExecuteIntentHandler();
            using var writer = new StringWriter();
            var runEvents = new RunEventEmitter(writer, "run-fail-closed");

            var exitCode = await host.RunAsync(run, runEvents, intent, CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.False(intent.ExecuteAsyncWasCalled);
            Assert.Equal("policy", intent.FailedDocumentKind);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class NeverExecuteIntentHandler : IAgentIntentHandler
    {
        public bool ExecuteAsyncWasCalled { get; private set; }
        public string? FailedDocumentKind { get; private set; }

        public Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnInstructionLoadFailureAsync(
            string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
        {
            FailedDocumentKind = documentKind;
            return Task.CompletedTask;
        }

        public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
        {
            // If this ever runs after a policy load failure, SC-010 is violated: a wiki
            // change would become possible before the policy that governs it loaded.
            ExecuteAsyncWasCalled = true;
            return Task.FromResult(0);
        }

        public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
            => Task.FromResult(exception.Message);
    }
}
