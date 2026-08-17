/**
 * The conversations the browser is holding.
 *
 * A Query conversation has always been client-side and ephemeral — "one per browser window"
 * (008 data-model.md), with the Hub sourcing follow-up context from its own Conversation
 * Record (ADR-014). The Hi-Fi design asks for a list you can browse and return to
 * ("if we have 'new conversation', we could change the ask-button into a
 * 'conversation overview'-page", chat 3), which is the same ephemeral state kept for more
 * than one id at a time rather than any new server contract.
 *
 * Consequence, and it is deliberate: reloading the page empties the list. Nothing here is
 * persisted, because nothing about a conversation is the browser's to own.
 * TODO(backend): a `GET /api/query-conversations` listing would make this survive a reload
 * and let a conversation be opened by URL; until then the list is session-scoped.
 */

import { DEFAULT_ASK_MODEL } from '$lib/models';
import type { QueryTurn } from '$lib/types';

export interface Conversation {
	id: string;
	turns: QueryTurn[];
	/**
	 * The model this conversation asks with. Client-side only for now — the submission
	 * contract (contracts/query-conversation-api.md) carries the prompt and nothing else.
	 * TODO(backend): accept a model on the turn submission and drop this local-only field.
	 */
	model: string;
}

function newId(): string {
	return crypto.randomUUID();
}

class ConversationStore {
	/** Newest first — the order the overview lists them in. */
	list: Conversation[] = $state([]);
	activeId: string | null = $state(null);

	get active(): Conversation | null {
		return this.list.find((c) => c.id === this.activeId) ?? null;
	}

	/** Opens a fresh conversation and makes it active; returns its id. */
	create(model: string = DEFAULT_ASK_MODEL): string {
		const conversation: Conversation = { id: newId(), turns: [], model };
		this.list = [conversation, ...this.list];
		this.activeId = conversation.id;
		return conversation.id;
	}

	open(id: string) {
		this.activeId = id;
	}

	setModel(id: string, model: string) {
		this.list = this.list.map((c) => (c.id === id ? { ...c, model } : c));
	}

	addTurn(conversationId: string, turn: QueryTurn) {
		this.list = this.list.map((c) =>
			c.id === conversationId ? { ...c, turns: [...c.turns, turn] } : c
		);
	}

	/** Turn ids are server-issued and unique, so a turn is addressable without its conversation. */
	updateTurn(turnId: string, update: (turn: QueryTurn) => QueryTurn) {
		this.list = this.list.map((c) =>
			c.turns.some((t) => t.turnId === turnId)
				? { ...c, turns: c.turns.map((t) => (t.turnId === turnId ? update(t) : t)) }
				: c
		);
	}

	findTurn(turnId: string): QueryTurn | null {
		for (const conversation of this.list) {
			const turn = conversation.turns.find((t) => t.turnId === turnId);
			if (turn) return turn;
		}
		return null;
	}

	/** Every turn still streaming, in any conversation — what a page unload has to interrupt. */
	get runningTurnIds(): string[] {
		return this.list
			.flatMap((c) => c.turns.filter((t) => t.state === 'running'))
			.map((t) => t.turnId);
	}

	/** Test seam: the store outlives a component, so a test must be able to empty it. */
	reset() {
		this.list = [];
		this.activeId = null;
	}
}

export const conversations = new ConversationStore();

/** The conversation's own running turn — the design allows one question at a time per thread. */
export function activeTurnId(conversation: Conversation | null): string | null {
	return conversation?.turns.find((t) => t.state === 'running')?.turnId ?? null;
}

/** The overview card's heading: the conversation's opening question. */
export function conversationTitle(conversation: Conversation): string {
	return conversation.turns[0]?.prompt ?? 'New conversation';
}

export function conversationNote(conversation: Conversation): string {
	const count = conversation.turns.length;
	if (count === 0) return 'no questions yet';
	const questions = `${count} question${count === 1 ? '' : 's'}`;
	return activeTurnId(conversation) ? `${questions} · answering now` : questions;
}

/**
 * Query-string flag that carries "+ Ask" intent across a navigation. The nav sets it when
 * the action is a link from another screen; `routes/query/+page.svelte` consumes it on
 * mount and strips it again. Declared here so the writer and the reader cannot drift.
 */
export const NEW_CONVERSATION_PARAM = 'new';
