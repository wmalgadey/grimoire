# Grimoire.ArchTests

## ADR-006 guardrail probe cycle

- Red probe: use Probes/BadGuardrailBypassProbe.cs as a forbidden write target.
- Green verification: AutonomousGuardrailBoundaryTests confirms deny-by-default blocks that path.
- Probe remains in test-only scope as a durable regression check for future changes.
