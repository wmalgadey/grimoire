<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { ingestHub, type IngestConversationTurnPayload } from '../../services/ingestHub.js';
  import { getConversation, sendConversationMessage, type ConversationTurn } from '../../services/ingestApi.js';

  let { conversationId, filePath, onDismiss }: {
    conversationId: string | null;
    filePath?: string;
    onDismiss: () => void;
  } = $props();

  let turns = $state<ConversationTurn[]>([]);
  let messageInput = $state('');
  let sending = $state(false);
  let errorMessage = $state('');
  let loading = $state(false);
  let messagesContainer: HTMLElement | undefined;

  function scrollToBottom() {
    if (messagesContainer) {
      setTimeout(() => {
        if (messagesContainer) messagesContainer.scrollTop = messagesContainer.scrollHeight;
      }, 0);
    }
  }

  function handleConversationTurn(payload: IngestConversationTurnPayload) {
    if (payload.conversationId !== conversationId) return;
    // Only add if not already present (avoid duplicates with optimistic updates)
    const exists = turns.some(t => t.turnIndex === payload.turnIndex && t.role === payload.role);
    if (!exists) {
      turns = [...turns, {
        turnIndex: payload.turnIndex,
        role: payload.role,
        message: payload.message,
        createdAt: payload.createdAt,
      }];
      scrollToBottom();
    }
  }

  async function loadHistory() {
    if (!conversationId) return;
    loading = true;
    errorMessage = '';
    try {
      const conv = await getConversation(conversationId);
      turns = conv.turns ?? [];
      scrollToBottom();
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : 'Failed to load conversation.';
    } finally {
      loading = false;
    }
  }

  async function handleSend(event: SubmitEvent) {
    event.preventDefault();
    const msg = messageInput.trim();
    if (!msg || !conversationId) return;

    sending = true;
    errorMessage = '';
    messageInput = '';

    // Optimistic update
    const optimisticTurn: ConversationTurn = {
      turnIndex: turns.length,
      role: 'user',
      message: msg,
      createdAt: new Date().toISOString(),
    };
    turns = [...turns, optimisticTurn];
    scrollToBottom();

    try {
      const returnedTurn = await sendConversationMessage(conversationId, msg);
      const exists = turns.some(
        t => t.turnIndex === returnedTurn.turnIndex && t.role === returnedTurn.role,
      );
      if (!exists) {
        turns = [...turns, {
          turnIndex: returnedTurn.turnIndex,
          role: returnedTurn.role,
          message: returnedTurn.message,
          createdAt: returnedTurn.createdAt,
        }];
        scrollToBottom();
      }
    } catch (err) {
      errorMessage = err instanceof Error ? err.message : 'Failed to send message.';
      // Remove optimistic entry on failure
      turns = turns.filter(t => t !== optimisticTurn);
    } finally {
      sending = false;
    }
  }

  let unsubTurn: (() => void) | undefined;

  onMount(async () => {
    unsubTurn = ingestHub.onConversationTurn(handleConversationTurn);
    await loadHistory();
  });

  onDestroy(() => {
    unsubTurn?.();
  });
</script>

<div class="panel-overlay" onclick={(e) => e.target === e.currentTarget && onDismiss()} role="dialog" aria-modal="true" aria-label="Conversation panel">
  <div class="panel">
    <div class="panel-header">
      <div class="header-text">
        <h2>Conversation</h2>
        {#if filePath}
          <p class="filepath">{filePath}</p>
        {/if}
      </div>
      <button class="close-btn" onclick={onDismiss} aria-label="Close conversation">✕</button>
    </div>

    <div class="messages" bind:this={messagesContainer}>
      {#if loading}
        <p class="loading">Loading conversation...</p>
      {:else if turns.length === 0}
        <p class="empty">No messages yet.</p>
      {:else}
        {#each turns as turn}
          <div class="turn {turn.role}">
            <div class="bubble">
              <span class="role-label">{turn.role === 'agent' ? 'Agent' : 'You'}</span>
              <p class="message">{turn.message}</p>
              <span class="timestamp">{new Date(turn.createdAt).toLocaleTimeString()}</span>
            </div>
          </div>
        {/each}
      {/if}
    </div>

    {#if errorMessage}
      <p class="error">{errorMessage}</p>
    {/if}

    <form class="input-row" onsubmit={handleSend}>
      <input
        type="text"
        bind:value={messageInput}
        placeholder="Type a message..."
        disabled={sending || !conversationId}
        aria-label="Message input"
      />
      <button type="submit" disabled={sending || !messageInput.trim() || !conversationId}>
        {sending ? '...' : 'Send'}
      </button>
    </form>
  </div>
</div>

<style>
  .panel-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.4);
    display: flex;
    align-items: stretch;
    justify-content: flex-end;
    z-index: 900;
  }

  .panel {
    width: 400px;
    max-width: 100%;
    background: white;
    display: flex;
    flex-direction: column;
    box-shadow: -4px 0 16px rgba(0, 0, 0, 0.15);
  }

  .panel-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    padding: 1rem;
    border-bottom: 1px solid #e0e0e0;
  }

  .header-text h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  .header-text .filepath {
    margin: 0.25rem 0 0;
    font-size: 0.8rem;
    color: #666;
    font-family: monospace;
    word-break: break-all;
  }

  .close-btn {
    background: none;
    border: none;
    font-size: 1.1rem;
    cursor: pointer;
    color: #666;
    padding: 0.2rem 0.4rem;
    border-radius: 4px;
    transition: background 0.2s;
    flex-shrink: 0;
  }

  .close-btn:hover {
    background: #f5f5f5;
    color: #333;
  }

  .messages {
    flex: 1;
    overflow-y: auto;
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }

  .loading, .empty {
    color: #888;
    font-style: italic;
    font-size: 0.9rem;
    text-align: center;
    margin: auto;
  }

  .turn {
    display: flex;
  }

  .turn.agent {
    justify-content: flex-start;
  }

  .turn.user {
    justify-content: flex-end;
  }

  .bubble {
    max-width: 75%;
    padding: 0.6rem 0.8rem;
    border-radius: 12px;
    display: flex;
    flex-direction: column;
    gap: 0.2rem;
  }

  .turn.agent .bubble {
    background: #e3f2fd;
    border-bottom-left-radius: 4px;
  }

  .turn.user .bubble {
    background: #e8f5e9;
    border-bottom-right-radius: 4px;
  }

  .role-label {
    font-size: 0.7rem;
    font-weight: 700;
    color: #777;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  .message {
    margin: 0;
    font-size: 0.9rem;
    white-space: pre-wrap;
    word-break: break-word;
  }

  .timestamp {
    font-size: 0.7rem;
    color: #aaa;
    align-self: flex-end;
  }

  .error {
    font-size: 0.8rem;
    color: #c62828;
    background: #ffebee;
    padding: 0.4rem 0.75rem;
    margin: 0;
  }

  .input-row {
    display: flex;
    gap: 0.5rem;
    padding: 0.75rem;
    border-top: 1px solid #e0e0e0;
  }

  .input-row input {
    flex: 1;
    padding: 0.5rem 0.75rem;
    border: 1px solid #ccc;
    border-radius: 20px;
    font-size: 0.9rem;
    outline: none;
  }

  .input-row input:focus {
    border-color: #1976d2;
  }

  .input-row button {
    padding: 0.5rem 1rem;
    background: #1976d2;
    color: white;
    border: none;
    border-radius: 20px;
    cursor: pointer;
    font-size: 0.9rem;
    transition: background 0.2s;
    white-space: nowrap;
  }

  .input-row button:hover:not(:disabled) {
    background: #1565c0;
  }

  .input-row button:disabled {
    background: #90a4ae;
    cursor: not-allowed;
  }
</style>
