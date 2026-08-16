import * as signalR from '@microsoft/signalr';
import type {
	CompositeBoardResponse,
	ConnectionState,
	RemediationMessageTurnChangedEvent,
	RemediationTaskBoardEntry,
	RemediationTaskLifecycleEvent
} from '$lib/types';
import { presentResponseError } from './apiError';

const HUB_PATH = '/hubs/remediation-lifecycle';
const BOARD_PATH = '/api/board';

export interface RemediationLifecycleClient {
	start(): Promise<void>;
	stop(): Promise<void>;
	onRemediationTaskLifecycleChanged(
		handler: (event: RemediationTaskLifecycleEvent) => void
	): () => void;
	// 015-lint-board-parity T043 (US5): the task detail view's own message-turn stream —
	// clients re-fetch GET .../messages on `completed`/`failed` to render the reply.
	onRemediationMessageTurnChanged(
		handler: (event: RemediationMessageTurnChangedEvent) => void
	): () => void;
	onReconnected(handler: () => void): () => void;
	onConnectionStateChanged(handler: (state: ConnectionState) => void): () => void;
}

/**
 * Thin wrapper around the `remediationTaskLifecycleChanged` SignalR channel
 * (015-lint-board-parity T026, contracts/remediation-lifecycle-events.md "Hub 2:
 * Remediation lifecycle") — mirrors `lintLifecycleClient.ts`; its own hub connection so
 * neither ingest's nor lint's channel is ever touched (FR-015, research.md R1).
 */
export function createRemediationLifecycleClient(
	hubUrl: string = HUB_PATH
): RemediationLifecycleClient {
	const connection = new signalR.HubConnectionBuilder()
		.withUrl(hubUrl)
		.withAutomaticReconnect()
		.build();

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
		onRemediationTaskLifecycleChanged(handler) {
			connection.on('remediationTaskLifecycleChanged', handler);
			return () => connection.off('remediationTaskLifecycleChanged', handler);
		},
		onRemediationMessageTurnChanged(handler) {
			connection.on('remediationMessageTurnChanged', handler);
			return () => connection.off('remediationMessageTurnChanged', handler);
		},
		onReconnected(handler) {
			// Same gating as ingestLifecycleClient: @microsoft/signalr has no unregister
			// API for onreconnected callbacks, so the unsubscribe flips a local flag.
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
 * Fetches the composite board initial state (contracts/lint-board-api.md
 * `GET /api/board`) and extracts the remediation task entries — the board's recovery
 * source for cards proposed before this page load (spec edge case). Other entry kinds
 * are ignored here; ingest and lint keep their own bootstrap paths (FR-015).
 */
export async function fetchRemediationTasksFromBoard(
	fetchImpl: typeof fetch = fetch
): Promise<RemediationTaskBoardEntry[]> {
	const response = await fetchImpl(BOARD_PATH);
	if (!response.ok) {
		// 023 T052: the Hub's own reason, when it sent one — the bare status is the fallback.
		throw new Error((await presentResponseError(response)).message);
	}

	const board: CompositeBoardResponse = await response.json();
	return board.entries.filter((e): e is RemediationTaskBoardEntry => e.kind === 'remediation_task');
}

/**
 * Applies one `remediationTaskLifecycleChanged` event to the current remediation board
 * entries, per contracts/remediation-lifecycle-events.md `## Rules`: idempotent by
 * `(eventId, taskId)`, stale/out-of-order events ignored (latest timestamp per task is
 * authoritative). An event for a task this client has not seen yet cannot be
 * materialized locally (the event carries no proposal title — Principle V keeps that
 * agent text server-side), so `unknownTask` signals the caller to refresh from
 * `GET /api/board`. Pure function — testable independently of the SignalR transport.
 */
export function applyRemediationTaskLifecycleEvent(
	entries: RemediationTaskBoardEntry[],
	event: RemediationTaskLifecycleEvent,
	seenEventKeys: Set<string>
): { entries: RemediationTaskBoardEntry[]; unknownTask: boolean } {
	const key = `${event.eventId}:${event.taskId}`;
	if (seenEventKeys.has(key)) {
		return { entries, unknownTask: false };
	}
	seenEventKeys.add(key);

	const existing = entries.find((e) => e.taskId === event.taskId);
	if (!existing) {
		return { entries, unknownTask: true };
	}

	// Out-of-order guard: latest timestamp per task is authoritative.
	if (new Date(event.timestamp).getTime() < new Date(existing.updatedAt).getTime()) {
		return { entries, unknownTask: false };
	}

	return {
		entries: entries.map((e) =>
			e.taskId === event.taskId
				? {
						...e,
						state: event.toState,
						queuePosition: event.queuePosition,
						outcomeReason: event.outcomeReason,
						updatedAt: event.timestamp
					}
				: e
		),
		unknownTask: false
	};
}

export interface RemediationTaskStream {
	start(): Promise<void>;
	stop(): Promise<void>;
}

/**
 * Bootstraps the board's remediation entries from `GET /api/board`, then applies live
 * `remediationTaskLifecycleChanged` events on top, idempotently; refreshes from the
 * composite response for unknown tasks (new proposals) and on reconnect — the same
 * bootstrap-then-stream rule as `createLintRunStream` (spec edge case: connection drop
 * must recover correct state, SC-002).
 */
export function createRemediationTaskStream(
	onTasksChanged: (tasks: RemediationTaskBoardEntry[]) => void,
	options?: {
		hubUrl?: string;
		fetchImpl?: typeof fetch;
		client?: RemediationLifecycleClient;
	}
): RemediationTaskStream {
	let tasks: RemediationTaskBoardEntry[] = [];
	const seenEventKeys = new Set<string>();
	const client = options?.client ?? createRemediationLifecycleClient(options?.hubUrl);

	async function refresh() {
		tasks = await fetchRemediationTasksFromBoard(options?.fetchImpl);
		onTasksChanged(tasks);
	}

	client.onRemediationTaskLifecycleChanged((event) => {
		const applied = applyRemediationTaskLifecycleEvent(tasks, event, seenEventKeys);
		tasks = applied.entries;
		onTasksChanged(tasks);
		if (applied.unknownTask) {
			// 024 FR-011: a best-effort catch-up read triggered by an event about a task we have
			// not seen. Deliberately silent — the user did not ask for it, and the next event or
			// reconnect re-reads anyway.
			void refresh().catch(() => {});
		}
	});
	client.onReconnected(() => {
		void refresh();
	});

	return {
		async start() {
			// PR #41 review (T052, mirrors T051's lintLifecycleClient fix): the bootstrap
			// refresh and the hub connection are independent failure domains — a
			// transient `/api/board` failure must never prevent the hub from connecting,
			// or live remediation-task updates silently stop until a full page reload
			// (FR-003/SC-002). Run both concurrently; only the hub connection's outcome
			// is fatal to start().
			const [, clientResult] = await Promise.allSettled([refresh(), client.start()]);
			if (clientResult.status === 'rejected') {
				throw clientResult.reason;
			}
		},
		stop: () => client.stop()
	};
}
