namespace Grimoire.EvalRunner.Capture;

/// <summary>
/// How many samples of one scenario are captured at a time.
///
/// A capture sample is almost entirely provider latency: spawn the real agent, wait out a
/// multi-turn conversation, score it. Ten of those in sequence is what made a seven-scenario
/// refresh a 40-minute wait. Nothing about a sample is shared with its siblings — each gets
/// its own workspace/sandbox under the temp root, its own write-lock directory, its own
/// capture file and its own agent process — so the samples of a scenario are independent by
/// construction, and running them concurrently changes only wall-clock.
///
/// Bounded rather than unbounded on purpose. Every concurrent sample is a real child process
/// plus its own model traffic, and the provider adapter absorbs only a short burst of 429s
/// (<c>AnthropicModelClient.MaxProviderRetries</c> is 2). A scenario whose samples exhaust
/// that budget stores nothing at all — capture never writes a partial scenario — so an
/// over-eager degree of parallelism does not merely slow the run down, it can throw away
/// every sample's spend. Hence a modest default and a documented ceiling.
/// </summary>
internal static class CaptureParallelism
{
    /// <summary>Applied when <c>--parallel</c> is not given.</summary>
    public const int Default = 4;

    /// <summary>Upper bound accepted from <c>--parallel</c>.</summary>
    public const int Max = 16;

    /// <summary>Sequential capture — the pre-parallel behaviour, and the pipelines' own default.</summary>
    public const int Sequential = 1;

    public static ParallelOptions Options(int maxParallelSamples, CancellationToken cancellationToken)
        => new()
        {
            MaxDegreeOfParallelism = Math.Clamp(maxParallelSamples, Sequential, Max),
            CancellationToken = cancellationToken,
        };
}
