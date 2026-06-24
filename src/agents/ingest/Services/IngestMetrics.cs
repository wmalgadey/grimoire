using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Grimoire.Ingest.Services;

public static class IngestTracing
{
    public static readonly ActivitySource Source = new("Grimoire.Ingest");
}

public class IngestMetrics
{
    public const string MeterName = "Grimoire.Ingest";

    private readonly Counter<long> _filesTotal;
    private readonly Counter<long> _chunksTotal;
    private readonly Counter<long> _llmCallsTotal;
    private readonly Counter<long> _conversationTurnsTotal;
    private readonly Counter<long> _feedbackRequestsTotal;
    private readonly Counter<long> _gitCommitsTotal;
    private readonly ObservableGauge<int> _activeRunGauge;

    private volatile int _activeRun;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _filesTotal = meter.CreateCounter<long>(
            "grimoire.ingest.files_total",
            description: "Files evaluated per run");

        _chunksTotal = meter.CreateCounter<long>(
            "grimoire.ingest.chunks_total",
            description: "Chunks produced across all processed files");

        _llmCallsTotal = meter.CreateCounter<long>(
            "grimoire.ingest.llm_calls_total",
            description: "Claude SDK calls made");

        _conversationTurnsTotal = meter.CreateCounter<long>(
            "grimoire.ingest.conversation_turns_total",
            description: "Total conversation turns across all sessions");

        _feedbackRequestsTotal = meter.CreateCounter<long>(
            "grimoire.ingest.feedback_requests_total",
            description: "Feedback requests raised");

        _gitCommitsTotal = meter.CreateCounter<long>(
            "grimoire.ingest.git_commits_total",
            description: "Successful git commits after runs");

        _activeRunGauge = meter.CreateObservableGauge<int>(
            "grimoire.ingest.active_run",
            () => _activeRun,
            description: "1 if a run is in progress, 0 otherwise");
    }

    public void RecordFile(string status) =>
        _filesTotal.Add(1, new KeyValuePair<string, object?>("status", status));

    public void RecordChunks(int count) =>
        _chunksTotal.Add(count);

    public void RecordLlmCall(string status) =>
        _llmCallsTotal.Add(1, new KeyValuePair<string, object?>("status", status));

    public void RecordConversationTurn() =>
        _conversationTurnsTotal.Add(1);

    public void RecordFeedbackRequest(string reason) =>
        _feedbackRequestsTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordGitCommit() =>
        _gitCommitsTotal.Add(1);

    public void SetActiveRun(bool active) =>
        _activeRun = active ? 1 : 0;
}
