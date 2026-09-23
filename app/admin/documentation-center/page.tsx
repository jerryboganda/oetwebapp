'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Download, FileCheck2, FileText, History, Search, ShieldAlert } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { AdminHubSection, type AdminHubLink } from '@/components/admin/ui/hub-card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { statusToTone } from '@/components/admin/ui/badge';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import {
  getDocumentationModulesPageData,
  type DocumentationModuleSummary,
} from '@/lib/admin';
import {
  downloadDocumentationEvidencePdf,
  downloadDocumentationMasterPdf,
  type DocumentationExportMode,
} from '@/lib/api';
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

export default function DocumentationCenterPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [retryNonce, setRetryNonce] = useState(0);
  const [modules, setModules] = useState<DocumentationModuleSummary[]>([]);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const items = await getDocumentationModulesPageData();
        if (cancelled) return;
        setModules(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load the Documentation Center.' });
        }
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, [retryNonce]);

  async function handleDownloadMaster(mode: DocumentationExportMode) {
    setDownloading(`master-${mode}`);
    try {
      const result = await downloadDocumentationMasterPdf(mode);
      saveBlob(result.blob, result.fileName);
      setToast({ variant: 'success', message: 'Master evidence pack downloaded.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to generate the Master PDF.' });
    } finally {
      setDownloading(null);
    }
  }

  async function handleDownloadEvidenceAnnex() {
    setDownloading('evidence');
    try {
      const result = await downloadDocumentationEvidencePdf('internal');
      saveBlob(result.blob, result.fileName);
      setToast({ variant: 'success', message: 'Evidence annex downloaded.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to generate the Evidence Annex.' });
    } finally {
      setDownloading(null);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [{ label: 'Admin', href: '/admin' }, { label: 'Documentation Center' }];

  const headerActions = (
    <div className="flex flex-wrap items-center gap-2">
      <Button variant="outline" asChild>
        <Link href="/admin/documentation-center/evidence">
          <Search className="h-4 w-4" /> Evidence Register
        </Link>
      </Button>
      <Button variant="outline" asChild>
        <Link href="/admin/documentation-center/exports">
          <History className="h-4 w-4" /> Export History
        </Link>
      </Button>
      <Button
        variant="outline"
        onClick={handleDownloadEvidenceAnnex}
        loading={downloading === 'evidence'}
        startIcon={<FileText className="h-4 w-4" />}
      >
        Evidence Annex
      </Button>
      <Button
        variant="outline"
        onClick={() => handleDownloadMaster('external')}
        loading={downloading === 'master-external'}
        startIcon={<ShieldAlert className="h-4 w-4" />}
      >
        Immigration Pack
      </Button>
      <Button
        onClick={() => handleDownloadMaster('internal')}
        loading={downloading === 'master-internal'}
        startIcon={<Download className="h-4 w-4" />}
      >
        Download Master PDF
      </Button>
    </div>
  );

  const links: AdminHubLink[] = modules.map((module) => ({
    href: `/admin/documentation-center/${module.code}`,
    title: `${module.code} — ${module.title}`,
    description: module.description,
    icon: <FileCheck2 className="h-5 w-5" />,
    badge: module.status === 'Published' ? `v${module.versionNumber} · Published` : module.status,
    badgeVariant: statusToTone(module.status),
  }));

  return (
    <AdminCatalogLayout
      title="Documentation Center"
      description="The evidence-grade technical & innovation record of the platform — Master Dossier, 15 specialist reports and the Evidence Annex, each traceable to real code, deployment and test evidence."
      breadcrumbs={breadcrumbs}
      actions={headerActions}
      hideViewModeToggle
      itemsClassName="flex flex-col gap-6"
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setRetryNonce((n) => n + 1)}
        emptyContent={
          <EmptyState
            illustration={<FileCheck2 />}
            title="No documentation modules yet"
            description="Modules are seeded on API startup. If this persists, check the DocumentationCenterSeeder logs."
          />
        }
      >
        <AdminHubSection title="Specialist Reports" links={links} columns="three" />
      </AsyncStateWrapper>
    </AdminCatalogLayout>
  );
}
