import { redirect } from 'next/navigation';

/**
 * Final Developer Modification Brief (items 6-8) retired the Reading
 * pathway/drill/error-bank system in favour of the Practice Hub
 * (`/reading/practice`). Nothing in the app links to this route any more —
 * it is kept only as a redirect, not a 404, in case an old bookmark or
 * external link still points here.
 */
export default function ReadingPathwayPage() {
  redirect('/reading/practice');
}
