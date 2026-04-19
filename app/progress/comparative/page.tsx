import { redirect } from 'next/navigation';

/**
 * Phase 6 hard-cutover redirect. The comparative analytics view is now an
 * inline tab on /progress. Old bookmarks land on the trend page; the user
 * can switch to the Comparative tab from there. We keep the route alive
 * (rather than 404) so external links from emails / sponsor reports
 * continue to resolve.
 */
export default function ComparativeAnalyticsRedirect() {
  redirect('/progress');
}
