using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;

namespace Grimoire.AgentEvals;

/// <summary>
/// T049 (004 SC-007): ≥ 90% of sampled runs submitted with an explicit, well-formed
/// steering User Prompt visibly reflect that steer in the run outcome (final summary
/// and touched pages match the requested focus). Unlike SC-006's pattern-matching
/// checks, "reflects the steer" is a judgment call, so this uses an LLM judge scoring
/// each run's narrative + touched-page content against its steer (Constitution
/// Principle II: agent-judgment criteria are evaluation-scored, not reimplemented as
/// deterministic string matching).
/// </summary>
[Collection("EvalProviderEnvironment")]
public class SteeringAdoptionEvals
{
    private static readonly IReadOnlyList<(string SourceContent, string Steer)> Pairs =
    [
        (
            "Retrieval-augmented generation (RAG) combines a retriever that fetches relevant documents " +
            "with a generator that conditions its answer on those documents. It reduces hallucination " +
            "versus a pure parametric model, but retrieval quality caps answer quality: a generator " +
            "cannot answer well from irrelevant context. Marketing materials for RAG products often " +
            "claim it 'eliminates hallucination entirely', which is not accurate.",
            "Focus only on the marketing-claims accuracy angle. Ignore the general RAG architecture description."
        ),
        (
            "Kubernetes Horizontal Pod Autoscaler (HPA) scales replica count based on observed CPU/memory " +
            "utilization or custom metrics. It polls the metrics API on an interval, compares against a " +
            "target, and computes a desired replica count with a stabilization window to avoid flapping. " +
            "HPA does not provision new nodes — that's the Cluster Autoscaler's job, a related but " +
            "separate controller.",
            "Treat this as an update to the existing autoscaling page: only add the flapping/stabilization-window detail, don't create a new page."
        ),
        (
            "Domain-Driven Design's Bounded Context is a boundary within which a particular domain model " +
            "applies with a consistent ubiquitous language. Different bounded contexts can model the same " +
            "real-world concept differently (e.g. 'Customer' means something different in Billing vs. " +
            "Support). Context maps document the relationships and translation patterns between contexts.",
            "Focus on the practical anti-pattern of skipping context maps, not the textbook definition."
        ),
        (
            "SQLite's WAL (write-ahead log) mode allows concurrent readers alongside a single writer by " +
            "appending changes to a separate log file instead of writing directly to the database file, " +
            "then periodically checkpointing. This trades a small amount of read staleness tolerance for " +
            "much better write concurrency than the default rollback-journal mode.",
            "Ignore WAL mechanics — I only care about when to prefer WAL vs rollback-journal for an embedded single-writer service like ours."
        ),
        (
            "OpenTelemetry defines three signal types: traces (causally-linked spans), metrics " +
            "(aggregatable numeric measurements), and logs (timestamped structured events). Correlating " +
            "them typically means propagating a trace_id/span_id into log records and exemplars on " +
            "metrics, so an operator can pivot from a metric spike to the specific traces that caused it.",
            "Steer toward the log/metric correlation mechanics specifically — skip the general three-signal-types overview, we already have a page for that."
        ),
        (
            "The circuit breaker pattern wraps a call to an unreliable dependency with a state machine: " +
            "closed (calls pass through), open (calls fail fast without attempting the dependency), and " +
            "half-open (a trial call decides whether to close again). It prevents cascading failure and " +
            "gives a struggling downstream service room to recover.",
            "Focus specifically on the half-open state's trial-call behavior — that's the part I'm actually trying to understand."
        ),
        (
            "Git's reflog records every position HEAD and branch refs have pointed to, including commits " +
            "that are no longer reachable from any branch after a reset or rebase. It's a local-only, " +
            "time-limited safety net (default 90 days for reachable and 30 for unreachable entries) — " +
            "reflog entries are never pushed and don't exist on a fresh clone.",
            "Focus purely on the recovery angle: how reflog gets you out of a bad reset/rebase. Skip the retention-window numbers."
        ),
        (
            "Confidence scoring in a personal knowledge base assigns a qualitative reliability signal to " +
            "a page based on source count, source authority, and staleness. It is not a truth score — it " +
            "flags which pages deserve re-verification, not which are 'correct'.",
            "Treat this source as an update to the existing confidence-scoring page's rationale section, not a new page — it's explaining existing behavior, not introducing anything new."
        ),
        (
            "Feature flags decouple deployment from release: code ships dark behind a flag, then gets " +
            "enabled per-user, per-percentage, or per-environment without a redeploy. The operational risk " +
            "is flag debt — flags nobody removes after the rollout completes, which silently multiply the " +
            "number of code paths under test.",
            "Focus the page specifically on flag debt and its cleanup discipline. The deploy/release decoupling part is already covered elsewhere in the wiki — don't duplicate it."
        ),
        (
            "Idempotency keys let a client safely retry a non-idempotent operation (like charging a card) " +
            "by attaching a client-generated unique key to the request; the server recognizes a replay of " +
            "the same key and returns the original result instead of performing the action twice. Keys " +
            "need a server-side expiry policy, or the dedup table grows unbounded.",
            "Steer toward the expiry-policy operational concern — I already understand the basic retry-safety concept."
        ),
    ];

    [EvalFact]
    public async Task SC007_SteeredRuns_ReflectTheRequestedFocus_AtLeast90Percent()
    {
        var runner = new AgentEvalRunner();
        // The judge needs its own AnthropicModelClient wired to whichever provider is
        // resolved (Anthropic or affordable) — it isn't part of the main agent loop that
        // RunAsync wires up, so it must be constructed via the same helper (nothing has
        // mutated the process env yet at this point in the test method).
        var judge = AgentEvalRunner.CreateProviderWiredAnthropicClient(EvalProviderResolver.Resolve().Configuration);
        var successes = 0;

        for (var i = 0; i < Pairs.Count; i++)
        {
            var (sourceContent, steer) = Pairs[i];

            var result = await runner.RunAsync(
                fixtureName: "empty-topic",
                sourceContent: sourceContent,
                runLabel: $"sc007-steer-{i + 1}",
                mutateSystemPrompt: null,
                cancellationToken: CancellationToken.None,
                userPrompt: steer);

            var pass = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && await JudgeReflectsSteerAsync(judge, steer, result, CancellationToken.None);

            if (pass)
            {
                successes++;
            }
        }

        var rate = Pairs.Count == 0 ? 0 : (double)successes / Pairs.Count;
        Assert.True(rate >= 0.90, $"SC-007 threshold not met. Success rate: {rate:P1} ({successes}/{Pairs.Count}).");
    }

    /// <summary>
    /// LLM-judge rubric: given the steer and the run's summary plus the full content of
    /// every touched page, does the outcome demonstrably reflect the requested focus?
    /// The judge sees no tools and returns a single verdict line so parsing stays
    /// deterministic even though the judgment itself is not.
    /// </summary>
    private static async Task<bool> JudgeReflectsSteerAsync(
        AnthropicModelClient judge, string steer, EvalRunResult result, CancellationToken cancellationToken)
    {
        var touchedPageContents = result.PageFiles
            .Select(p => $"### {Path.GetFileName(p)}\n\n{File.ReadAllText(p)}")
            .ToList();

        var judgePrompt =
            $"""
            You are evaluating whether an ingest agent's run outcome reflects a requested steer.

            Steer given to the agent for this run:
            "{steer}"

            Agent's final summary:
            "{result.Artifact.Narrative}"

            Touched wiki pages:
            {(touchedPageContents.Count == 0 ? "(none)" : string.Join("\n\n", touchedPageContents))}

            Question: does the run's summary and the touched pages' content demonstrably
            reflect the requested steer — i.e. did the agent apply the requested focus,
            rather than ignoring it and processing the source generically?

            Respond with exactly one line: "VERDICT: PASS" or "VERDICT: FAIL", optionally
            followed by a one-sentence reason on the next line.
            """;

        var turn = await judge.NextTurnAsync(
            systemPrompt: "You are a strict, terse evaluation judge. Follow the requested response format exactly.",
            conversation: [new ConversationMessage("user", judgePrompt)],
            tools: [],
            cancellationToken: cancellationToken);

        return (turn.AssistantText ?? string.Empty).Contains("VERDICT: PASS", StringComparison.Ordinal);
    }
}
