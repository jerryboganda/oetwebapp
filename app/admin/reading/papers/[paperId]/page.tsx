'use client';

import { useParams } from 'next/navigation';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { ReadingRecalcPanel } from '@/components/domain/reading/reading-recalc-panel';

/**
 * Admin — reading paper score recalculation.
 *
 * Hosts the recalc control for a paper. Re-grades attempts against the current
 * accepted-answer set while preserving manual overrides.
 */
export default function AdminReadingPaperRecalcPage() {
  const params = useParams();
  const raw = params?.paperId;
  const paperId = Array.isArray(raw) ? raw[0] : raw ?? '';

  return (
    <AdminPageShell mainAriaLabel="Reading paper recalculation">
      <PageHeader
        eyebrow="Reading"
        title="Recalculate scores"
        description="Re-grade attempts for this paper after editing accepted answers."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Recalculate' },
        ]}
      />
      {paperId ? (
        <div className="max-w-2xl">
          <ReadingRecalcPanel paperId={paperId} area="admin" />
        </div>
      ) : (
        <p className="text-sm text-admin-fg-muted">No paper id provided.</p>
      )}
    </AdminPageShell>
  );
}
