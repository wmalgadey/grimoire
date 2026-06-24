import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';

export interface IngestRunStartedPayload {
  runId: string;
  startedAt: string;
  fileCount: number;
}

export interface IngestProgressPayload {
  runId: string;
  filePath: string;
  status: 'Processed' | 'Failed' | 'Skipped';
  chunkCount: number;
  durationMs: number;
  processedSoFar: number;
  totalFiles: number;
  errorMessage?: string;
}

export interface IngestLogEntryPayload {
  runId: string;
  level: 'info' | 'warn' | 'error';
  message: string;
  timestamp: string;
}

export interface FeedbackOption {
  action: 'process' | 'skip' | 'tag';
  label: string;
}

export interface IngestFeedbackRequestPayload {
  runId: string;
  requestId: string;
  filePath: string;
  reason: 'UnknownFormat' | 'Oversized' | 'MissingMetadata';
  options: FeedbackOption[];
}

export interface RunSummaryFile {
  filePath: string;
  status: string;
  chunkCount: number;
  durationMs: number;
  conversationId?: string;
}

export interface RunSummary {
  totalFiles: number;
  processedCount: number;
  failedCount: number;
  skippedCount: number;
  totalChunks: number;
  durationMs: number;
  files: RunSummaryFile[];
}

export interface IngestRunCompletedPayload {
  runId: string;
  status: 'Completed' | 'Failed';
  completedAt: string;
  summary: RunSummary;
}

export interface IngestConversationOpenedPayload {
  conversationId: string;
  runId: string;
  filePath: string;
  openingMessage: string;
  createdAt: string;
}

export interface IngestConversationTurnPayload {
  conversationId: string;
  turnIndex: number;
  role: 'agent' | 'user';
  message: string;
  createdAt: string;
}

class IngestHubService {
  private connection: HubConnection;

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/ingest')
      .withAutomaticReconnect()
      .build();
  }

  async start(): Promise<void> {
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start();
    }
  }

  async stop(): Promise<void> {
    if (this.connection.state !== HubConnectionState.Disconnected) {
      await this.connection.stop();
    }
  }

  onRunStarted(handler: (payload: IngestRunStartedPayload) => void): () => void {
    this.connection.on('IngestRunStarted', handler);
    return () => this.connection.off('IngestRunStarted', handler);
  }

  onProgress(handler: (payload: IngestProgressPayload) => void): () => void {
    this.connection.on('IngestProgress', handler);
    return () => this.connection.off('IngestProgress', handler);
  }

  onLogEntry(handler: (payload: IngestLogEntryPayload) => void): () => void {
    this.connection.on('IngestLogEntry', handler);
    return () => this.connection.off('IngestLogEntry', handler);
  }

  onFeedbackRequest(handler: (payload: IngestFeedbackRequestPayload) => void): () => void {
    this.connection.on('IngestFeedbackRequest', handler);
    return () => this.connection.off('IngestFeedbackRequest', handler);
  }

  onRunCompleted(handler: (payload: IngestRunCompletedPayload) => void): () => void {
    this.connection.on('IngestRunCompleted', handler);
    return () => this.connection.off('IngestRunCompleted', handler);
  }

  onConversationOpened(handler: (payload: IngestConversationOpenedPayload) => void): () => void {
    this.connection.on('IngestConversationOpened', handler);
    return () => this.connection.off('IngestConversationOpened', handler);
  }

  onConversationTurn(handler: (payload: IngestConversationTurnPayload) => void): () => void {
    this.connection.on('IngestConversationTurn', handler);
    return () => this.connection.off('IngestConversationTurn', handler);
  }

  get state(): HubConnectionState {
    return this.connection.state;
  }
}

export const ingestHub = new IngestHubService();
