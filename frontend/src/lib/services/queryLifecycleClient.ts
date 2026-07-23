import * as signalR from '@microsoft/signalr';
import type { ConnectionState, QueryAnswerChunkEvent, QueryTurnChangedEvent } from '$lib/types';

const HUB_PATH = '/hubs/query-lifecycle';

export interface QueryLifecycleClient {
	start(): Promise<void>;
	stop(): Promise<void>;
	onAnswerChunk(handler: (event: QueryAnswerChunkEvent) => void): () => void;
	onTurnChanged(handler: (event: QueryTurnChangedEvent) => void): () => void;
	onReconnected(handler: () => void): () => void;
	onConnectionStateChanged(handler: (state: ConnectionState) => void): () => void;
}

/**
 * Thin wrapper around the `queryAnswerChunk`/`queryTurnChanged` SignalR channels
 * (contracts/query-conversation-api.md) — mirrors ingestLifecycleClient.ts's shape but
 * is structurally independent (research.md R8): its own connection, its own hub route.
 */
export function createQueryLifecycleClient(hubUrl: string = HUB_PATH): QueryLifecycleClient {
	const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).withAutomaticReconnect().build();

	let connectionStateHandler: ((state: ConnectionState) => void) | undefined;
	connection.onreconnecting(() => connectionStateHandler?.('reconnecting'));
	connection.onreconnected(() => connectionStateHandler?.('connected'));
	connection.onclose(() => connectionStateHandler?.('disconnected'));

	return {
		async start() {
			connectionStateHandler?.('connecting');
			try {
				await connection.start();
				connectionStateHandler?.('connected');
			} catch (err) {
				connectionStateHandler?.('disconnected');
				throw err;
			}
		},
		stop: () => connection.stop(),
		onAnswerChunk(handler) {
			connection.on('queryAnswerChunk', handler);
			return () => connection.off('queryAnswerChunk', handler);
		},
		onTurnChanged(handler) {
			connection.on('queryTurnChanged', handler);
			return () => connection.off('queryTurnChanged', handler);
		},
		onReconnected(handler) {
			// No unregister API for onreconnected (mirrors ingestLifecycleClient.ts) — gate
			// behind a local flag so the returned unsubscribe is actually effective.
			let active = true;
			connection.onreconnected(() => {
				if (active) handler();
			});
			return () => {
				active = false;
			};
		},
		onConnectionStateChanged(handler) {
			connectionStateHandler = handler;
			return () => {
				if (connectionStateHandler === handler) {
					connectionStateHandler = undefined;
				}
			};
		}
	};
}

/**
 * Applies one `queryAnswerChunk` delta to a turn's accumulated answer text, in
 * `sequence` order (contracts `## Rules`): out-of-order or duplicate sequences for an
 * already-applied position are ignored. `lastAppliedSequence` is the turn's own
 * high-water mark, tracked by the caller per turnId.
 */
export function applyAnswerChunk(
	currentAnswer: string,
	event: QueryAnswerChunkEvent,
	lastAppliedSequence: number
): { answer: string; lastAppliedSequence: number } {
	if (event.sequence <= lastAppliedSequence) {
		return { answer: currentAnswer, lastAppliedSequence };
	}

	return { answer: currentAnswer + event.text, lastAppliedSequence: event.sequence };
}

/**
 * Applies one `queryTurnChanged` event idempotently by `(eventId, turnId)` — mirrors
 * applyLifecycleEvent's idempotence rule for the ingest lifecycle channel.
 */
export function applyTurnChanged(
	event: QueryTurnChangedEvent,
	seenEventKeys: Set<string>
): boolean {
	const key = `${event.eventId}:${event.turnId}`;
	if (seenEventKeys.has(key)) {
		return false;
	}
	seenEventKeys.add(key);
	return true;
}
