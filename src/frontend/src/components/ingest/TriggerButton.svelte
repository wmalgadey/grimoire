<script lang="ts">
  import { triggerRun } from '../../services/ingestApi.js';

  let { runActive = false }: { runActive?: boolean } = $props();

  let errorMessage = $state('');
  let triggering = $state(false);

  async function handleClick() {
    triggering = true;
    errorMessage = '';
    try {
      const result = await triggerRun();
      if ('error' in result) {
        errorMessage = result.error + (result.activeRunId ? ` (active run: ${result.activeRunId})` : '');
      }
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : 'Failed to trigger run.';
    } finally {
      triggering = false;
    }
  }
</script>

<div class="trigger-button">
  <button
    onclick={handleClick}
    disabled={runActive || triggering}
    aria-busy={triggering}
  >
    {#if triggering}
      Triggering...
    {:else if runActive}
      Run In Progress...
    {:else}
      Trigger Ingest Run
    {/if}
  </button>
  {#if errorMessage}
    <p class="error">{errorMessage}</p>
  {/if}
</div>

<style>
  .trigger-button {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  button {
    padding: 0.6rem 1.5rem;
    background: #4CAF50;
    color: white;
    border: none;
    border-radius: 4px;
    font-size: 1rem;
    cursor: pointer;
    transition: background 0.2s;
  }

  button:hover:not(:disabled) {
    background: #388e3c;
  }

  button:disabled {
    background: #90a4ae;
    cursor: not-allowed;
  }

  .error {
    font-size: 0.85rem;
    color: #c62828;
    background: #ffebee;
    padding: 0.4rem 0.6rem;
    border-radius: 4px;
    margin: 0;
  }
</style>
