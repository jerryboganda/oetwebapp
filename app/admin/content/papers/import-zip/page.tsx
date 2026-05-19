'use client';

// SUBAGENT_C: Dedicated ZIP bulk-import page. Wraps the shared
// `ZipBulkImportPanel` inside the standard admin route surface so deep links
// (and the JSON-tab "Switch to ZIP" affordance) have a dedicated URL.

import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { ZipBulkImportPanel } from '@/components/admin/import/ZipBulkImportPanel';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

export default function AdminContentZipImportPage() {
  const { isAuthenticated } = useAdminAuth();
  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        title="Bulk ZIP import"
        description="Stage a ZIP of papers + assets, review the detected manifest, and commit the approved papers in a single transaction."
      />
      <AdminRoutePanel title="ZIP bulk import">
        <ZipBulkImportPanel />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
