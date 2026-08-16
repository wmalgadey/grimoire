/**
 * The models offered where the design asks for a model choice: the Ingest dialog, the Run
 * Lint popover, and the Ask composer ("we should add to ingest, '+ ask' and 'run lint' ui …
 * a model selection", chat 3), with the defaults it settled on — Sonnet for ingest and
 * conversations, Opus for Lint.
 *
 * TODO(backend): none of the three submission contracts carries a model today, so a choice
 * made here is remembered in the browser and not sent. This list is the client's placeholder
 * until the Hub exposes the models it can dispatch to (an endpoint would also stop this
 * hard-coded list from drifting from what the harness actually supports).
 */

export const MODELS = ['Claude Haiku 4.5', 'Claude Sonnet 4.5', 'Claude Opus 4.1'] as const;

export const MODEL_NOTES: Record<string, string> = {
	'Claude Haiku 4.5': 'fastest, for short sources',
	'Claude Sonnet 4.5': 'default',
	'Claude Opus 4.1': 'deepest reading, slowest'
};

export const DEFAULT_MODEL = 'Claude Sonnet 4.5';
export const DEFAULT_INGEST_MODEL = DEFAULT_MODEL;
export const DEFAULT_ASK_MODEL = DEFAULT_MODEL;
export const DEFAULT_LINT_MODEL = 'Claude Opus 4.1';
