using System.Text.Json;
using Grimoire.IntegrationTests.Fakes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T036 (US4) — live loop-activity propagation (FR-018, SC-011): an `activity` event
/// from the agent reaches connected clients as `run_activity` within 2 seconds (p95),
/// carrying loop mechanics only.
/// </summary>
public class RunActivityRealtimeTests
{
    [Fact]
    public async Task ActivityEvents_PropagateToClients_Within2Seconds_P95()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var fixture = new IngestSubmissionPipelineFixture(launcher: launcher, livenessWindow: TimeSpan.FromSeconds(30));

        await fixture.Coordinator.EnqueueAsync("task-live", Path.Combine(fixture.Root, "src.md"), null);
        var handle = Assert.Single(launcher.Handles);
        handle.EmitEvent("started", "task-live");

        const int eventCount = 20;
        var latencies = new List<TimeSpan>();

        for (var turn = 1; turn <= eventCount; turn++)
        {
            var emittedAt = DateTimeOffset.UtcNow;
            handle.EmitEvent("activity", "task-live", new
            {
                modelTurns = turn,
                toolCalls = turn * 2,
                toolCallsByName = new Dictionary<string, int> { ["read_file"] = turn, ["write_file"] = turn },
                currentAction = "tool_call:write_file",
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            (string Method, object? Payload, DateTimeOffset ReceivedAt)? received = null;
            while (DateTime.UtcNow < deadline)
            {
                lock (fixture.PublishedActivity)
                {
                    if (fixture.PublishedActivity.Count(a => a.Method == "runActivityChanged") >= turn)
                    {
                        received = fixture.PublishedActivity.Where(a => a.Method == "runActivityChanged").Skip(turn - 1).First();
                        break;
                    }
                }

                await Task.Delay(5);
            }

            Assert.NotNull(received);
            latencies.Add(received.Value.ReceivedAt - emittedAt);
        }

        handle.EmitEvent("completed", "task-live", new { summary = "Done." });

        // SC-011: p95 event→client latency ≤ 2 s.
        var p95 = latencies.OrderBy(l => l).ElementAt((int)Math.Ceiling(latencies.Count * 0.95) - 1);
        Assert.True(p95 <= TimeSpan.FromSeconds(2), $"p95 activity propagation latency was {p95.TotalMilliseconds:F0} ms.");

        // Payload carries loop mechanics only (Principle V) and correlates via taskId.
        object? lastPayload;
        lock (fixture.PublishedActivity)
        {
            lastPayload = fixture.PublishedActivity.Last(a => a.Method == "runActivityChanged").Payload;
        }

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(lastPayload));
        Assert.Equal("run_activity", json.GetProperty("kind").GetString());
        Assert.Equal("task-live", json.GetProperty("taskId").GetString());
        Assert.Equal(eventCount, json.GetProperty("modelTurns").GetInt32());
        Assert.Equal(eventCount * 2, json.GetProperty("toolCalls").GetInt32());
        Assert.Equal("tool_call:write_file", json.GetProperty("currentAction").GetString());

        // The detail-view snapshot mirrors the latest event while the task runs.
        var snapshot = fixture.Coordinator.GetActivity("task-live");
        if (snapshot is not null)
        {
            Assert.Equal(eventCount, snapshot.ModelTurns);
        }
    }
}
