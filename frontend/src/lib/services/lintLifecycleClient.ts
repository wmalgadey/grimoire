import * as signalR from '@microsoft/signalr';
import type {
	CompositeBoardResponse,
	ConnectionState,
	LintRun,
	LintRunBoardEntry,
	LintRunLifecycleEvent
} from '$lib/types';
import { parseHttpErrorMessage } from './httpErrorMessage';

const HUB_PATH = '/hubs/lint-lifecycle';
const BOARD_PATH = '/api/board';

export interface LintLifecycleClient {
	start(): Promise<void>;
	stop(): Promise<void>;
	onLintRunLifecycleChanged(handler: (event: LintRunLifecycleEvent) => void): () => void;
	onReconnected(handler: () => void): () => void;
	onConnectionStateChanged(handler: (state: ConnectionState) => void): () => void;
}

/**
 * Thin wrapper around the `lintRunLifecycleChanged` SignalR channel
 * (015-lint-board-parity T013, contracts/remediation-lifecycle-events.md "Hub 1: Lint
 * lifecycle") — mirrors `ingestLifecycleClient.ts`; its own hub connection so ingest's
 * channel is never touched (FR-015, research.md R1).
 */
export function createLintLifecycleClient(hubUrl: string = HUB_PATH): LintLifecycleClient {
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
		onLintRunLifecycleChanged(handler) {
			connection.on('lintRunLifecycleChanged', handler);
			return () => connection.off('lintRunLifecycleChanged', handler);
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
 * `GET /api/board`) and extracts the latest lint run entry — `null` when no lint run has
 * ever been triggered (the board then shows its "no lint activity yet" state, US1
 * scenario 1). Ingest entries are ignored here: the ingest board keeps its own,
 * unchanged bootstrap path (FR-015).
 */
export async function fetchLintRunFromBoard(
	fetchImpl: typeof fetch = fetch
): Promise<LintRun | null> {
	const response = await fetchImpl(BOARD_PATH);
	if (!response.ok) {
		// 023 T052: the Hub's own reason, when it sent one — the bare status is the fallback.
		throw new Error(await parseHttpErrorMessage(response));
	}

	const board: CompositeBoardResponse = await response.json();
	const entry = board.entries.find((e): e is LintRunBoardEntry => e.kind === 'lint_run');
	if (!entry) return null;

	return {
		runId: entry.runId,
		status: entry.status,
		triggeredAt: entry.triggeredAt,
		completedAt: entry.completedAt,
		failureReason: entry.failureReason,
		hasFindingsReport: entry.hasFindingsReport
	};
}

/**
 * Applies one `lintRunLifecycleChanged` event to the board's current lint run view, per
 * contracts/remediation-lifecycle-events.md `## Rules`: idempotent by `(eventId, runId)`,
 * stale/out-of-order events ignored. Pure function — testable independently of the
 * SignalR transport (mirrors `applyLifecycleEvent`).
 */
export function applyLintRunLifecycleEvent(
	run: LintRun | null,
	event: LintRunLifecycleEvent,
	seenEventKeys: Set<string>
): LintRun | null {
	const key = `${event.eventId}:${event.runId}`;
	if (seenEventKeys.has(key)) {
		return run;
	}
	seenEventKeys.add(key);

	if (run && run.runId === event.runId) {
		// Out-of-order guard: a `running` event arriving after the run already reached a
		// terminal state is stale (latest state per run is authoritative).
		if (run.status !== 'running' && event.toStatus === 'running') {
			return run;
		}

		return {
			...run,
			status: event.toStatus,
			completedAt: event.toStatus === 'running' ? null : event.timestamp,
			failureReason: event.failureReason
		};
	}

	// A run this client has not seen yet (e.g. triggered from the /lint page after the
	// board bootstrapped — spec edge case, SC-001): materialize it from the event.
	return {
		runId: event.runId,
		status: event.toStatus,
		triggeredAt: event.timestamp,
		completedAt: event.toStatus === 'running' ? null : event.timestamp,
		failureReason: event.failureReason,
		hasFindingsReport: false
	};
}

export interface LintRunStream {
	start(): Promise<void>;
	stop(): Promise<void>;
}

/**
 * Bootstraps the board's lint run view from `GET /api/board`, then applies live
 * `lintRunLifecycleChanged` events on top, idempotently. On reconnect, refreshes from
 * the composite response before resuming the stream (same bootstrap-then-stream rule as
 * `createBoardLifecycleStream`, spec edge case: connection drop must recover correct
 * state, SC-002).
 */
export function createLintRunStream(
	onRunChanged: (run: LintRun | null) => void,
	options?: {
		hubUrl?: string;
		fetchImpl?: typeof fetch;
		client?: LintLifecycleClient;
	}
): LintRunStream {
	let run: LintRun | null = null;
	const seenEventKeys = new Set<string>();
	const client = options?.client ?? createLintLifecycleClient(options?.hubUrl);

	async function refresh() {
		run = await fetchLintRunFromBoard(options?.fetchImpl);
		onRunChanged(run);
	}

	client.onLintRunLifecycleChanged((event) => {
		run = applyLintRunLifecycleEvent(run, event, seenEventKeys);
		onRunChanged(run);
	});
	client.onReconnected(() => {
		void refresh();
	});

	return {
		async start() {
			// PR #41 review (T051): the bootstrap refresh and the hub connection are
			// independent failure domains — a transient `/api/board` failure must never
			// prevent the hub from connecting, or live updates silently stop until a full
			// page reload (FR-003/SC-001/SC-002). Run both concurrently; only the hub
			// connection's outcome is fatal to start() (the board page's own `.catch`
			// treats refresh as best-effort, matching `fetchLintRunFromBoard`'s callers
			// elsewhere already tolerating a failed initial fetch).
			const [, clientResult] = await Promise.allSettled([refresh(), client.start()]);
			if (clientResult.status === 'rejected') {
				throw clientResult.reason;
			}
		},
		stop: () => client.stop()
	};
}
