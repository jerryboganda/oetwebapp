'use client';

/**
 * Mocks Module Phase 6 — admin Quality-Control review-pipeline page.
 *
 * Hosts the multi-stage editorial pipeline UI for a single mock bundle.
 * The heavy lifting (timeline, transitions, advance form) lives in
 * <MockReviewStageRail/>; this page just wires the bundleId route param
 * into the standard `AdminRouteWorkspace` chrome.
 */

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, ListChecks } from 'lucide-react';

import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { MockReviewStageRail } from '@/components/domain/admin/MockReviewStageRail';

export default function MockBundleReviewPipelinePage() {
  const params = useParams<{ bundleId: string }>();
  const bundleId = params?.bundleId ?? '';

  return (
    <AdminOperationsLayout
      eyebrow="Mock bundle"
      title="Editorial review pipeline"
      description="Walk a bundle from academic review through to published. Stage transitions are monotonic — the backend rejects regressions."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Mocks', href: '/admin/content/mocks' },
        { label: 'Review pipeline' },
      ]}
      actions={
        <div className="flex items-center gap-2">
          <Button asChild variant="ghost" size="sm">
            <Link href={`/admin/content/mocks/${encodeURIComponent(bundleId)}/item-analysis`}>
              <ListChecks className="mr-1 h-3.5 w-3.5" aria-hidden /> Item analysis
            </Link>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link href="/admin/content/mocks">
              <ArrowLeft className="mr-1 h-3.5 w-3.5" aria-hidden /> Back to bundles
            </Link>
          </Button>
        </div>
      }
    >
      <Card>
        <CardContent className="p-5">
          <div className="mb-4 text-xs text-admin-fg-muted">
            Bundle ID: <span className="font-mono text-admin-fg-strong">{bundleId || '—'}</span>
          </div>
          {bundleId ? (
            <MockReviewStageRail bundleId={bundleId} />
          ) : (
            <p className="text-sm text-admin-fg-muted">Missing bundle id in the route.</p>
          )}
        </CardContent>
      </Card>
    </AdminOperationsLayout>
  );
}
