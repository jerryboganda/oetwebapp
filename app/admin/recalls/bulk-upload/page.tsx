'use client';

import Link from 'next/link';
import { Upload } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';

export default function AdminRecallsBulkUploadPage() {
  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Recalls - Bulk upload"
        description="Legacy Recalls bulk upload is disabled for production safety."
        icon={Upload}
      />

      <AdminRoutePanel
        title="Use the controlled Vocabulary importer"
        description="Recalls vocabulary rows are managed as VocabularyTerm records. Production imports must use preview, dry run, draft import, batch export, and reconciliation."
      >
        <div className="space-y-3">
          <InlineAlert variant="warning">
            This route previously inserted active rows and could overwrite existing terms. It is now blocked. Use the safe importer for every Recalls batch.
          </InlineAlert>
          <Link href="/admin/content/vocabulary/import">
            <Button variant="primary">Open safe Vocabulary import</Button>
          </Link>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
