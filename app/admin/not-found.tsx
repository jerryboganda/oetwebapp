/**
 * Admin route-level 404 boundary.
 * Renders when a child segment calls `notFound()` or when Next.js falls
 * through to it from a missing dynamic route. Spec reference:
 * docs/admin-redesign/axelit-study/19-ERROR-EMPTY-STATES.md §2.15.
 *
 * Server component — no client interactivity needed; both actions are
 * plain navigations.
 */

import { FileQuestion } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { EmptyState } from '@/components/admin/ui/empty-state';

export default function AdminNotFound() {
  return (
    <AdminPageShell hideSkipLink>
      <div className="flex min-h-[60vh] items-center justify-center">
        <EmptyState
          variant="default"
          size="lg"
          headingLevel="h1"
          illustration={<FileQuestion aria-hidden="true" />}
          title="Admin page not found"
          description="The page you're looking for doesn't exist or has been moved. Use the navigation to find what you need."
          primaryAction={{
            label: 'Back to dashboard',
            href: '/admin',
          }}
        />
      </div>
    </AdminPageShell>
  );
}
