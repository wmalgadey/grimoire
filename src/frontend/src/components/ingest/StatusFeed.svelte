<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { ingestHub, type IngestProgressPayload, type IngestLogEntryPayload } from '../../services/ingestHub.js';

  let { runId }: { runId: string | null } = $props();

  interface LogEntry {
    level: 'info' | 'warn' | 'error';
    message: string;
    timestamp: string;
  }

  let currentFile = $state('');
  let processedSoFar = $state(0);
  let totalFiles = $state(0);
  let logEntries = $state<LogEntry[]>([]);
  let logContainer: HTMLElement | undefined;

  let progressPct = $derived(totalFiles > 0 ? Math.round((processedSoFar / totalFiles) * 100) : 0);

  function handleProgress(payload: IngestProgressPayload) {
    if (runId && payload.runId !== runId) return;
    currentFile = payload.filePath;
    processedSoFar = payload.processedSoFar;
    totalFiles = payload.totalFiles;
  }

  function handleLogEntry(payload: IngestLogEntryPayload) {
    if (runId && payload.runId !== runId) return;
    logEntries = [...logEntries, { level: payload.level, message: payload.message, timestamp: payload.timestamp }];
    // Auto-scroll to bottom
    if (logContainer) {
      setTimeout(() => {
        if (logContainer) logContainer.scrollTop = logContainer.scrollHeight;
      }, 0);
    }
  }

  let unsubProgress: (() => void) | undefined;
  let unsubLog: (() => void) | undefined;

  onMount(() => {
    unsubProgress = ingestHub.onProgress(handleProgress);
    unsubLog = ingestHub.onLogEntry(handleLogEntry);
  });

  onDestroy(() => {
    unsubProgress?.();
    unsubLog?.();
  });
</script>

<div class="status-feed">
  <h2>Status</h2>
  {#if !runId}
    <p class="idle">No active run.</p>
  {:else}
    <div class="current-file">
      <span class="label">Processing:</span>
      <span class="filepath">{currentFile || 'Waiting...'}</span>
    </div>
    <div class="progress-bar-container" role="progressbar" aria-valuenow={progressPct} aria-valuemin={0} aria-valuemax={100}>
      <div class="progress-bar" style="width: {progressPct}%"></div>
    </div>
    <div class="progress-label">{processedSoFar} / {totalFiles} files ({progressPct}%)</div>
  {/if}

  <div class="log-container" bind:this={logContainer}>
    {#if logEntries.length === 0}
      <p class="no-logs">No log entries yet.</p>
    {:else}
      {#each logEntries as entry}
        <div class="log-entry {entry.level}">
          <span class="log-time">{new Date(entry.timestamp).toLocaleTimeString()}</span>
          <span class="log-level">[{entry.level.toUpperCase()}]</span>
          <span class="log-message">{entry.message}</span>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .status-feed {
    padding: 1rem;
    border: 1px solid #ddd;
    border-radius: 6px;
    background: #fafafa;
  }

  h2 {
    margin: 0 0 0.75rem;
    font-size: 1.1rem;
  }

  .idle {
    color: #888;
    font-style: italic;
    margin: 0 0 0.75rem;
  }

  .current-file {
    margin-bottom: 0.5rem;
    font-size: 0.9rem;
  }

  .label {
    font-weight: 600;
    margin-right: 0.4rem;
  }

  .filepath {
    font-family: monospace;
    color: #444;
    word-break: break-all;
  }

  .progress-bar-container {
    height: 8px;
    background: #e0e0e0;
    border-radius: 4px;
    overflow: hidden;
    margin-bottom: 0.25rem;
  }

  .progress-bar {
    height: 100%;
    background: #4CAF50;
    transition: width 0.3s ease;
  }

  .progress-label {
    font-size: 0.8rem;
    color: #666;
    margin-bottom: 0.75rem;
  }

  .log-container {
    height: 200px;
    overflow-y: auto;
    background: #1e1e1e;
    border-radius: 4px;
    padding: 0.5rem;
    font-family: monospace;
    font-size: 0.78rem;
  }

  .no-logs {
    color: #666;
    font-style: italic;
    margin: 0;
    font-family: monospace;
    font-size: 0.8rem;
  }

  .log-entry {
    display: flex;
    gap: 0.4rem;
    padding: 0.15rem 0;
    line-height: 1.4;
  }

  .log-time {
    color: #888;
    white-space: nowrap;
    flex-shrink: 0;
  }

  .log-level {
    white-space: nowrap;
    flex-shrink: 0;
    font-weight: 600;
  }

  .log-entry.info .log-level { color: #81c784; }
  .log-entry.warn .log-level { color: #ffb74d; }
  .log-entry.error .log-level { color: #e57373; }

  .log-message {
    color: #e0e0e0;
    word-break: break-word;
  }
</style>
