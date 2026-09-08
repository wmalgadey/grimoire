/**
 * The model the harness dispatches to, stated rather than offered.
 *
 * This was a three-model picker in the Ingest dialog, the Run Lint popover and the Ask
 * composer. Since #117 was decided on 2026-08-18 — Haiku 4.5 as the deliberate default *and
 * floor* — two of those were choices the project had already declined, and the defaults the
 * picker shipped (Sonnet for ingest and conversations, Opus for Lint) named models nothing
 * runs. The choice was never transmitted either: no submission contract carries a model, so a
 * pick was remembered in the browser and dropped. A control that offers options the harness
 * will not honour is worse than no control (#149), so the surfaces now state what will
 * actually run and offer nothing.
 *
 * This is the client's statement of the #117 decision, not a reading of the deployment. The
 * harness resolves the real id per agent from GRIMOIRE_INGEST_MODEL / _QUERY_ / _LINT_ and
 * fails closed naming the variable when one is unset (PR #142), so an operator who overrides
 * one of those makes this line stale — the browser has no way to know. Making it authoritative
 * needs the Hub to expose what it can dispatch to, which is #84's endpoint, along with real
 * per-run model selection. Until then a hand-maintained constant that names the one decided
 * model is the honest interim: wrong only where an operator has deliberately diverged, instead
 * of wrong by construction on every surface.
 */
export const ACTIVE_MODEL = 'claude-haiku-4-5';
