<script lang="ts">
  import { uploadFiles, type UploadResponse } from '../../services/ingestApi.js';

  let { onUploaded }: { onUploaded?: (result: UploadResponse) => void } = $props();

  let files = $state<File[]>([]);
  let subDirectory = $state('');
  let uploading = $state(false);
  let statusMessage = $state('');
  let statusKind = $state<'success' | 'error' | ''>('');
  let dragging = $state(false);

  function handleFileInput(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      files = Array.from(input.files);
      statusMessage = '';
      statusKind = '';
    }
  }

  function handleDragOver(event: DragEvent) {
    event.preventDefault();
    dragging = true;
  }

  function handleDragLeave() {
    dragging = false;
  }

  function handleDrop(event: DragEvent) {
    event.preventDefault();
    dragging = false;
    if (event.dataTransfer?.files) {
      files = Array.from(event.dataTransfer.files);
      statusMessage = '';
      statusKind = '';
    }
  }

  async function handleSubmit(event: SubmitEvent) {
    event.preventDefault();
    if (files.length === 0) {
      statusMessage = 'Please select at least one file.';
      statusKind = 'error';
      return;
    }

    uploading = true;
    statusMessage = '';
    statusKind = '';

    try {
      const result = await uploadFiles(files, subDirectory || undefined);
      statusMessage = `Uploaded ${result.fileCount} file(s) successfully.`;
      statusKind = 'success';
      files = [];
      subDirectory = '';
      onUploaded?.(result);
    } catch (err) {
      statusMessage = err instanceof Error ? err.message : 'Upload failed.';
      statusKind = 'error';
    } finally {
      uploading = false;
    }
  }
</script>

<div class="upload-form">
  <h2>Upload Files</h2>
  <form onsubmit={handleSubmit}>
    <div
      class="drop-zone"
      class:dragging
      ondragover={handleDragOver}
      ondragleave={handleDragLeave}
      ondrop={handleDrop}
      role="region"
      aria-label="File drop zone"
    >
      {#if files.length > 0}
        <ul class="file-list">
          {#each files as file}
            <li>{file.name} <span class="file-size">({(file.size / 1024).toFixed(1)} KB)</span></li>
          {/each}
        </ul>
      {:else}
        <p>Drag and drop files here, or click to select</p>
      {/if}
      <input
        type="file"
        multiple
        onchange={handleFileInput}
        class="file-input"
        aria-label="Select files to upload"
      />
    </div>

    <div class="field">
      <label for="sub-directory">Sub-directory (optional)</label>
      <input
        id="sub-directory"
        type="text"
        bind:value={subDirectory}
        placeholder="e.g. docs/2024"
      />
    </div>

    {#if statusMessage}
      <p class="status" class:success={statusKind === 'success'} class:error={statusKind === 'error'}>
        {statusMessage}
      </p>
    {/if}

    <button type="submit" disabled={uploading || files.length === 0}>
      {uploading ? 'Uploading...' : 'Upload'}
    </button>
  </form>
</div>

<style>
  .upload-form {
    padding: 1rem;
    border: 1px solid #ddd;
    border-radius: 6px;
    background: #fafafa;
  }

  h2 {
    margin: 0 0 1rem;
    font-size: 1.1rem;
  }

  .drop-zone {
    position: relative;
    border: 2px dashed #aaa;
    border-radius: 6px;
    padding: 1.5rem;
    text-align: center;
    cursor: pointer;
    transition: border-color 0.2s, background 0.2s;
    min-height: 80px;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .drop-zone.dragging {
    border-color: #4CAF50;
    background: #f0fff0;
  }

  .drop-zone p {
    margin: 0;
    color: #666;
  }

  .file-input {
    position: absolute;
    inset: 0;
    opacity: 0;
    cursor: pointer;
    width: 100%;
    height: 100%;
  }

  .file-list {
    list-style: none;
    margin: 0;
    padding: 0;
    text-align: left;
    width: 100%;
  }

  .file-list li {
    padding: 0.2rem 0;
    font-size: 0.9rem;
  }

  .file-size {
    color: #888;
    font-size: 0.8em;
  }

  .field {
    margin-top: 0.75rem;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  .field label {
    font-size: 0.85rem;
    color: #444;
  }

  .field input {
    padding: 0.4rem 0.6rem;
    border: 1px solid #ccc;
    border-radius: 4px;
    font-size: 0.9rem;
  }

  .status {
    margin-top: 0.5rem;
    font-size: 0.85rem;
    padding: 0.4rem 0.6rem;
    border-radius: 4px;
  }

  .status.success {
    background: #e8f5e9;
    color: #2e7d32;
  }

  .status.error {
    background: #ffebee;
    color: #c62828;
  }

  button[type='submit'] {
    margin-top: 0.75rem;
    padding: 0.5rem 1.25rem;
    background: #1976d2;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 0.95rem;
    transition: background 0.2s;
  }

  button[type='submit']:hover:not(:disabled) {
    background: #1565c0;
  }

  button[type='submit']:disabled {
    background: #90a4ae;
    cursor: not-allowed;
  }
</style>
