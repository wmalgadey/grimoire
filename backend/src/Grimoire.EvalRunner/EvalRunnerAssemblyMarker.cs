using System.Runtime.CompilerServices;

// ADR-022 T043: lets Grimoire.AgentEvals assert against the internal CliOptions.Parse
// directly (SC-009 — --recordings-root is unrecognized and has no effect) instead of
// spawning a live process.
[assembly: InternalsVisibleTo("Grimoire.AgentEvals")]

namespace Grimoire.EvalRunner;

/// <summary>Anchor type for assembly-level structural tests (ADR-012 C7/C8).</summary>
public static class EvalRunnerAssemblyMarker;
