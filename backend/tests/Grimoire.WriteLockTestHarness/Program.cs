using Grimoire.AgentRuntime.Guardrails.Coordination;

// T013 (012-query-synthesis-writes, research.md R6): a tiny, real, separate-process test
// harness proving CrossProcessFileLock provides genuine OS-level cross-process exclusion
// — an in-process fake cannot prove this, only a real second process racing the same
// lock file can. No network, no API key: purely local filesystem locking.
//
// Usage: lock-probe <writeLocksDir> <canonicalTargetPath> <backoffCapMs> <holdMs>
//   holdMs >= 0: acquire, print "ACQUIRED", hold for holdMs, release, print "RELEASED".
//   holdMs <  0: acquire, print "ACQUIRED", then block indefinitely (until this process
//                is killed or its stdin is closed) — used by the "killed holder" case.
// Exit code 0 on ACQUIRED (eventually released or killed), 1 on TIMEOUT, 2 on bad args.

if (args.Length != 5 || args[0] != "lock-probe")
{
    await Console.Error.WriteLineAsync(
        "Usage: lock-probe <writeLocksDir> <canonicalTargetPath> <backoffCapMs> <holdMs>");
    return 2;
}

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
