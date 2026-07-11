import { redirect } from '@sveltejs/kit';

// T079 (Convergence) - the board was merged into the submission page (`/`) so it's visible
// immediately when ingesting a new source, instead of requiring a separate page visit.
export function load() {
	redirect(308, '/');
}
