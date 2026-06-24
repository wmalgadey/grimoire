<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import UploadForm from '../components/ingest/UploadForm.svelte';
  import TriggerButton from '../components/ingest/TriggerButton.svelte';
  import StatusFeed from '../components/ingest/StatusFeed.svelte';
  import FeedbackDialog from '../components/ingest/FeedbackDialog.svelte';
  import ConversationPanel from '../components/ingest/ConversationPanel.svelte';
  import BatchSummary from '../components/ingest/BatchSummary.svelte';
  import { ingestHub } from '../services/ingestHub.js';
  import type {
    IngestFeedbackRequestPayload,
    IngestRunStartedPayload,
    IngestRunCompletedPayload,
    IngestConversationOpenedPayload,
    RunSummary,
  } from '../services/ingestHub.js';
  import type { UploadResponse } from '../services/ingestApi.js';

  let currentRunId = $state<string | null>(null);
  let runActive = $state(false);
  let pendingFeedback = $state<IngestFeedbackRequestPayload | null>(null);
  let batchSummary = $state<RunSummary | null>(null);
  let activeConversation = $state<{ conversationId: string; filePath: string } | null>(null);

  function handleRunStarted(payload: IngestRunStartedPayload) {
    currentRunId = payload.runId;
    runActive = true;
    batchSummary = null;
  }

  function handleRunCompleted(payload: IngestRunCompletedPayload) {
    runActive = false;
    batchSummary = payload.summary;
  }

  function handleFeedbackRequest(payload: IngestFeedbackRequestPayload) {
    pendingFeedback = payload;
  }

  function handleConversationOpened(payload: IngestConversationOpenedPayload) {
    // Automatically open the conversation panel when a new conversation starts
    activeConversation = { conversationId: payload.conversationId, filePath: payload.filePath };
  }

  function handleUploaded() {
    // Files uploaded; user can now trigger a run
  }

  let unsubRunStarted: (() => void) | undefined;
  let unsubRunCompleted: (() => void) | undefined;
  let unsubFeedback: (() => void) | undefined;
  let unsubConversationOpened: (() => void) | undefined;

  onMount(async () => {
    await ingestHub.start();
    unsubRunStarted = ingestHub.onRunStarted(handleRunStarted);
    unsubRunCompleted = ingestHub.onRunCompleted(handleRunCompleted);
    unsubFeedback = ingestHub.onFeedbackRequest(handleFeedbackRequest);
    unsubConversationOpened = ingestHub.onConversationOpened(handleConversationOpened);
  });

  onDestroy(async () => {
    unsubRunStarted?.();
    unsubRunCompleted?.();
    unsubFeedback?.();
    unsubConversationOpened?.();
    await ingestHub.stop();
  });
</script>

<main>
  <header>
    <h1>Ingest Agent</h1>
    {#if currentRunId}
      <span class="run-id">Run: <code>{currentRunId}</code></span>
    {/if}
  </header>

  <div class="controls">
    <UploadForm onUploaded={handleUploaded} />
    <div class="trigger-area">
      <TriggerButton {runActive} />
    </div>
  </div>

  <StatusFeed runId={currentRunId} />

  {#if batchSummary}
    <BatchSummary
      summary={batchSummary}
      onDiscuss={(convId, filePath) => (activeConversation = { conversationId: convId, filePath })}
    />
  {/if}

  {#if pendingFeedback}
    <FeedbackDialog
      request={pendingFeedback}
      runId={currentRunId}
      onDismiss={() => (pendingFeedback = null)}
    />
  {/if}

  {#if activeConversation}
    <ConversationPanel
      conversationId={activeConversation.conversationId}
      filePath={activeConversation.filePath}
      onDismiss={() => (activeConversation = null)}
    />
  {/if}
</main>

<style>
  main {
    max-width: 900px;
    margin: 0 auto;
    padding: 1.5rem 1rem;
    display: flex;
    flex-direction: column;
    gap: 1.25rem;
  }

  header {
    display: flex;
    align-items: baseline;
    gap: 1rem;
  }

  h1 {
    margin: 0;
    font-size: 1.6rem;
  }

  .run-id {
    font-size: 0.8rem;
    color: #777;
  }

  .run-id code {
    font-family: monospace;
    background: #f5f5f5;
    padding: 0.1em 0.3em;
    border-radius: 3px;
  }

  .controls {
    display: grid;
    grid-template-columns: 1fr auto;
    gap: 1rem;
    align-items: start;
  }

  .trigger-area {
    padding-top: 2rem;
  }

  @media (max-width: 600px) {
    .controls {
      grid-template-columns: 1fr;
    }

    .trigger-area {
      padding-top: 0;
    }
  }
</style>
