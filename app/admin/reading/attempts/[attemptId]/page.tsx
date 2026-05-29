'use client';

import { useParams } from 'next/navigation';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { ReadingAttemptReviewScreen } from '@/components/domain/reading/reading-attempt-review-screen';

/**
 * Admin — privileged (non-redacted) reading attempt review.
 *
 * Surfaces the full graded breakdown, manual score override, and tutor
 * feedback for a single reading attempt. The expert console mounts the same
 * shared screen with `area="expert"`.
 */
export default function AdminReadingAttemptReviewPage() {
  const params = useParams();
  const raw = params?.attemptId;
  const attemptId = Array.isArray(raw) ? raw[0] : raw ?? '';

  return (
    <AdminPageShell mainAriaLabel="Reading attempt review">
      <PageHeader
        eyebrow="Reading"
        title="Attempt review"
        description="Full graded breakdown, manual override, and tutor feedback for this reading attempt."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Attempt review' },
        ]}
      />
      {attemptId ? (
        <ReadingAttemptReviewScreen attemptId={attemptId} area="admin" />
      ) : (
        <p className="text-sm text-admin-fg-muted">No attempt id provided.</p>
      )}
    </AdminPageShell>
  );
}
