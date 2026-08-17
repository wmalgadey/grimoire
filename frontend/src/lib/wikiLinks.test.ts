import { expect, test } from 'vitest';
import {
	citationNote,
	extractCitations,
	extractConversationCitations,
	linkifyCitations,
	obsidianUri
} from './wikiLinks';

// The design's two citation surfaces read from the same parse: the inline links inside an
// answer, and the list of touched pages in the thread's rail. Both are derived from the
// agent's own `[[page]]` convention — nothing about which pages an answer touched is invented
// on the client beyond counting what it cited.

test('extracts each cited page once, with its count, in first-appearance order', () => {
	const answer =
		'Decided in [[policies/retention]], restated with the backup caveat in [[ops/backups]] ' +
		'and again in [[policies/retention]].';

	expect(extractCitations(answer)).toEqual([
		{ page: 'policies/retention', count: 2 },
		{ page: 'ops/backups', count: 1 }
	]);
});

test('ignores an empty citation and trims surrounding space', () => {
	expect(extractCitations('[[  spaced/page  ]] and [[]]')).toEqual([
		{ page: 'spaced/page', count: 1 }
	]);
});

test('sums citations across every answer in a conversation', () => {
	expect(extractConversationCitations(['See [[a]] and [[b]].', 'Also [[a]].', ''])).toEqual([
		{ page: 'a', count: 2 },
		{ page: 'b', count: 1 }
	]);
});

test('rewrites a citation as an Obsidian link and leaves the rest of the answer alone', () => {
	const linked = linkifyCitations('Sources are kept for 90 days ([[policies/retention]]).');

	expect(linked).toBe(
		'Sources are kept for 90 days ([policies/retention ↗](obsidian://open?file=policies%2Fretention)).'
	);
});

test('an aliased citation keeps the alias as its link text', () => {
	expect(linkifyCitations('[[ops/backups|the backup note]]')).toBe(
		'[the backup note ↗](obsidian://open?file=ops%2Fbackups)'
	);
});

test('a page path with parentheses cannot break out of the markdown link', () => {
	// Unencoded, `)` would end the link destination early and the rest would leak as text.
	expect(linkifyCitations('[[notes/foo (draft)]]')).toBe(
		'[notes/foo (draft) ↗](obsidian://open?file=notes%2Ffoo%20%28draft%29)'
	);
});

test('a vault is addressed explicitly when one is known', () => {
	expect(obsidianUri('a/b', 'wiki')).toBe('obsidian://open?vault=wiki&file=a%2Fb');
});

test('the rail note reads naturally for one, two and many citations', () => {
	expect(citationNote(1)).toBe('cited once');
	expect(citationNote(2)).toBe('cited twice');
	expect(citationNote(5)).toBe('cited 5 times');
});
