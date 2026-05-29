'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileJson, ListChecks } from 'lucide-react';

import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getContentPaper } from '@/lib/content-upload-api';
import { ListeningManifestPanel } from '@/components/domain/listening/ListeningManifestPanel';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

/**
 * WS5 — Listening test import / export. Hosts {@link ListeningManifestPanel}
 * inside the admin shell so an author can round-trip a complete §19 manifest
 * (Parts A, B, and C) for a paper. Mirrors the reading manifest surface.
 */
export default function AdminListeningImportPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();

  const [paperTitle, setPaperTitle] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const loadTitle = useCallback(async () => {
    if (!paperId) return;
    try {
      const paper = await getContentPaper(paperId);
      setPaperTitle(paper.title ?? '');
    } catch {
      // The panel falls back to a generic filename when the title is unknown,
      // so a failed title fetch must not block import/export.
      setPaperTitle('');
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void loadTitle();
  }, [isAuthenticated, role, loadTitle]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Import / Export' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: import / export" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Authoring"
      icon={<FileJson className="h-5 w-5" />}
      title="Listening: import / export"
      description={`Paper ${paperId ?? ''}. Import a complete Listening test from a §19 JSON manifest, or export the current test for round-tripping and backup.`}
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href="/admin/content/listening">
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to papers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ListChecks className="h-4 w-4 mr-1.5" />
              Structure
            </Link>
          </Button>
        </div>
      }
    >
      {paperId && (
        <ListeningManifestPanel
          paperId={paperId}
          paperTitle={paperTitle}
          onImported={() => { void loadTitle(); }}
          onNotify={(variant, message) => setToast({ variant, message })}
        />
      )}

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}
