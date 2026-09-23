'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { Download, ShieldAlert } from 'lucide-react';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import {
  getDocumentationModuleDetailData,
  type DocumentationModuleDetail,
} from '@/lib/admin';
import { downloadDocumentationModulePdf, type DocumentationExportMode } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

function saveBlob(blob: Blob, fileName: string) {
  const objectUrl = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(objectUrl);
}

export default function DocumentationModuleDetailPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const params = useParams<{ code?: string }>();
  const code = params?.code ?? '';

  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [retryNonce, setRetryNonce] = useState(0);
  const [detail, setDetail] = useState<DocumentationModuleDetail | null>(null);
  const [downloading, setDownloading] = useState<DocumentationExportMode | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    if (!code) return;
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const result = await getDocumentationModuleDetailData(code);
        if (cancelled) return;
        setDetail(result);
        setPageStatus(result.sections.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load this module.' });
        }
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, [code, retryNonce]);

  async function handleDownload(mode: DocumentationExportMode) {
    setDownloading(mode);
    try {
      const result = await downloadDocumentationModulePdf(code, mode);
      saveBlob(result.blob, result.fileName);
      setToast({ variant: 'success', message: `${code} PDF downloaded.` });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to generate the PDF.' });
    } finally {
      setDownloading(null);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Documentation Center', href: '/admin/documentation-center' },
    { label: code },
  ];

  const headerActions = (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        variant="outline"
        onClick={() => handleDownload('external')}
        loading={downloading === 'external'}
        startIcon={<ShieldAlert className="h-4 w-4" />}
      >
        Immigration Version
      </Button>
      <Button
        onClick={() => handleDownload('internal')}
        loading={downloading === 'internal'}
        startIcon={<Download className="h-4 w-4" />}
      >
        Download PDF
      </Button>
    </div>
  );

  const evidenceColumns: Column<DocumentationModuleDetail['evidence'][number]>[] = [
    { key: 'evidenceId', header: 'Evidence ID', render: (e) => <span className="font-mono text-xs font-semibold text-admin-fg-strong">{e.evidenceId}</span> },
    { key: 'evidenceType', header: 'Type', render: (e) => <span className="text-admin-fg-muted">{e.evidenceType}</span> },
    { key: 'description', header: 'Description', render: (e) => <span className="text-admin-fg-strong">{e.description}</span> },
    { key: 'sourceReference', header: 'Source', render: (e) => <span className="font-mono text-xs text-admin-fg-muted">{e.sourceReference}</span> },
    {
      key: 'isInternalOnly',
      header: 'Visibility',
      render: (e) => (e.isInternalOnly ? <Badge variant="warning" size="sm">Internal only</Badge> : <Badge variant="success" size="sm">External-safe</Badge>),
    },
  ];

  const versionColumns: Column<DocumentationModuleDetail['versions'][number]>[] = [
    { key: 'versionNumber', header: 'Version', render: (v) => <span className="font-semibold text-admin-fg-strong">v{v.versionNumber}</span> },
    { key: 'status', header: 'Status', render: (v) => <Badge variant={statusToTone(v.status)} size="sm">{v.status}</Badge> },
    { key: 'generatedAt', header: 'Generated', render: (v) => <span className="text-admin-fg-muted">{new Date(v.generatedAt).toLocaleDateString()}</span> },
    { key: 'approvedByName', header: 'Approved by', render: (v) => <span className="text-admin-fg-muted">{v.approvedByName ?? '—'}</span> },
  ];

  return (
    <AdminPageShell mainAriaLabel="Documentation Center module detail">
      <PageHeader
        title={detail ? `${detail.code} — ${detail.title}` : code}
        description={detail?.description ?? 'Loading module…'}
        breadcrumbs={breadcrumbs}
        actions={headerActions}
      />

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setRetryNonce((n) => n + 1)}
        emptyContent={
          <EmptyState title="Not yet available" description="This module has no published version yet." />
        }
      >
        {detail ? (
          <div className="space-y-6">
            <Card>
              <CardHeader className="flex-row items-center justify-between gap-3">
                <div>
                  <CardTitle>Report content</CardTitle>
                  <CardDescription>Version {detail.versionNumber} · {detail.status}</CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-6">
                {detail.sections.map((section, index) => (
                  <div key={`${section.heading}-${index}`} className="space-y-2">
                    <div className="flex items-center gap-2">
                      <h3 className="text-base font-semibold text-admin-fg-strong">{section.heading}</h3>
                      {section.isInternalOnly ? <Badge variant="warning" size="sm">Internal only</Badge> : null}
                    </div>
                    <p className="whitespace-pre-wrap text-sm leading-relaxed text-admin-fg-default">{section.bodyMarkdown}</p>
                  </div>
                ))}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Evidence cited in this report</CardTitle>
                <CardDescription>Every claim above resolves to one of these sourced entries.</CardDescription>
              </CardHeader>
              <CardContent>
                <DataTable aria-label="Module evidence" columns={evidenceColumns} data={detail.evidence} keyExtractor={(e) => e.evidenceId} />
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Version history</CardTitle>
              </CardHeader>
              <CardContent>
                <DataTable aria-label="Version history" columns={versionColumns} data={detail.versions} keyExtractor={(v) => v.id} />
              </CardContent>
            </Card>
          </div>
        ) : null}
      </AsyncStateWrapper>
    </AdminPageShell>
  );
}
