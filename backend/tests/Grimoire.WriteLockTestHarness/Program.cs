using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.Domain.Guardrails;
using System.Text.Json;

// T013 (012-query-synthesis-writes, research.md R6): a tiny, real, separate-process test
// harness proving CrossProcessFileLock provides genuine OS-level cross-process exclusion
// — an in-process fake cannot prove this, only a real second process racing the same
// lock file can. No network, no API key: purely local filesystem locking.
//
// lock-probe usage: lock-probe <writeLocksDir> <canonicalTargetPath> <backoffCapMs> <holdMs>
//   holdMs >= 0: acquire, print "ACQUIRED", hold for holdMs, release, print "RELEASED".
//   holdMs <  0: acquire, print "ACQUIRED", then block indefinitely (until this process
//                is killed or its stdin is closed) — used by the "killed holder" case.
//   Exit code 0 on ACQUIRED (eventually released or killed), 1 on TIMEOUT, 2 on bad args.
//
// T037 (012-query-synthesis-writes, US3, research.md R6): guarded-append drives the
// actual GuardedToolExecutor stack (policy + SharedFileWriteGuard + CrossProcessFileLock),
// not just the raw lock — proves the full read-then-compare-and-swap-write guarantee holds
// across genuine separate OS processes, not only in-process fakes.
//
// guarded-append usage:
//   guarded-append <wikiRoot> <writeLocksDir> <relativePath> <entryText> <waitForStdinBeforeWrite:0|1>
//   Reads <relativePath> under <wikiRoot> (missing == empty content), prints "READ" and
//   flushes. If waitForStdinBeforeWrite is 1, blocks until a line arrives on stdin (the
//   parent process uses this to guarantee both racing processes have read before either
//   writes). Appends entryText as a new line and writes back through the real guarded
//   executor. Prints "WRITTEN" and exits 0 on success; prints "DENIED:<reason>" and exits 1
//   on a policy/coordination denial.

if (args.Length == 5 && args[0] == "lock-probe")
{
    return await RunLockProbeAsync(args);
}

if (args.Length == 6 && args[0] == "guarded-append")
{
    return await RunGuardedAppendAsync(args);
}

// Issue #183: a stand-in "talkative ingest agent" for
// AgentProcessDispatchStderrDrainTests — recognizes the exact `--task-id ...` argv
// shape AgentProcessHost.StartIngestProcess(IngestAgentRequest) always spawns with (no other
// flag here is parsed) so the real dispatch path (StartAsync, not RunToExitAsync) can be
// pointed at this harness in place of the real Grimoire.IngestAgent worker.
if (args.Length > 0 && args[0] == "--task-id")
{
    return await RunStderrFloodAsync();
}

await Console.Error.WriteLineAsync(
    "Usage: lock-probe <writeLocksDir> <canonicalTargetPath> <backoffCapMs> <holdMs>\n" +
    "       guarded-append <wikiRoot> <writeLocksDir> <relativePath> <entryText> <waitForStdinBeforeWrite:0|1>");
return 2;

static async Task<int> RunLockProbeAsync(string[] args)
{
    var writeLocksDir = args[1];
    var targetPath = args[2];
    var backoffCapMs = int.Parse(args[3]);
    var holdMs = int.Parse(args[4]);

    var handle = await CrossProcessFileLock.TryAcquireAsync(
        writeLocksDir, targetPath, TimeSpan.FromMilliseconds(backoffCapMs), CancellationToken.None);

    if (handle is null)
    {
        Console.WriteLine("TIMEOUT");
        return 1;
    }

    Console.WriteLine("ACQUIRED");
    Console.Out.Flush();

    if (holdMs < 0)
    {
        // Hold until this process is killed or its stdin closes (parent decides which).
        await Console.In.ReadLineAsync();
    }
    else
    {
        await Task.Delay(holdMs);
    }

    handle.Dispose();
    Console.WriteLine("RELEASED");
    return 0;
}

static async Task<int> RunGuardedAppendAsync(string[] args)
{
    var wikiRoot = args[1];
    var writeLocksDir = args[2];
    var relativePath = args[3];
    var entryText = args[4];
    var waitForStdinBeforeWrite = args[5] == "1";

    Directory.CreateDirectory(wikiRoot);

    var policy = new SafetyPolicy(
        wikiRoot,
        readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
        writePrefixes: [wikiRoot + Path.DirectorySeparatorChar]);
    var journal = new WriteJournal();
    var executor = new GuardedToolExecutor(policy, journal, wikiRoot, writeLocksDir: writeLocksDir);

    var readResult = await executor.ExecuteAsync(
        ToolRegistry.ReadFile,
        JsonSerializer.Serialize(new { path = relativePath }),
        turn: 1,
        CancellationToken.None);
    var currentContent = readResult.IsError ? string.Empty : readResult.Content;

    Console.WriteLine("READ");
    Console.Out.Flush();

    if (waitForStdinBeforeWrite)
    {
        await Console.In.ReadLineAsync();
    }

    var newContent = currentContent.Length == 0 ? entryText : currentContent + "\n" + entryText;

    var writeResult = await executor.ExecuteAsync(
        ToolRegistry.WriteFile,
        JsonSerializer.Serialize(new { path = relativePath, content = newContent }),
        turn: 2,
        CancellationToken.None);

    if (!writeResult.IsError)
    {
        Console.WriteLine("WRITTEN");
        return 0;
    }

    var reason = executor.Denials.Count > 0 ? executor.Denials[^1].Reason : "unknown";
    Console.WriteLine($"DENIED:{reason}");
    return 1;
}

// Issue #183: writes comfortably past a Linux pipe's default 64 KiB kernel buffer to
// stderr, then emits a single stdout line and exits. Before the fix, nothing drained the
// dispatch path's redirected stderr — once the buffer filled, the next stderr write
// blocked forever and this process (and the run coordinating it) never reached its
// terminal event. Real ANSI/JSON content in every line so a naive "read one huge line"
// implementation would not accidentally pass by reading it as one incomplete line either.
static async Task<int> RunStderrFloodAsync()
{
    const int floodBytes = 200_000; // ~3x the 64 KiB default pipe buffer
    var line = new string('e', 200);
    var written = 0;

    while (written < floodBytes)
    {
        await Console.Error.WriteLineAsync(line);
        written += line.Length + 1;
    }

    await Console.Error.FlushAsync();

    Console.WriteLine("""{"event":"terminal","status":"completed"}""");
    return 0;
}
