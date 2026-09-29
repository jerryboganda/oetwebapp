import { describe, expect, it, vi } from 'vitest';
import { redirect } from 'next/navigation';
import ReadingPathwayPage from './page';

vi.mock('next/navigation', () => ({
  redirect: vi.fn(),
}));

// Final Developer Modification Brief items 6-8: the pathway/drill/error-bank
// system is retired. This orphaned route must never again render its stale
// "Drill your weakest skills / Error Bank" prose — it redirects instead.
describe('Reading pathway page (retired)', () => {
  it('redirects to /reading/practice', () => {
    ReadingPathwayPage();
    expect(redirect).toHaveBeenCalledWith('/reading/practice');
  });
});
