import { expect, test } from 'vitest';
import { isRedirect } from '@sveltejs/kit';
import { load } from './+page';

// T080 (Convergence) - `/board` was merged into `/` (T079); this only covers the redirect.
test('load() redirects to the merged submission+board page', () => {
	try {
		load();
		expect.unreachable('load() should have thrown a redirect');
	} catch (error) {
		expect(isRedirect(error)).toBe(true);
		const redirect = error as { status: number; location: string };
		expect(redirect.status).toBe(308);
		expect(redirect.location).toBe('/');
	}
});
