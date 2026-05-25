'use client';

import Link from 'next/link';
import { Upload } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Recalls', href: '/admin/recalls' },
  { label: 'Bulk upload' },
];

export default function AdminRecallsBulkUploadPage() {
  return (
    <AdminSettingsLayout
      title="Recalls — Bulk upload"
      description="Legacy Recalls bulk upload is disabled for production safety."
      breadcrumbs={BREADCRUMBS}
      icon={<Upload className="h-5 w-5" />}
    >
      <SettingsSection
        title="Use the controlled Vocabulary importer"
        description="Recalls vocabulary rows are managed as VocabularyTerm records. Production imports must use preview, dry run, draft import, batch export, and reconciliation."
      >
        <div className="space-y-3">
          <InlineAlert variant="warning">
            This route previously inserted active rows and could overwrite existing terms. It is now blocked. Use the safe importer for every Recalls batch.
          </InlineAlert>
          <Button variant="primary" asChild>
            <Link href="/admin/content/vocabulary/import">Open safe Vocabulary import</Link>
          </Button>
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
