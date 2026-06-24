<script lang="ts">
  import type { RunSummary } from '../../services/ingestHub.js';

  let { summary, onDiscuss }: {
    summary: RunSummary | null;
    onDiscuss: (_conversationId: string, _filePath: string) => void;
  } = $props();

  function formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(1)}s`;
  }

  function statusClass(status: string): string {
    const s = status.toLowerCase();
    if (s === 'processed') return 'status-processed';
    if (s === 'failed') return 'status-failed';
    if (s === 'skipped') return 'status-skipped';
    return '';
  }

  function fileName(filePath: string): string {
    return filePath.split('/').pop() ?? filePath;
  }
</script>

{#if summary}
  <div class="batch-summary">
    <h2>Run Summary</h2>

    <div class="stats">
      <div class="stat">
        <span class="stat-value">{summary.totalFiles}</span>
        <span class="stat-label">Total</span>
      </div>
      <div class="stat processed">
        <span class="stat-value">{summary.processedCount}</span>
        <span class="stat-label">Processed</span>
      </div>
      <div class="stat failed">
        <span class="stat-value">{summary.failedCount}</span>
        <span class="stat-label">Failed</span>
      </div>
      <div class="stat skipped">
        <span class="stat-value">{summary.skippedCount}</span>
        <span class="stat-label">Skipped</span>
      </div>
      <div class="stat">
        <span class="stat-value">{summary.totalChunks}</span>
        <span class="stat-label">Chunks</span>
      </div>
      <div class="stat">
        <span class="stat-value">{formatDuration(summary.durationMs)}</span>
        <span class="stat-label">Duration</span>
      </div>
    </div>

    <div class="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>File</th>
            <th>Status</th>
            <th>Chunks</th>
            <th>Duration</th>
            <th>Action</th>
          </tr>
        </thead>
        <tbody>
          {#each summary.files as file}
            <tr>
              <td class="file-cell" title={file.filePath}>{fileName(file.filePath)}</td>
              <td><span class="status-badge {statusClass(file.status)}">{file.status}</span></td>
              <td class="num">{file.chunkCount}</td>
              <td class="num">{formatDuration(file.durationMs)}</td>
              <td>
                {#if file.status.toLowerCase() === 'processed' && file.conversationId}
                  <button
                    class="discuss-btn"
                    onclick={() => onDiscuss(file.conversationId!, file.filePath)}
                  >
                    Discuss
                  </button>
                {:else}
                  <span class="no-action">—</span>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
{/if}

<style>
  .batch-summary {
    padding: 1rem;
    border: 1px solid #ddd;
    border-radius: 6px;
    background: #fafafa;
  }

  h2 {
    margin: 0 0 0.75rem;
    font-size: 1.1rem;
  }

  .stats {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }

  .stat {
    display: flex;
    flex-direction: column;
    align-items: center;
    background: #fff;
    border: 1px solid #e0e0e0;
    border-radius: 6px;
    padding: 0.5rem 0.9rem;
    min-width: 70px;
  }

  .stat-value {
    font-size: 1.4rem;
    font-weight: 700;
    line-height: 1;
  }

  .stat-label {
    font-size: 0.75rem;
    color: #777;
    margin-top: 0.15rem;
  }

  .stat.processed .stat-value { color: #2e7d32; }
  .stat.failed .stat-value { color: #c62828; }
  .stat.skipped .stat-value { color: #e65100; }

  .table-wrapper {
    overflow-x: auto;
  }

  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.88rem;
  }

  th, td {
    padding: 0.5rem 0.75rem;
    text-align: left;
    border-bottom: 1px solid #e0e0e0;
  }

  th {
    background: #f5f5f5;
    font-weight: 600;
    color: #444;
  }

  .file-cell {
    max-width: 220px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-family: monospace;
    font-size: 0.82rem;
  }

  .num {
    text-align: right;
  }

  .status-badge {
    display: inline-block;
    padding: 0.15em 0.55em;
    border-radius: 10px;
    font-size: 0.8em;
    font-weight: 600;
  }

  .status-processed {
    background: #e8f5e9;
    color: #2e7d32;
  }

  .status-failed {
    background: #ffebee;
    color: #c62828;
  }

  .status-skipped {
    background: #fff3e0;
    color: #e65100;
  }

  .discuss-btn {
    padding: 0.25rem 0.7rem;
    background: #1976d2;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 0.82rem;
    transition: background 0.2s;
  }

  .discuss-btn:hover {
    background: #1565c0;
  }

  .no-action {
    color: #bbb;
  }
</style>
