<script lang="ts">
  import { submitFeedback } from '../../services/ingestApi.js';
  import type { IngestFeedbackRequestPayload } from '../../services/ingestHub.js';

  let { request, runId, onDismiss }: {
    request: IngestFeedbackRequestPayload | null;
    runId: string | null;
    onDismiss: () => void;
  } = $props();

  let selectedAction = $state('');
  let tagValue = $state('');
  let submitting = $state(false);
  let errorMessage = $state('');

  const reasonLabels: Record<string, string> = {
    UnknownFormat: 'Unknown file format',
    Oversized: 'File exceeds size limit',
    MissingMetadata: 'Missing required metadata',
  };

  function friendlyReason(reason: string): string {
    return reasonLabels[reason] ?? reason;
  }

  async function handleSubmit(event: SubmitEvent) {
    event.preventDefault();
    if (!request || !runId || !selectedAction) return;

    submitting = true;
    errorMessage = '';

    try {
      await submitFeedback(
        runId,
        request.requestId,
        request.filePath,
        selectedAction,
        selectedAction === 'tag' ? tagValue : undefined,
      );
      onDismiss();
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : 'Feedback submission failed.';
    } finally {
      submitting = false;
    }
  }

  function handleOverlayClick(event: MouseEvent) {
    if (event.target === event.currentTarget) {
      onDismiss();
    }
  }
</script>

{#if request}
  <div class="overlay" onclick={handleOverlayClick} role="dialog" aria-modal="true" aria-label="Feedback required">
    <div class="dialog">
      <h2>Feedback Required</h2>
      <p class="reason">{friendlyReason(request.reason)}</p>
      <p class="filepath"><strong>File:</strong> <code>{request.filePath}</code></p>

      <form onsubmit={handleSubmit}>
        <fieldset>
          <legend>Choose an action</legend>
          {#each request.options as option}
            <label class="radio-option">
              <input
                type="radio"
                name="action"
                value={option.action}
                bind:group={selectedAction}
              />
              {option.label}
            </label>
          {/each}
        </fieldset>

        {#if selectedAction === 'tag'}
          <div class="field">
            <label for="tag-value">Tag value</label>
            <input
              id="tag-value"
              type="text"
              bind:value={tagValue}
              placeholder="Enter tag"
              required
            />
          </div>
        {/if}

        {#if errorMessage}
          <p class="error">{errorMessage}</p>
        {/if}

        <div class="actions">
          <button type="button" onclick={onDismiss} disabled={submitting}>Cancel</button>
          <button type="submit" disabled={submitting || !selectedAction}>
            {submitting ? 'Submitting...' : 'Submit'}
          </button>
        </div>
      </form>
    </div>
  </div>
{/if}

<style>
  .overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
  }

  .dialog {
    background: white;
    border-radius: 8px;
    padding: 1.5rem;
    max-width: 480px;
    width: 90%;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
  }

  h2 {
    margin: 0 0 0.5rem;
    font-size: 1.2rem;
  }

  .reason {
    color: #e65100;
    font-weight: 600;
    margin: 0 0 0.5rem;
  }

  .filepath {
    margin: 0 0 1rem;
    font-size: 0.9rem;
    word-break: break-all;
  }

  .filepath code {
    background: #f5f5f5;
    padding: 0.1em 0.3em;
    border-radius: 3px;
    font-size: 0.85em;
  }

  fieldset {
    border: 1px solid #ddd;
    border-radius: 4px;
    padding: 0.75rem;
    margin: 0 0 0.75rem;
  }

  legend {
    font-size: 0.85rem;
    color: #555;
    padding: 0 0.25rem;
  }

  .radio-option {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0;
    cursor: pointer;
    font-size: 0.9rem;
  }

  .field {
    margin-bottom: 0.75rem;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  .field label {
    font-size: 0.85rem;
    color: #444;
  }

  .field input {
    padding: 0.4rem 0.6rem;
    border: 1px solid #ccc;
    border-radius: 4px;
    font-size: 0.9rem;
  }

  .error {
    font-size: 0.85rem;
    color: #c62828;
    background: #ffebee;
    padding: 0.4rem 0.6rem;
    border-radius: 4px;
    margin: 0 0 0.75rem;
  }

  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 0.5rem;
  }

  .actions button {
    padding: 0.5rem 1.1rem;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 0.9rem;
    transition: background 0.2s;
  }

  .actions button[type='button'] {
    background: #e0e0e0;
    color: #333;
  }

  .actions button[type='button']:hover:not(:disabled) {
    background: #bdbdbd;
  }

  .actions button[type='submit'] {
    background: #1976d2;
    color: white;
  }

  .actions button[type='submit']:hover:not(:disabled) {
    background: #1565c0;
  }

  .actions button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
</style>
