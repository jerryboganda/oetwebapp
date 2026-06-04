'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Headphones, Plus, Archive as ArchiveIcon, CheckCircle2, XCircle, Settings } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  archiveContentPaper,
  listContentPapers,
  publishContentPaper,
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUSES = [
  { value: '', label: 'Any status' },
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const REQUIRED_LISTENING_ROLES: PaperAssetRole[] = [
  'Audio',
  'QuestionPaper',
  'AudioScript',
  'AnswerKey',
];

const ROLE_SHORT_LABELS: Record<PaperAssetRole, string> = {
  Audio: 'Audio',
  QuestionPaper: 'QP',
  AudioScript: 'AS',
  AnswerKey: 'AK',
  CaseNotes: 'CN',
  ModelAnswer: 'MA',
  RoleCard: 'RC',
  AssessmentCriteria: 'AC',
  WarmUpQuestions: 'WU',
  Supplementary: 'SUP',
};

function hasRole(paper: ContentPaperDto, role: PaperAssetRole): boolean {
  return Boolean(paper.assets?.some((a) => a.role === role && a.isPrimary));
}

function isMissingRequiredAssets(paper: ContentPaperDto): boolean {
  return REQUIRED_LISTENING_ROLES.some((r) => !hasRole(paper, r));
}

export default function AdminListeningPapersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<ContentPaperDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [filterStatus, setFilterStatus] = useState('');
  const [search, setSearch] = useState('');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);

  const load = useCallback(async () => {
    if (!isAuthenticated || role !== 'admin') return;
    setStatus('loading');
    try {
      const data = await listContentPapers({
        subtest: 'listening',
        status: filterStatus || undefined,
        search: search || undefined,
        pageSize: 100,
      });
      setRows(data);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load listening papers: ${(e as Error).message}` });
    }
  }, [filterStatus, isAuthenticated, role, search]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => { void load(); });
  }, [isAuthenticated, load, role]);

  const archive = useCallback(async (id: string) => {
    if (!canWriteContent) return;
    if (!confirm('Archive this listening paper? Learners will no longer see it.')) return;
    try {
      await archiveContentPaper(id);
      setToast({ variant: 'success', message: 'Paper archived.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Archive failed: ${(e as Error).message}` });
    }
  }, [canWriteContent, load]);

  const runBulk = useCallback(async (
    action: 'archive' | 'publish',
    op: (id: string) => Promise<unknown>,
  ) => {
    if (!canWriteContent || bulkBusy || selectedKeys.size === 0) return;
    const ids = Array.from(selectedKeys);
    const selectedPapers = rows.filter((p) => selectedKeys.has(p.id));
    const titles = selectedPapers.map((p) => `• ${p.title}`).join('\n');
    const verb = action === 'archive' ? 'Archive' : 'Publish';
    const consequence = action === 'archive'
      ? 'Learners will no longer see them.'
      : 'They become visible to entitled learners immediately.';
    if (!confirm(`${verb} ${ids.length} listening paper${ids.length === 1 ? '' : 's'}?\n\n${titles}\n\n${consequence}`)) {
      return;
    }
    setBulkBusy(true);
    const failures: string[] = [];
    for (const id of ids) {
      try {
        await op(id);
      } catch (e) {
        const paper = selectedPapers.find((p) => p.id === id);
        failures.push(`${paper?.title ?? id}: ${(e as Error).message}`);
      }
    }
    setBulkBusy(false);
    setSelectedKeys(new Set());
    if (failures.length === 0) {
      setToast({ variant: 'success', message: `${verb}d ${ids.length} paper${ids.length === 1 ? '' : 's'}.` });
    } else if (failures.length === ids.length) {
      setToast({ variant: 'error', message: `${verb} failed for all ${ids.length}: ${failures[0]}` });
    } else {
      setToast({
        variant: 'error',
        message: `${verb}d ${ids.length - failures.length} of ${ids.length}. First failure: ${failures[0]}`,
      });
    }
    await load();
  }, [bulkBusy, canWriteContent, load, rows, selectedKeys]);

  const bulkArchive = useCallback(() => { void runBulk('archive', archiveContentPaper); }, [runBulk]);
  const bulkPublish = useCallback(() => { void runBulk('publish', publishContentPaper); }, [runBulk]);

  const stats = useMemo(() => {
    let published = 0;
    let draft = 0;
    let review = 0;
    let missing = 0;
    for (const p of rows) {
      if (p.status === 'Published') published++;
      else if (p.status === 'Draft') draft++;
      else if (p.status === 'InReview') review++;
      if (isMissingRequiredAssets(p)) missing++;
    }
    return { total: rows.length, published, draft, review, missing };
  }, [rows]);

  const columns: Column<ContentPaperDto>[] = useMemo(() => [
    {
      key: 'title',
      header: 'Title',
      render: (p) => canWriteContent ? (
        <Link href={`/admin/content/papers/${p.id}`} className="font-medium hover:text-[var(--admin-primary)]">
          {p.title}
        </Link>
      ) : <span className="font-medium">{p.title}</span>,
    },
    { key: 'slug', header: 'Slug', render: (p) => <span className="font-mono text-xs">{p.slug}</span> },
    {
      key: 'scope', header: 'Profession scope', render: (p) => p.appliesToAllProfessions
        ? <Badge variant="muted">All professions</Badge>
        : <Badge variant="info">{p.professionId ?? '-'}</Badge>,
    },
    {
      key: 'status', header: 'Status', render: (p) => <Badge variant={
        p.status === 'Published' ? 'success'
          : p.status === 'Archived' ? 'muted'
          : p.status === 'InReview' ? 'warning' : 'default'
      }>{p.status}</Badge>,
    },
    {
      key: 'assets',
      header: 'Asset readiness',
      render: (p) => (
        <div className="flex flex-wrap gap-1.5" aria-label="Required asset readiness">
          {REQUIRED_LISTENING_ROLES.map((r) => {
            const ok = hasRole(p, r);
            return (
              <span
                key={r}
                title={`${r}: ${ok ? 'present' : 'missing'}`}
                className={
                  'inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-semibold ' +
                  (ok ? 'bg-[var(--admin-success-tint)] text-[var(--admin-success)]' : 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]')
                }
              >
                {ok ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
                {ROLE_SHORT_LABELS[r]}
              </span>
            );
          })}
        </div>
      ),
    },
    { key: 'updated', header: 'Updated', render: (p) => new Date(p.updatedAt).toLocaleString() },
    {
      key: 'actions',
      header: 'Actions',
      render: (p) => canWriteContent ? (
        <div className="flex gap-2">
          <Link
            href={`/admin/content/papers/${p.id}`}
            className="inline-flex min-h-9 items-center rounded-admin px-3 py-2 text-sm font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
          >
            Edit
          </Link>
          <Link
            href={`/admin/content/listening/${p.id}/structure`}
            className="inline-flex min-h-9 items-center rounded-admin px-3 py-2 text-sm font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
          >
            Questions
          </Link>
          <Link
            href={`/admin/content/listening/${p.id}/audio`}
            className="inline-flex min-h-9 items-center rounded-admin px-3 py-2 text-sm font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
          >
            Audio
          </Link>
          {p.status !== 'Archived' && (
            <Button variant="ghost" size="sm" onClick={() => void archive(p.id)}>
              <ArchiveIcon className="w-4 h-4" /> Archive
            </Button>
          )}
        </div>
      ) : <span className="text-xs text-admin-fg-muted">Read only</span>,
    },
  ], [archive, canWriteContent]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout
        title="Listening Papers"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Listening' },
        ]}
      >
        <div className="p-6">
          <p className="text-sm text-admin-fg-muted">Admin access required.</p>
        </div>
      </AdminTableLayout>
    );
  }

  return (
    <AdminTableLayout
      eyebrow="CMS"
      title="Listening Papers"
      description="Author and publish OET Listening papers (Part A 24 items, Part B 6 items, Part C 12 items; total 42 items per paper). Upload audio + question paper + audio script + answer key. AI extraction proposes the question map; you review and approve."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Listening' },
      ]}
      actions={canWriteContent ? (
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="primary" size="sm" asChild>
            <Link href="/admin/content/papers?subtest=listening">
              <Plus className="mr-1.5 h-4 w-4" />
              New Listening Paper
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href="/admin/content/papers/import">
              Bulk ZIP import
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href="/admin/content/listening/policy">
              <Settings className="mr-1.5 h-4 w-4" />
              Policy
            </Link>
          </Button>
        </div>
      ) : undefined}
      banner={
        <div className="space-y-4">
          <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
            <KpiTile label="Total" value={stats.total} tone="default" icon={<Headphones className="h-4 w-4" />} />
            <KpiTile label="Published" value={stats.published} tone="success" />
            <KpiTile label="Drafts" value={stats.draft} tone="default" />
            <KpiTile label="In review" value={stats.review} tone="warning" />
            <KpiTile label="Missing assets" value={stats.missing} tone="danger" />
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Filters</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <Select label="Status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} options={STATUSES} />
                <Input label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title or slug" />
              </div>
            </CardContent>
          </Card>
        </div>
      }
      footer={
        <Card>
          <CardContent className="p-5">
            <details className="group">
              <summary className="cursor-pointer text-sm font-semibold text-admin-fg-strong">
                Authoring quick-reference: canonical Listening paper layout
              </summary>
              <div className="mt-3 space-y-3 text-sm text-admin-fg-muted">
                <div>
                  <strong className="text-admin-fg-strong">Canonical structure (42 items total):</strong>
                  <ul className="ml-5 mt-1 list-disc space-y-1">
                    <li><strong>Part A</strong>: 2 patient consultations × 12 short-answer items = <strong>24 items</strong></li>
                    <li><strong>Part B</strong>: 6 short workplace extracts × 1 multiple-choice (3-option) item = <strong>6 items</strong></li>
                    <li><strong>Part C</strong>: 2 longer presentations × 6 multiple-choice (3-option) items = <strong>12 items</strong></li>
                  </ul>
                </div>
                <div>
                  <strong className="text-admin-fg-strong">Required asset roles (4):</strong>
                  <ul className="ml-5 mt-1 list-disc space-y-1">
                    <li><code>Audio</code>: single combined MP3/WAV for the full paper</li>
                    <li><code>QuestionPaper</code>: learner-facing PDF</li>
                    <li><code>AudioScript</code>: full transcript PDF (admin/marker reference)</li>
                    <li><code>AnswerKey</code>: official key PDF</li>
                  </ul>
                </div>
                <div>
                  <strong className="text-admin-fg-strong">Publish gate checks:</strong>
                  <ul className="ml-5 mt-1 list-disc space-y-1">
                    <li>All 4 required asset roles attached as primary</li>
                    <li>Non-empty <code>SourceProvenance</code></li>
                    <li>Authored structure validates to 24+6+12=42 items with no validation errors</li>
                    <li>Every item has a non-empty correct answer; MCQ items have exactly 3 options</li>
                  </ul>
                </div>
              </div>
            </details>
          </CardContent>
        </Card>
      }
    >
      <AsyncStateWrapper status={status}>
        <DataTable data={rows} columns={columns} keyExtractor={(p) => p.id} selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
        <div className="p-4">
          <BulkActionBar
            selectedCount={selectedKeys.size}
            onClearSelection={() => setSelectedKeys(new Set())}
            actions={[
              { key: 'archive', label: bulkBusy ? 'Archiving…' : 'Archive selected', variant: 'danger', onClick: bulkArchive },
              { key: 'publish', label: bulkBusy ? 'Publishing…' : 'Publish selected', onClick: bulkPublish },
            ]}
          />
        </div>
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminTableLayout>
  );
}
