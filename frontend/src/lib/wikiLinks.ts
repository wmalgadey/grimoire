/**
 * Wiki citations in an agent answer, and the Obsidian links they turn into.
 *
 * The Query system prompt's citation convention is `[[page/path]]` (see
 * QueryConversation.svelte's original note). The Hi-Fi design turns each of those into an
 * inline link in the answer text and lists the touched pages in the thread's right rail:
 * "what has been touched by the query can be opened, and we will create a link to open in
 * obsidian" (chat 1). Grimoire never renders wiki content itself — Obsidian is the reader.
 */

/** `[[page]]` and `[[page|alias]]`; the page part may not contain `]`, `|` or a newline. */
const WIKILINK = /\[\[([^\]|\n]+)(?:\|([^\]\n]*))?\]\]/g;

export interface Citation {
	/** The page path as written between the brackets, trimmed. */
	page: string;
	/** How many times this page is cited across the text it was extracted from. */
	count: number;
}

/**
 * The vault an `obsidian://open` URI addresses. Obsidian resolves a URI without a `vault`
 * parameter against the most recently opened vault, which is the right default for a
 * single-vault install and the only thing we can know from the browser.
 *
 * TODO(backend): the Hub knows its own wiki root and could expose the vault name (e.g. on
 * the existing defaults endpoint) so these links address the vault explicitly instead of
 * relying on "whichever vault was open last".
 */
export function obsidianUri(page: string, vault?: string): string {
	// `encodeURIComponent` deliberately leaves parentheses alone; a markdown link destination
	// does not, and would end early at the first `)` in a page path. Encoding both closes that.
	const file = encodeURIComponent(page.trim()).replace(/\(/g, '%28').replace(/\)/g, '%29');
	return vault
		? `obsidian://open?vault=${encodeURIComponent(vault)}&file=${file}`
		: `obsidian://open?file=${file}`;
}

/** The wiki itself rather than a page in it — what the nav's "Open in Obsidian" points at. */
export function obsidianVaultUri(vault?: string): string {
	return vault ? `obsidian://open?vault=${encodeURIComponent(vault)}` : 'obsidian://open';
}

/** Every distinct page cited in `text`, in first-appearance order, with its citation count. */
export function extractCitations(text: string): Citation[] {
	const counts = new Map<string, number>();
	for (const match of text.matchAll(WIKILINK)) {
		const page = match[1].trim();
		if (!page) continue;
		counts.set(page, (counts.get(page) ?? 0) + 1);
	}
	return [...counts].map(([page, count]) => ({ page, count }));
}

/** The same, across a whole conversation's answers — what the thread's rail lists. */
export function extractConversationCitations(answers: string[]): Citation[] {
	const counts = new Map<string, number>();
	for (const answer of answers) {
		for (const { page, count } of extractCitations(answer)) {
			counts.set(page, (counts.get(page) ?? 0) + count);
		}
	}
	return [...counts].map(([page, count]) => ({ page, count }));
}

export function citationNote(count: number): string {
	return count === 1 ? 'cited once' : count === 2 ? 'cited twice' : `cited ${count} times`;
}

/**
 * Rewrite `[[page]]` citations as markdown links to Obsidian, leaving the rest of the answer
 * untouched so the existing marked → DOMPurify rendering keeps handling it. The `↗` is part
 * of the link text because the design marks every outbound page link with one.
 *
 * `(` `)` in a page path would end the markdown destination early, so the URI is
 * percent-encoded by `obsidianUri` — `encodeURIComponent` covers both.
 */
export function linkifyCitations(markdown: string, vault?: string): string {
	return markdown.replace(WIKILINK, (_full, rawPage: string, alias?: string) => {
		const page = rawPage.trim();
		if (!page) return _full;
		const label = (alias ?? '').trim() || page;
		return `[${label} ↗](${obsidianUri(page, vault)})`;
	});
}
