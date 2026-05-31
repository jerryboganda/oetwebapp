'use client';

import { useRef, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, ArrowLeft, CloudUpload, FileText, Loader2 } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { apiClient } from '@/lib/api';
import { DEFAULT_CONTENT_SOURCE_PROVENANCE } from '@/lib/content-upload-defaults';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface ReadinessIssue {
  code: string;
  severity: 'error' | 'warning' | string;
  message: string;
}

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
  deliveryModes: string[];
  officialShape: string;
  readinessIssues: ReadinessIssue[];
  assets: ProposedAsset[];
}

interface ProposedReference {
  proposalId: string;
  target: string;
  title: string;
  sourceRelativePath: string;
  kind: string | null;
  professionId: string | null;
  sharedResourceKind: string | null;
  templateKey: string | null;
  sortOrder: number | null;
  sourceProvenance: string | null;
  readinessIssues: ReadinessIssue[];
}

interface ImportInventory {
  totalFiles: number;
  classifiedFileCount: number;
  unclassifiedFileCount: number;
  filesByExtension: Record<string, number>;
  filesByTopLevel: Record<string, number>;
}

interface StagedResponse {
  sessionId: string;
  expiresAt: string;
  papers: ProposedPaper[];
  references: ProposedReference[];
  inventory: ImportInventory;
  readinessIssues: ReadinessIssue[];
  issues: Array<{ relativePath: string; issueCode: string; message: string }>;
}

function readinessSummary(issues: ReadinessIssue[]) {
  const errors = issues.filter((issue) => issue.severity === 'error').length;
  const warnings = issues.filter((issue) => issue.severity !== 'error').length;
  if (errors > 0) return <Badge variant="danger">{errors} blocker{errors === 1 ? '' : 's'}</Badge>;
  if (warnings > 0) return <Badge variant="warning">{warnings} warning{warnings === 1 ? '' : 's'}</Badge>;
  return <Badge variant="success">Ready to commit</Badge>;
}

export default function BulkImportPage() {
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const [uploading, setUploading] = useState(false);
  const [staged, setStaged] = useState<StagedResponse | null>(null);
  const [approved, setApproved] = useState<Record<string, boolean>>({});
  const [provenance, setProvenance] = useState(DEFAULT_CONTENT_SOURCE_PROVENANCE);
  const [committing, setCommitting] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Papers', href: '/admin/content/papers' },
    { label: 'Bulk import' },
  ];

  if (isLoading) return null;

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Bulk import" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  if (!canWriteContent) {
    return (
      <AdminSettingsLayout title="Bulk import" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Content write permission is required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  const doUpload = async (file: File) => {
    if (!canWriteContent) return;
    setUploading(true);
    try {
      const form = new FormData();
      form.set('file', file);
      const data = await apiClient.postForm<StagedResponse>('/v1/admin/imports/zip', form);
      setStaged(data);
      setApproved(Object.fromEntries([
        ...data.papers.map((p) => [p.proposalId, true] as const),
        ...data.references.map((r) => [r.proposalId, true] as const),
      ]));
      setToast({
        variant: 'success',
        message: `Detected ${data.papers.length} papers and ${data.references.length} references from ${file.name}.`,
      });
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  };

  const doCommit = async () => {
    if (!staged || !canWriteContent) return;
    setCommitting(true);
    try {
      const approvals = [
        ...staged.papers.map((p) => ({
          proposalId: p.proposalId,
          approve: Boolean(approved[p.proposalId]),
          overrideSourceProvenance: provenance.trim() || DEFAULT_CONTENT_SOURCE_PROVENANCE,
        })),
        ...staged.references.map((r) => ({
          proposalId: r.proposalId,
          approve: Boolean(approved[r.proposalId]),
          overrideSourceProvenance: provenance.trim() || DEFAULT_CONTENT_SOURCE_PROVENANCE,
        })),
      ];
      const result = await apiClient.post<{
        createdPaperCount: number;
        createdAssetCount: number;
        deduplicatedAssetCount: number;
        createdReferenceCount: number;
        warnings: string[];
      }>(`/v1/admin/imports/zip/${staged.sessionId}/commit`, approvals);
      setToast({
        variant: 'success',
        message: `Created ${result.createdPaperCount} papers, ${result.createdAssetCount} paper assets, and ${result.createdReferenceCount} references (${result.deduplicatedAssetCount} deduplicated).`,
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
    { key: 'shape', header: 'Official shape', render: (p) => <span className="text-xs text-admin-fg-muted">{p.officialShape}</span> },
    {
      key: 'sc', header: 'Scope', render: (p) => p.appliesToAllProfessions
        ? <Badge variant="muted">All</Badge>
        : <Badge variant="info">{p.professionId ?? '-'}</Badge>,
    },
    { key: 'tag', header: 'Card / Letter', render: (p) => p.cardType ?? p.letterType ?? '-' },
    { key: 'cnt', header: 'Assets', render: (p) => p.assets.length },
    {
      key: 'roles', header: 'Roles', render: (p) => (
        <div className="flex gap-1 flex-wrap">
          {p.assets.map((a, i) => <Badge key={i} variant="muted">{a.role}{a.part ? ` ${a.part}` : ''}</Badge>)}
        </div>
      ),
    },
    { key: 'ready', header: 'Readiness', render: (p) => readinessSummary(p.readinessIssues) },
  ];

  const referenceColumns: Column<ProposedReference>[] = [
    {
      key: 'a', header: 'Approve',
      render: (r) => (
        <input
          type="checkbox"
          checked={approved[r.proposalId] ?? false}
          onChange={(e) => setApproved({ ...approved, [r.proposalId]: e.target.checked })}
        />
      ),
    },
    { key: 'target', header: 'Target', render: (r) => <Badge variant="info">{r.target}</Badge> },
    { key: 'title', header: 'Title', render: (r) => r.title },
    { key: 'kind', header: 'Kind', render: (r) => r.kind ?? r.sharedResourceKind ?? r.templateKey ?? '-' },
    { key: 'scope', header: 'Scope', render: (r) => r.professionId ? <Badge variant="info">{r.professionId}</Badge> : <Badge variant="muted">All</Badge> },
    { key: 'source', header: 'Source', render: (r) => <span className="text-xs text-admin-fg-muted break-all">{r.sourceRelativePath}</span> },
    { key: 'ready', header: 'Readiness', render: (r) => readinessSummary(r.readinessIssues) },
  ];

  return (
    <AdminSettingsLayout
      eyebrow="CMS"
      icon={<CloudUpload className="w-5 h-5" />}
      title="Bulk import"
      description="Upload a ZIP matching the Project Real Content folder structure. The system proposes papers, you approve, and assets are stored content-addressed with SHA-256 dedup."
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href="/admin/content/papers">
            <ArrowLeft className="w-4 h-4 mr-1.5" /> Back to papers
          </Link>
        </Button>
      }
    >
      <div className="space-y-6">
        <Card>
          <CardHeader><CardTitle>Upload ZIP</CardTitle></CardHeader>
          <CardContent>
            <div className="flex items-center gap-4">
              <input
                ref={fileRef}
                type="file"
                accept=".zip,application/zip"
                disabled={uploading}
                onChange={(e) => { const f = e.target.files?.[0]; if (f) void doUpload(f); }}
                className="block text-sm text-admin-fg-strong file:mr-3 file:rounded-admin file:border-0 file:bg-[var(--admin-primary)] file:px-3 file:py-2 file:text-sm file:font-semibold file:text-[var(--admin-primary-fg)]"
              />
              {uploading && <span className="text-sm text-admin-fg-muted flex items-center gap-2"><Loader2 className="w-4 h-4 animate-spin" /> Staging…</span>}
            </div>
          </CardContent>
        </Card>

        {staged && (
          <>
            <div className="grid gap-3 md:grid-cols-4">
              <Card>
                <CardContent className="p-4">
                  <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-admin-fg-muted">
                    <FileText className="h-4 w-4" /> Files
                  </div>
                  <div className="mt-2 text-2xl font-semibold text-admin-fg-strong">{staged.inventory.totalFiles}</div>
                  <div className="mt-1 text-xs text-admin-fg-muted">{staged.inventory.classifiedFileCount} classified</div>
                </CardContent>
              </Card>
              <Card>
                <CardContent className="p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-admin-fg-muted">Papers</div>
                  <div className="mt-2 text-2xl font-semibold text-admin-fg-strong">{staged.papers.length}</div>
                  <div className="mt-1 text-xs text-admin-fg-muted">Listening, Reading, Writing, Speaking</div>
                </CardContent>
              </Card>
              <Card>
                <CardContent className="p-4">
                  <div className="text-xs font-medium uppercase tracking-wide text-admin-fg-muted">References</div>
                  <div className="mt-2 text-2xl font-semibold text-admin-fg-strong">{staged.references.length}</div>
                  <div className="mt-1 text-xs text-admin-fg-muted">Rulebooks, scoring, shared resources</div>
                </CardContent>
              </Card>
              <Card>
                <CardContent className="p-4">
                  <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-admin-fg-muted">
                    <AlertTriangle className="h-4 w-4" /> Issues
                  </div>
                  <div className="mt-2 text-2xl font-semibold text-admin-fg-strong">{staged.inventory.unclassifiedFileCount}</div>
                  <div className="mt-1 text-xs text-admin-fg-muted">Need manual review</div>
                </CardContent>
              </Card>
            </div>

            {staged.readinessIssues.length > 0 && (
              <InlineAlert variant="warning">
                {staged.readinessIssues.map((issue) => issue.message).join(' ')}
              </InlineAlert>
            )}

            {staged.issues.length > 0 && (
              <InlineAlert variant="warning">
                {staged.issues.length} files were not auto-classified. Edit each paper post-import to add them manually.
              </InlineAlert>
            )}

            {staged.issues.length > 0 && (
              <Card>
                <CardHeader><CardTitle>Unclassified files</CardTitle></CardHeader>
                <CardContent>
                  <div className="space-y-2">
                    {staged.issues.map((issue) => (
                      <div key={`${issue.issueCode}:${issue.relativePath}`} className="rounded-admin border border-admin-border-subtle p-3 text-sm">
                        <div className="font-medium text-admin-fg-strong">{issue.issueCode}</div>
                        <div className="mt-1 text-admin-fg-muted break-all">{issue.relativePath}</div>
                        <div className="mt-1 text-admin-fg-muted">{issue.message}</div>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            )}

            <Card>
              <CardHeader><CardTitle>Source provenance (required before commit)</CardTitle></CardHeader>
              <CardContent>
                <Input
                  type="text"
                  value={provenance}
                  onChange={(e) => setProvenance(e.target.value)}
                  placeholder={DEFAULT_CONTENT_SOURCE_PROVENANCE}
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>Proposed papers ({staged.papers.length})</CardTitle></CardHeader>
              <CardContent>
                <DataTable data={staged.papers} columns={columns} keyExtractor={(p) => p.proposalId} />
              </CardContent>
            </Card>

            {staged.references.length > 0 && (
              <Card>
                <CardHeader><CardTitle>Reference targets ({staged.references.length})</CardTitle></CardHeader>
                <CardContent>
                  <DataTable data={staged.references} columns={referenceColumns} keyExtractor={(r) => r.proposalId} />
                </CardContent>
              </Card>
            )}

            <Card>
              <CardHeader><CardTitle>File type inventory</CardTitle></CardHeader>
              <CardContent>
                <div className="flex flex-wrap gap-2">
                  {Object.entries(staged.inventory.filesByExtension).map(([ext, count]) => (
                    <Badge key={ext} variant="muted">.{ext}: {count}</Badge>
                  ))}
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <div className="text-sm font-medium text-admin-fg-strong">Commit reviewed proposals</div>
                  <div className="mt-1 text-xs text-admin-fg-muted">
                    Approved targets are created as drafts. Publish gates still require structured authoring and review.
                  </div>
                </div>
                <div className="flex gap-3 justify-end">
                  <Button variant="ghost" onClick={() => { setStaged(null); setApproved({}); }}>Discard</Button>
                  <Button
                    variant="primary"
                    onClick={() => void doCommit()}
                    loading={committing}
                    loadingText="Committing…"
                    disabled={!provenance.trim() || Object.values(approved).every((v) => !v)}
                  >
                    Commit approved proposals
                  </Button>
                </div>
              </CardContent>
            </Card>
          </>
        )}
      </div>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}
