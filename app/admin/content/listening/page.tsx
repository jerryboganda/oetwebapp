'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Headphones, Plus, Archive as ArchiveIcon, CheckCircle2, XCircle, Settings } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
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
        <Link href={`/admin/content/papers/${p.id}`} className="font-medium hover:text-primary">
          {p.title}
        </Link>
      ) : <span className="font-medium">{p.title}</span>,
    },
    { key: 'slug', header: 'Slug', render: (p) => <span className="font-mono text-xs">{p.slug}</span> },
    {
      key: 'scope', header: 'Profession scope', render: (p) => p.appliesToAllProfessions
        ? <Badge variant="muted">All professions</Badge>
        : <Badge variant="info">{p.professionId ?? '—'}</Badge>,
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
                  (ok ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger')
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
            className="inline-flex min-h-9 items-center rounded px-3 py-2 text-sm font-semibold text-navy hover:bg-background-light"
          >
            Edit
          </Link>
          <Link
            href={`/admin/content/listening/${p.id}/structure`}
            className="inline-flex min-h-9 items-center rounded px-3 py-2 text-sm font-semibold text-navy hover:bg-background-light"
          >
            Questions
          </Link>
          {p.status !== 'Archived' && (
            <Button variant="ghost" size="sm" onClick={() => void archive(p.id)}>
              <ArchiveIcon className="w-4 h-4" /> Archive
            </Button>
          )}
        </div>
      ) : <span className="text-xs text-muted">Read only</span>,
    },
  ], [archive, canWriteContent]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening Papers">
      <AdminRouteSectionHeader
        icon={<Headphones className="w-6 h-6" />}
        title="Listening Papers"
        description="Author and publish OET Listening papers (Part A consultations 24 items, Part B workplace extracts 6 items, Part C presentations 12 items — total 42 items per paper). Upload audio + question paper + audio script + answer key. AI extraction proposes the question map; you review and approve."
      />

      <AdminRoutePanel title="Filters">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <Select label="Status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} options={STATUSES} />
          <Input label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title or slug" />
          {canWriteContent ? (
            <div className="flex items-end gap-2 md:col-span-2">
              <Link
                href="/admin/content/papers?subtest=listening"
                className="inline-flex min-h-11 items-center justify-center rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white shadow-soft transition hover:bg-primary/90 dark:bg-violet-700 dark:hover:bg-violet-600"
              >
                <Plus className="w-4 h-4 mr-1" /> Create Listening paper
              </Link>
              <Link
                href="/admin/content/papers/import"
                className="inline-flex min-h-11 items-center justify-center rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy transition hover:bg-background-light"
              >
                Bulk ZIP import
              </Link>
              <Link
                href="/admin/content/listening/policy"
                className="inline-flex min-h-11 items-center justify-center rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy transition hover:bg-background-light"
              >
                <Settings className="w-4 h-4 mr-1" /> Policy Settings
              </Link>
            </div>
          ) : null}
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title="Listening overview">
        <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
          <StatTile label="Total" value={stats.total} />
          <StatTile label="Published" value={stats.published} tone="success" />
          <StatTile label="Drafts" value={stats.draft} />
          <StatTile label="In review" value={stats.review} tone="warning" />
          <StatTile label="Missing assets" value={stats.missing} tone="danger" />
        </div>
      </AdminRoutePanel>

      <AsyncStateWrapper status={status}>
        <AdminRoutePanel title={`Listening papers (${rows.length})`}>
          <DataTable data={rows} columns={columns} keyExtractor={(p) => p.id} selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
          <BulkActionBar
            selectedCount={selectedKeys.size}
            onClearSelection={() => setSelectedKeys(new Set())}
            actions={[
              { key: 'archive', label: bulkBusy ? 'Archiving…' : 'Archive selected', variant: 'danger', onClick: bulkArchive },
              { key: 'publish', label: bulkBusy ? 'Publishing…' : 'Publish selected', onClick: bulkPublish },
            ]}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <AdminRoutePanel>
        <details className="group">
          <summary className="cursor-pointer text-sm font-semibold text-admin-text">
            Authoring quick-reference: canonical Listening paper layout
          </summary>
          <div className="mt-3 space-y-3 text-sm text-admin-text-muted">
            <div>
              <strong className="text-admin-text">Canonical structure (42 items total):</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li><strong>Part A</strong> — 2 patient consultations × 12 short-answer items = <strong>24 items</strong></li>
                <li><strong>Part B</strong> — 6 short workplace extracts × 1 multiple-choice (3-option) item = <strong>6 items</strong></li>
                <li><strong>Part C</strong> — 2 longer presentations × 6 multiple-choice (3-option) items = <strong>12 items</strong></li>
              </ul>
            </div>
            <div>
              <strong className="text-admin-text">Required asset roles (4):</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li><code>Audio</code> — single combined MP3/WAV for the full paper</li>
                <li><code>QuestionPaper</code> — learner-facing PDF</li>
                <li><code>AudioScript</code> — full transcript PDF (admin/marker reference)</li>
                <li><code>AnswerKey</code> — official key PDF</li>
              </ul>
            </div>
            <div>
              <strong className="text-admin-text">Publish gate checks:</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li>All 4 required asset roles attached as primary</li>
                <li>Non-empty <code>SourceProvenance</code></li>
                <li>Authored structure validates to 24+6+12=42 items with no validation errors</li>
                <li>Every item has a non-empty correct answer; MCQ items have exactly 3 options</li>
              </ul>
            </div>
          </div>
        </details>
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}

function StatTile({ label, value, tone }: { label: string; value: number; tone?: 'success' | 'warning' | 'danger' }) {
  const toneClass =
    tone === 'success' ? 'text-success'
      : tone === 'warning' ? 'text-warning'
      : tone === 'danger' ? 'text-danger'
      : 'text-navy';
  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted">{label}</div>
      <div className={`mt-1 text-2xl font-bold ${toneClass}`}>{value}</div>
    </div>
  );
}
