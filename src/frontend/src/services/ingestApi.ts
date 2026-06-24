export interface UploadResponse {
  runId?: string;
  fileCount: number;
  uploadedFiles: string[];
}

export interface TriggerResponse {
  runId: string;
  status: string;
}

export interface TriggerConflictResponse {
  error: string;
  activeRunId?: string;
}

export interface ConversationTurn {
  turnIndex: number;
  role: 'agent' | 'user';
  message: string;
  createdAt: string;
}

export interface ConversationTurnResponse {
  conversationId: string;
  turnIndex: number;
  role: string;
  message: string;
}

export interface ConversationResponse {
  conversationId: string;
  filePath: string;
  turns: ConversationTurn[];
}

export async function uploadFiles(files: File[], subDirectory?: string): Promise<UploadResponse> {
  const formData = new FormData();
  for (const file of files) {
    formData.append('files', file);
  }
  if (subDirectory) {
    formData.append('subDirectory', subDirectory);
  }

  const response = await fetch('/api/ingest/upload', {
    method: 'POST',
    body: formData,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Upload failed (${response.status}): ${text}`);
  }

  return response.json();
}

export async function triggerRun(runId?: string): Promise<TriggerResponse | TriggerConflictResponse> {
  const response = await fetch('/api/ingest/trigger', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(runId ? { runId } : {}),
  });

  if (response.status === 409) {
    const body = await response.json();
    return { error: body.error ?? 'A run is already active', activeRunId: body.activeRunId };
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Trigger failed (${response.status}): ${text}`);
  }

  return response.json();
}

export async function submitFeedback(
  runId: string,
  requestId: string,
  filePath: string,
  action: string,
  tagValue?: string,
): Promise<void> {
  const response = await fetch(`/api/ingest/runs/${encodeURIComponent(runId)}/feedback`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ requestId, filePath, action, tagValue }),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Feedback submission failed (${response.status}): ${text}`);
  }
}

export async function sendConversationMessage(
  conversationId: string,
  message: string,
): Promise<ConversationTurnResponse> {
  const response = await fetch(
    `/api/ingest/conversations/${encodeURIComponent(conversationId)}/messages`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message }),
    },
  );

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Send message failed (${response.status}): ${text}`);
  }

  return response.json();
}

export async function getConversation(conversationId: string): Promise<ConversationResponse> {
  const response = await fetch(
    `/api/ingest/conversations/${encodeURIComponent(conversationId)}`,
  );

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Get conversation failed (${response.status}): ${text}`);
  }

  return response.json();
}
