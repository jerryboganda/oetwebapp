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

import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { MockReviewStageRail } from '@/components/domain/admin/MockReviewStageRail';

export default function MockBundleReviewPipelinePage() {
  const params = useParams<{ bundleId: string }>();
  const bundleId = params?.bundleId ?? '';

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminRouteSectionHeader
          eyebrow="Mock bundle"
          title="Editorial review pipeline"
          description="Walk a bundle from academic review through to published. Stage transitions are monotonic — the backend rejects regressions."
          icon={ListChecks}
          actions={
            <div className="flex items-center gap-2">
              <Link
                href={`/admin/content/mocks/${encodeURIComponent(bundleId)}/item-analysis`}
                className="inline-flex items-center gap-1 text-sm text-muted hover:text-primary"
              >
                Item analysis
              </Link>
              <Link
                href="/admin/content/mocks"
                className="inline-flex items-center gap-1 text-sm text-muted hover:text-primary"
              >
                <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back to bundles
              </Link>
            </div>
          }
        />

        <div className="mb-4 text-xs text-muted">
          Bundle ID: <span className="font-mono text-navy">{bundleId || '—'}</span>
        </div>

        {bundleId ? (
          <MockReviewStageRail bundleId={bundleId} />
        ) : (
          <p className="text-sm text-muted">Missing bundle id in the route.</p>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
