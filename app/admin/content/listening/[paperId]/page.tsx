'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Headphones, ArrowLeft, Send } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { getContentPaper, type ContentPaperDto } from '@/lib/content-upload-api';
import { adminPublishPaperWithWarnings } from '@/lib/api';
import { StructureTab } from '@/components/admin/listening/StructureTab';
import { ExtractsTab } from '@/components/admin/listening/ExtractsTab';
import { BackfillTab } from '@/components/admin/listening/BackfillTab';
import { ValidateTab } from '@/components/admin/listening/ValidateTab';
import { ExtractionsTab } from '@/components/admin/listening/ExtractionsTab';
import { AssetsTab } from '@/components/admin/listening/AssetsTab';

type TabKey = 'structure' | 'extracts' | 'backfill' | 'validate' | 'extractions' | 'assets';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const TABS: { key: TabKey; label: string }[] = [
  { key: 'structure', label: 'Structure' },
  { key: 'extracts', label: 'Extracts' },
  { key: 'backfill', label: 'Backfill TTS' },
  { key: 'validate', label: 'Validate' },
  { key: 'extractions', label: 'Extractions' },
  { key: 'assets', label: 'Assets' },
];

export default function ListeningWorkspacePage() {
  const params = useParams();
  const paperIdRaw = params?.paperId;
  const paperId = Array.isArray(paperIdRaw) ? paperIdRaw[0] : paperIdRaw;

  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [activeTab, setActiveTab] = useState<TabKey>('structure');
  const [toast, setToast] = useState<ToastState>(null);
  const [publishing, setPublishing] = useState(false);

  const onToast = useCallback((variant: 'success' | 'error', message: string) => {
    setToast({ variant, message });
  }, []);

  const loadPaper = useCallback(async () => {
    if (!paperId) return;
    try {
      const p = await getContentPaper(paperId);
      setPaper(p);
    } catch (e) {
      onToast('error', `Load paper failed: ${(e as Error).message}`);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin' || !paperId) return;
    void loadPaper();
  }, [isAuthenticated, loadPaper, paperId, role]);

  const publish = useCallback(async () => {
    if (!paperId || !canWriteContent) return;
    setPublishing(true);
    try {
      const res = await adminPublishPaperWithWarnings(paperId);
      const warnings = res.warnings ?? [];
      onToast(
        warnings.length === 0 ? 'success' : 'success',
        warnings.length === 0
          ? `Published (status=${res.status ?? 'Published'}).`
          : `Published with ${warnings.length} warning(s): ${warnings.slice(0, 3).join('; ')}`,
      );
      await loadPaper();
    } catch (e) {
      onToast('error', `Publish failed: ${(e as Error).message}`);
    } finally {
      setPublishing(false);
    }
  }, [canWriteContent, loadPaper, onToast, paperId]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }
  if (!paperId) {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-danger">Missing paperId.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening Workspace">
      <div className="mb-2">
        <Link
          href="/admin/content/listening"
          className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-3 w-3" /> Back to Listening papers
        </Link>
      </div>

      <AdminRouteSectionHeader
        icon={<Headphones className="w-6 h-6" />}
        title={paper?.title ?? 'Listening workspace'}
        description={
          paper
            ? `${paper.slug} · ${paper.status} · ${paper.appliesToAllProfessions ? 'All professions' : paper.professionId ?? '—'}`
            : 'Loading paper…'
        }
      />

      {paper && (
        <AdminRoutePanel>
          <div className="flex flex-wrap items-center gap-2">
            <Badge
              variant={
                paper.status === 'Published'
                  ? 'success'
                  : paper.status === 'Archived'
                    ? 'muted'
                    : paper.status === 'InReview'
                      ? 'warning'
                      : 'default'
              }
            >
              {paper.status}
            </Badge>
            <Badge variant="info">{paper.difficulty}</Badge>
            <span className="font-mono text-xs text-muted">id: {paper.id}</span>
            <div className="ml-auto">
              <Button
                variant="primary"
                size="sm"
                disabled={!canWriteContent || publishing}
                onClick={() => void publish()}
              >
                <Send className="h-3 w-3" /> {publishing ? 'Publishing…' : 'Publish (with warnings)'}
              </Button>
            </div>
          </div>
        </AdminRoutePanel>
      )}

      <AdminRoutePanel>
        <div
          role="tablist"
          aria-label="Listening workspace tabs"
          className="flex flex-wrap gap-1 border-b border-border"
        >
          {TABS.map((t) => {
            const active = activeTab === t.key;
            return (
              <button
                key={t.key}
                role="tab"
                aria-selected={active}
                onClick={() => setActiveTab(t.key)}
                className={
                  'min-h-10 rounded-t-lg px-3 py-2 text-sm font-semibold transition ' +
                  (active
                    ? 'border-b-2 border-primary text-primary'
                    : 'text-muted hover:text-navy')
                }
              >
                {t.label}
              </button>
            );
          })}
        </div>
        <div className="pt-4">
          {activeTab === 'structure' && <StructureTab paperId={paperId} onToast={onToast} />}
          {activeTab === 'extracts' && <ExtractsTab paperId={paperId} onToast={onToast} />}
          {activeTab === 'backfill' && <BackfillTab paperId={paperId} onToast={onToast} />}
          {activeTab === 'validate' && <ValidateTab paperId={paperId} onToast={onToast} />}
          {activeTab === 'extractions' && <ExtractionsTab paperId={paperId} onToast={onToast} />}
          {activeTab === 'assets' && <AssetsTab paperId={paperId} onToast={onToast} />}
        </div>
      </AdminRoutePanel>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
