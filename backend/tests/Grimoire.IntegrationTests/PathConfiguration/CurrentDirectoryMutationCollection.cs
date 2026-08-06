namespace Grimoire.IntegrationTests.PathConfiguration;

/// <summary>
/// 019-fast-test-tier (US2, FR-011/SC-003 flip of <c>parallelizeTestCollections</c>) — a
/// genuine race surfaced by parallelization, not papered over by reverting it (per the
/// spec's own edge case): every test in this collection calls
/// <see cref="System.IO.Directory.SetCurrentDirectory(string)"/> to exercise
/// ambient-cwd-based default path resolution (<c>Grimoire.Hub.Runtime.Paths</c>, ADR-009).
/// <see cref="System.Environment.CurrentDirectory"/> is process-wide, mutable state — while
/// one of these tests has it pointed at its own temp directory, <em>any</em> concurrently
/// running test that builds a host without an explicit content root
/// (<c>WebApplication.CreateBuilder()</c> defaults to <see cref="System.IO.Directory.GetCurrentDirectory"/>)
/// can transiently see that temp directory, including after it has already been deleted —
/// observed in practice as <c>ArgumentException: The content root '...' does not exist</c>
/// in unrelated host-building tests once collection-level parallelism was enabled.
/// <c>DisableParallelization</c> keeps every CWD-mutating test from running alongside any
/// other test in the assembly (verified empirically: collections carrying this attribute
/// run strictly after the parallel-enabled bucket completes, never overlapping it).
/// </summary>
[CollectionDefinition("CurrentDirectoryMutation", DisableParallelization = true)]
public sealed class CurrentDirectoryMutationCollection;
