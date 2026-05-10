import { redirect } from 'next/navigation';

/**
 * `/listening/results` has no meaningful index — individual results are at
 * `/listening/results/[id]`. This page exists only so that the LearnerBreadcrumbs
 * intermediate link (`/listening/results`) doesn't 404 and trigger a RSC prefetch
 * console error. Redirect visitors to the Listening home.
 */
export default function ListeningResultsIndexPage() {
  redirect('/listening');
}
