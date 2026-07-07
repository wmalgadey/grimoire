import * as signalR from '@microsoft/signalr';
import type { LifecycleEvent } from '$lib/types';

const HUB_PATH = '/hubs/ingest-lifecycle';

export interface IngestLifecycleClient {
	start(): Promise<void>;
	stop(): Promise<void>;
	onLifecycleChanged(handler: (event: LifecycleEvent) => void): () => void;
}

/**
 * Thin wrapper around the `taskLifecycleChanged` SignalR channel
 * (contracts/ingest-lifecycle-events.md). Idempotent event application and the
 * reconnect-then-refresh flow are layered on in `createBoardLifecycleStream` (T044, US2) —
 * this module only owns the connection lifecycle and raw event delivery.
 */
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
		}
	};
}
