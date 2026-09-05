using Grimoire.AgentRuntime.Host;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T025 + T028 (029-shared-foundation-prompt, US1, ADR-053): the one shared composition
/// point (<see cref="AgentHost.RunAsync"/>) produces <c>foundation + "\n\n" + role</c> for
/// every agent type, and that composed text is what actually reaches the model — not just
/// what a computed property says. Each of the three real agent projects' <c>Program.cs</c>
/// declares no <c>InternalsVisibleTo</c> for this assembly, so its own intent-handler class
/// is not directly testable; these tests instead construct the real, agent-agnostic
/// <see cref="AgentHost"/> with the same <see cref="AgentProfile"/> shape each Program.cs
/// builds (<see cref="AgentProfileFixtures"/>) and a hand-rolled
/// <see cref="IAgentIntentHandler"/> test double — mirroring the existing public pattern in
/// <c>LintPolicyIdentityTests.NeverExecuteIntentHandler</c>.
/// </summary>
public class FoundationPromptCompositionTests
{
    public static IEnumerable<object[]> AllProfiles() => AgentProfileFixtures.AllProfiles();

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public async Task ModelReceives_FoundationThenRole_ByteForByte_ViaTheModelClientPortFake(AgentProfile profile)
    {
        var root = Path.Combine(Path.GetTempPath(), $"foundation-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var foundationContent = $"# Foundation for {profile.AgentName}\nEvery agent shares this.\n";
            var roleContent = $"# Role for {profile.AgentName}\nOnly this agent does this.\n";
            var foundationPath = Path.Combine(root, "foundation-prompt.md");
            var systemPromptPath = Path.Combine(root, "system-prompt.md");
            var defaultUserPromptPath = Path.Combine(root, "default-user-prompt.md");
            await File.WriteAllTextAsync(foundationPath, foundationContent);
            await File.WriteAllTextAsync(systemPromptPath, roleContent);
            await File.WriteAllTextAsync(defaultUserPromptPath, "Integrate the source.");

            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}]
                }
                """);

            var fake = new FakeModelClient([FakeModelClient.FinalTurn("final narrative")]);
            var intent = new CapturingIntentHandler(root, fake, profile.ToolRegistry);

            var host = new AgentHost(profile);
            var run = new AgentHostRun(
                WikiRoot: root,
                FoundationPromptPath: foundationPath,
                SystemPromptPath: systemPromptPath,
                PolicyPath: policyPath,
                HeartbeatSeconds: 30,
                DefaultUserPromptPath: defaultUserPromptPath);
            using var writer = new StringWriter();
            var runEvents = new RunEventEmitter(writer, "run-composition");

            var exitCode = await host.RunAsync(run, runEvents, intent, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, fake.CallCount);
            Assert.Equal(foundationContent + "\n\n" + roleContent, fake.Calls[0].SystemPrompt);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public async Task ComposedSystemPrompt_IsFoundationThenRole_InThatOrder_ForEveryAgentType(AgentProfile profile)
    {
        // Feature-Scoped Invariant (ADR-053): the composition order is a property of this
        // feature's current surface — asserted directly on the real composed text, never
        // by reflection over LoadedInstructions's shape.
        var root = Path.Combine(Path.GetTempPath(), $"foundation-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            const string foundationMarker = "FOUNDATION-MARKER-CONTENT";
            const string roleMarker = "ROLE-MARKER-CONTENT";
            var foundationPath = Path.Combine(root, "foundation-prompt.md");
            var systemPromptPath = Path.Combine(root, "system-prompt.md");
            var defaultUserPromptPath = Path.Combine(root, "default-user-prompt.md");
            await File.WriteAllTextAsync(foundationPath, foundationMarker);
            await File.WriteAllTextAsync(systemPromptPath, roleMarker);
            await File.WriteAllTextAsync(defaultUserPromptPath, "Integrate the source.");

            var policyPath = Path.Combine(root, "policy.json");
            await File.WriteAllTextAsync(policyPath, """
                {
                  "version": 1,
                  "defaultDecision": "deny",
                  "read": [{"pathPrefix": "."}],
                  "write": [{"pathPrefix": "."}]
                }
                """);

            var fake = new FakeModelClient([FakeModelClient.FinalTurn("final narrative")]);
            var intent = new CapturingIntentHandler(root, fake, profile.ToolRegistry);

            var host = new AgentHost(profile);
            var run = new AgentHostRun(
                WikiRoot: root,
                FoundationPromptPath: foundationPath,
                SystemPromptPath: systemPromptPath,
                PolicyPath: policyPath,
                HeartbeatSeconds: 30,
                DefaultUserPromptPath: defaultUserPromptPath);
            using var writer = new StringWriter();
            var runEvents = new RunEventEmitter(writer, "run-order");

            await host.RunAsync(run, runEvents, intent, CancellationToken.None);

            Assert.NotNull(intent.Instructions);
            var composed = intent.Instructions!.ComposedSystemPrompt;
            Assert.Equal(foundationMarker + "\n\n" + roleMarker, composed);
            Assert.True(
                composed.IndexOf(foundationMarker, StringComparison.Ordinal) <
                composed.IndexOf(roleMarker, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
