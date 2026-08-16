/**
 * Rendering agent-authored markdown, sanitized.
 *
 * Same discipline as TaskRecordView.svelte and QueryConversation.svelte have always applied:
 * the content embeds arbitrary source-derived text (Principle V), so it is parsed by `marked`
 * and sanitized by DOMPurify before it reaches the DOM.
 *
 * The one addition is the `obsidian:` scheme. DOMPurify's default URI allow-list covers http,
 * https, mailto and a handful of others — an `obsidian://open?file=…` href would be stripped
 * silently, which is exactly the citation link the design's answers are made of. The regexp
 * below is DOMPurify's own default with `obsidian` added to the scheme group and nothing else
 * changed, so every other scheme stays as blocked as it was.
 */

import DOMPurify from 'dompurify';
import { marked } from 'marked';

const ALLOWED_URI_REGEXP =
	/^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp|obsidian):|[^a-z]|[a-z+.-]+(?:[^a-z+.:-]|$))/i;

export function renderMarkdown(markdown: string): string {
	return DOMPurify.sanitize(marked.parse(markdown, { async: false }) as string, {
		ALLOWED_URI_REGEXP
	});
}
