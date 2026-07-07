import * as signalR from '@microsoft/signalr';
import type { BoardTask, LifecycleEvent } from '$lib/types';
import { listBoard } from './ingestSubmissionsApi';

const HUB_PATH = '/hubs/ingest-lifecycle';

export interface IngestLifecycleClient {
	start(): Promise<void>;
	stop(): Promise<void>;
	onLifecycleChanged(handler: (event: LifecycleEvent) => void): () => void;
	onReconnected(handler: () => void): () => void;
}

/** Thin wrapper around the `taskLifecycleChanged` SignalR channel (contracts/ingest-lifecycle-events.md). */
export function createIngestLifecycleClient(hubUrl: string = HUB_PATH): IngestLifecycleClient {
	const connection = new signalR.HubConnectionBuilder()
		.withUrl(hubUrl)
		.withAutomaticReconnect()
		.build();

	return {
		start: () => connection.start(),
		stop: () => connection.stop(),
		onLifecycleChanged(handler) {
			connection.on('taskLifecycleChanged', handler);
			return () => connection.off('taskLifecycleChanged', handler);
		},
		onReconnected(handler) {
			connection.onreconnected(handler);
			return () => connection.off('reconnected', handler);
		}
	};
}

/**
 * Applies one lifecycle event to a board task list, per contracts/ingest-lifecycle-events.md
 * `## Rules`: events are applied idempotently by `(eventId, taskId)`, and the latest timestamp per
 * `taskId` is authoritative (an out-of-order/stale event is ignored). Pure function — easy to unit
 * test independently of the SignalR transport.
 */
export function applyLifecycleEvent(
	tasks: BoardTask[],
	event: LifecycleEvent,
	seenEventKeys: Set<string>
): BoardTask[] {
	const key = `${event.eventId}:${event.taskId}`;
	if (seenEventKeys.has(key)) {
		return tasks;
	}
	seenEventKeys.add(key);

	const index = tasks.findIndex((t) => t.taskId === event.taskId);
	if (index >= 0 && new Date(event.timestamp) < new Date(tasks[index].updatedAt)) {
		return tasks;
	}

	const updated: BoardTask = {
		taskId: event.taskId,
		status: event.toStatus,
		title: index >= 0 ? tasks[index].title : event.taskId,
		updatedAt: event.timestamp,
		failureReason: event.failureReason,
		taskLink: index >= 0 ? tasks[index].taskLink : `/api/ingest-submissions/${event.taskId}`
	};

	if (index >= 0) {
		const next = [...tasks];
		next[index] = updated;
		return next;
	}
	return [...tasks, updated];
}

export interface BoardLifecycleStream {
	start(): Promise<void>;
	stop(): Promise<void>;
}

/**
 * Bootstraps the board from `GET /api/ingest-submissions`, then applies live
 * `taskLifecycleChanged` events on top, idempotently. On reconnect, refreshes from the REST
 * endpoint before resuming the stream (contracts/ingest-lifecycle-events.md `## Rules`).
 */
export function createBoardLifecycleStream(
	onTasksChanged: (tasks: BoardTask[]) => void,
	options?: { hubUrl?: string; fetchImpl?: typeof fetch; client?: IngestLifecycleClient }
): BoardLifecycleStream {
	let tasks: BoardTask[] = [];
	const seenEventKeys = new Set<string>();
	const client = options?.client ?? createIngestLifecycleClient(options?.hubUrl);

	async function refresh() {
		tasks = await listBoard(options?.fetchImpl);
		onTasksChanged(tasks);
	}

	client.onLifecycleChanged((event) => {
		tasks = applyLifecycleEvent(tasks, event, seenEventKeys);
		onTasksChanged(tasks);
	});
	client.onReconnected(() => {
		void refresh();
	});

	return {
		async start() {
			await refresh();
			await client.start();
		},
		stop: () => client.stop()
	};
}
