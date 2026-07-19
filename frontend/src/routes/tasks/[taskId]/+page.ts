import type { PageLoad } from './$types';

// 006: the rendered task-detail route (research.md Decision 7) — the record itself is
// fetched client-side by +page.svelte via getTaskRecord() so it can be refetched live
// (US3); this load only threads the route param through.
export const load: PageLoad = ({ params }) => {
	return { taskId: params.taskId };
};
