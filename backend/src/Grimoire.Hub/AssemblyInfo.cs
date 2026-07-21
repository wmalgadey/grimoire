using System.Runtime.CompilerServices;

// Hermetic tests need to simulate the FileSystemWatcher IO failures
// TaskRecordWatcher self-restarts from (contracts/task-record-changed-event.md); the
// OS-level triggers (buffer overflow, watched-directory removal) are not reliably
// reproducible in a sandboxed CI/dev filesystem, so the watcher exposes a narrow
// `internal` test seam instead of introducing a port for a local-filesystem observer
// (Principle I persistence/filesystem exemption).
[assembly: InternalsVisibleTo("Grimoire.IntegrationTests")]
