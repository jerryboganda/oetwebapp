// Free Mocks (owner 2026-09-22): the paper the server treats as the free sample
// of a subtest carries this tag in its TagsCsv. The server derives "free" from
// the tag itself (ContentEntitlementService.FreeSampleTag) — the client only
// uses it to find the paper and place the FREE SAMPLE card. It is never sent
// back as a flag, so a forged request cannot make another paper free.
export const FREE_SAMPLE_TAG = 'free-sample';

// Exact copy of ContentEntitlementService.FreeSampleFeedback. Attempt-start
// responses carry it in `feedbackMessage` so the caller can skip the
// "1 credit used" toast (which would otherwise read a stale ledger row).
export const FREE_SAMPLE_FEEDBACK = 'Free sample — no credits used.';

export function hasFreeSampleTag(tagsCsv?: string | null): boolean {
  return (tagsCsv ?? '').split(',').some((tag) => tag.trim().toLowerCase() === FREE_SAMPLE_TAG);
}

export function findFreeSamplePaper<T extends { tagsCsv?: string | null }>(
  papers: readonly T[],
): T | null {
  return papers.find((paper) => hasFreeSampleTag(paper.tagsCsv)) ?? null;
}
