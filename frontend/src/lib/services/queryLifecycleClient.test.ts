import { expect, test } from 'vitest';
import { applyQueryAnswerChunk, applyQueryTurnChanged } from './queryLifecycleClient';
import type { QueryAnswerChunkEvent, QueryTurnChangedEvent } from '$lib/types';

// Exercises the rules mandated by contracts/query-conversation-api.md ## Rules that the
// component-level QueryConversation test never drives through the actual event shapes:
// answer_chunk applied strictly in sequence order (duplicates/out-of-order ignored), and
// queryTurnChanged applied idempotently by (eventId, turnId).

function chunk(overrides: Partial<QueryAnswerChunkEvent> = {}): QueryAnswerChunkEvent {
	return { turnId: 't-1', sequence: 1, text: 'chunk', ...overrides };
}

test('applyQueryAnswerChunk appends in-order deltas and advances the high-water mark', () => {
	let result = applyQueryAnswerChunk('', chunk({ sequence: 1, text: 'The wiki ' }), 0);
	expect(result).toEqual({ answer: 'The wiki ', lastAppliedSequence: 1 });

	result = applyQueryAnswerChunk(
		result.answer,
		chunk({ sequence: 2, text: 'covers ADRs.' }),
		result.lastAppliedSequence
	);
	expect(result).toEqual({ answer: 'The wiki covers ADRs.', lastAppliedSequence: 2 });
});

test('applyQueryAnswerChunk ignores a duplicate or out-of-order sequence', () => {
	const result = applyQueryAnswerChunk('The wiki ', chunk({ sequence: 1, text: 'DUPLICATE' }), 2);
	expect(result).toEqual({ answer: 'The wiki ', lastAppliedSequence: 2 });
});

function turnChanged(overrides: Partial<QueryTurnChangedEvent> = {}): QueryTurnChangedEvent {
	return {
		eventId: 'evt-1',
		turnId: 't-1',
		fromState: 'running',
		toState: 'completed',
		timestamp: new Date().toISOString(),
		failureReason: null,
		...overrides
	};
}

test('applyQueryTurnChanged applies a new (eventId, turnId) pair once', () => {
	const seen = new Set<string>();
	expect(applyQueryTurnChanged(turnChanged(), seen)).toBe(true);
});

test('applyQueryTurnChanged ignores a repeated (eventId, turnId) pair', () => {
	const seen = new Set<string>();
	const event = turnChanged();
	expect(applyQueryTurnChanged(event, seen)).toBe(true);
	expect(applyQueryTurnChanged(event, seen)).toBe(false);
});

test('applyQueryTurnChanged treats the same eventId for a different turnId as distinct', () => {
	const seen = new Set<string>();
	expect(applyQueryTurnChanged(turnChanged({ eventId: 'evt-1', turnId: 't-1' }), seen)).toBe(true);
	expect(applyQueryTurnChanged(turnChanged({ eventId: 'evt-1', turnId: 't-2' }), seen)).toBe(true);
});
