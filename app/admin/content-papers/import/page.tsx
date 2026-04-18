'use client';

import { useRef, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, CloudUpload, Loader2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { env } from '@/lib/env';
import { ensureFreshAccessToken } from '@/lib/auth-client';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface ProposedAsset {
  sourceRelativePath: string;
  role: string;
  part: string | null;
  suggestedTitle: string | null;
}

interface ProposedPaper {
  proposalId: string;
  subtestCode: string;
  title: string;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  cardType: string | null;
  letterType: string | null;
  sourceProvenance: string | null;
  assets: ProposedAsset[];
}

interface StagedResponse {
  sessionId: string;
  expiresAt: string;
  papers: ProposedPaper[];
  issues: Array<{ relativePath: string; issueCode: string; message: string }>;
}

export default function BulkImportPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [uploading, setUploading] = useState(false);
  const [staged, setStaged] = useState<StagedResponse | null>(null);
  const [approved, setApproved] = useState<Record<string, boolean>>({});
  const [provenance, setProvenance] = useState('Authored by Dr Hesham');
  const [committing, setCommitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  const doUpload = async (file: File) => {
    setUploading(true);
    try {
      const token = await ensureFreshAccessToken();
      const form = new FormData();
      form.set('file', file);
      const res = await fetch(`${env.apiBaseUrl}/v1/admin/imports/zip`, {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
        body: form,
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = (await res.json()) as StagedResponse;
      setStaged(data);
      setApproved(Object.fromEntries(data.papers.map((p) => [p.proposalId, true])));
      setToast({
        variant: 'success',
        message: `Detected ${data.papers.length} papers from ${file.name}.`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  };

  const doCommit = async () => {
    if (!staged) return;
    setCommitting(true);
    try {
      const token = await ensureFreshAccessToken();
      const approvals = staged.papers.map((p) => ({
        proposalId: p.proposalId,
        approve: Boolean(approved[p.proposalId]),
        overrideSourceProvenance: provenance,
      }));
      const res = await fetch(`${env.apiBaseUrl}/v1/admin/imports/zip/${staged.sessionId}/commit`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify(approvals),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const result = await res.json();
      setToast({
        variant: 'success',
        message: `Created ${result.createdPaperCount} papers and ${result.createdAssetCount} assets (${result.deduplicatedAssetCount} deduplicated).`,
      });
      setStaged(null);
      setApproved({});
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally { setCommitting(false); }
  };

  const columns: Column<ProposedPaper>[] = [
    {
      key: 'a', header: 'Approve',
      render: (p) => (
        <input
          type="checkbox"
          checked={approved[p.proposalId] ?? false}
          onChange={(e) => setApproved({ ...approved, [p.proposalId]: e.target.checked })}
        />
      ),
    },
    { key: 's', header: 'Subtest', render: (p) => <Badge variant="info">{p.subtestCode}</Badge> },
    { key: 't', header: 'Title', render: (p) => p.title },
    {
      key: 'sc', header: 'Scope', render: (p) => p.appliesToAllProfessions
        ? <Badge variant="muted">All</Badge>
        : <Badge variant="info">{p.professionId ?? '—'}</Badge>,
    },
    { key: 'tag', header: 'Card / Letter', render: (p) => p.cardType ?? p.letterType ?? '—' },
    { key: 'cnt', header: 'Assets', render: (p) => p.assets.length },
    {
      key: 'roles', header: 'Roles', render: (p) => (
        <div className="flex gap-1 flex-wrap">
          {p.assets.map((a, i) => <Badge key={i} variant="muted">{a.role}{a.part ? ` ${a.part}` : ''}</Badge>)}
        </div>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <Link href="/admin/content-papers" className="inline-flex items-center gap-2 text-sm text-muted hover:text-navy">
        <ArrowLeft className="w-4 h-4" /> Back to papers
      </Link>

      <AdminRouteSectionHeader
        icon={<CloudUpload className="w-6 h-6" />}
        title="Bulk import"
        description="Upload a ZIP matching the Project Real Content folder structure. The system proposes papers, you approve, and assets are stored content-addressed with SHA-256 dedup."
      />

      <AdminRoutePanel title="Upload ZIP">
        <div className="flex items-center gap-4">
          <input
            ref={fileRef}
            type="file"
            accept=".zip,application/zip"
            disabled={uploading}
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void doUpload(f); }}
          />
          {uploading && <span className="text-sm text-muted flex items-center gap-2"><Loader2 className="w-4 h-4 animate-spin" /> Staging…</span>}
        </div>
      </AdminRoutePanel>

      {staged && (
        <>
          {staged.issues.length > 0 && (
            <InlineAlert variant="warning">
              {staged.issues.length} files were not auto-classified. Edit each paper post-import to add them manually.
            </InlineAlert>
          )}

          <AdminRoutePanel title="Source provenance (required before commit)">
            <input
              type="text"
              className="w-full border rounded-lg p-2"
              value={provenance}
              onChange={(e) => setProvenance(e.target.value)}
              placeholder="Authored by Dr Hesham / Licensed from X / Public domain"
            />
          </AdminRoutePanel>

          <AdminRoutePanel title={`Proposed papers (${staged.papers.length})`}>
            <DataTable data={staged.papers} columns={columns} keyExtractor={(p) => p.proposalId} />
            <div className="flex gap-3 mt-4 justify-end">
              <Button variant="ghost" onClick={() => { setStaged(null); setApproved({}); }}>Discard</Button>
              <Button
                variant="primary"
                onClick={() => void doCommit()}
                loading={committing}
                disabled={!provenance.trim() || Object.values(approved).every((v) => !v)}
              >
                Approve selected &amp; commit
              </Button>
            </div>
          </AdminRoutePanel>
        </>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
