'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Archive as ArchiveIcon, CheckCircle2, PenSquare, Plus, XCircle } from 'lucide-react';
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
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUSES = [
  { value: '', label: 'Any status' },
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const LETTER_TYPES = [
  { value: '', label: 'Any letter type' },
  { value: 'routine_referral', label: 'Routine referral' },
  { value: 'urgent_referral', label: 'Urgent referral' },
  { value: 'non_medical_referral', label: 'Non-medical referral' },
  { value: 'update_discharge', label: 'Update and discharge' },
  { value: 'update_referral_specialist_to_gp', label: 'Specialist update / referral' },
  { value: 'transfer_letter', label: 'Transfer letter' },
];

const REQUIRED_WRITING_ROLES: PaperAssetRole[] = ['CaseNotes', 'ModelAnswer'];

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
  return REQUIRED_WRITING_ROLES.some((role) => !hasRole(paper, role));
}

function letterTypeLabel(value: string | null) {
  return LETTER_TYPES.find((option) => option.value === value)?.label ?? value ?? 'Missing';
}

export default function AdminWritingPapersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<ContentPaperDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [filterStatus, setFilterStatus] = useState('');
  const [filterLetterType, setFilterLetterType] = useState('');
  const [search, setSearch] = useState('');

  const load = useCallback(async () => {
    if (!isAuthenticated || role !== 'admin') return;
    setStatus('loading');
    try {
      const data = await listContentPapers({
        subtest: 'writing',
        status: filterStatus || undefined,
        letterType: filterLetterType || undefined,
        search: search || undefined,
        pageSize: 100,
      });
      setRows(data);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load writing papers: ${(e as Error).message}` });
    }
  }, [filterLetterType, filterStatus, isAuthenticated, role, search]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => { void load(); });
  }, [isAuthenticated, load, role]);

  const archive = useCallback(async (id: string) => {
    if (!canWriteContent) return;
    if (!confirm('Archive this writing paper? Learners will no longer see it.')) return;
    try {
      await archiveContentPaper(id);
      setToast({ variant: 'success', message: 'Paper archived.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Archive failed: ${(e as Error).message}` });
    }
  }, [canWriteContent, load]);

  const stats = useMemo(() => {
    let published = 0;
    let draft = 0;
    let missingAssets = 0;
    let missingLetterType = 0;
    for (const paper of rows) {
      if (paper.status === 'Published') published++;
      if (paper.status === 'Draft') draft++;
      if (isMissingRequiredAssets(paper)) missingAssets++;
      if (!paper.letterType) missingLetterType++;
    }
    return { total: rows.length, published, draft, missingAssets, missingLetterType };
  }, [rows]);

  const columns: Column<ContentPaperDto>[] = useMemo(() => [
    {
      key: 'title',
      header: 'Title',
      render: (paper) => canWriteContent ? (
        <Link href={`/admin/content/papers/${paper.id}`} className="font-medium hover:text-primary">
          {paper.title}
        </Link>
      ) : <span className="font-medium">{paper.title}</span>,
    },
    {
      key: 'letterType',
      header: 'Letter type',
      render: (paper) => paper.letterType
        ? <Badge variant="info">{letterTypeLabel(paper.letterType)}</Badge>
        : <Badge variant="danger">Missing</Badge>,
    },
    {
      key: 'scope',
      header: 'Profession scope',
      render: (paper) => paper.appliesToAllProfessions
        ? <Badge variant="muted">All professions</Badge>
        : <Badge variant="info">{paper.professionId ?? '—'}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (paper) => <Badge variant={
        paper.status === 'Published' ? 'success'
          : paper.status === 'Archived' ? 'muted'
          : paper.status === 'InReview' ? 'warning' : 'default'
      }>{paper.status}</Badge>,
    },
    {
      key: 'assets',
      header: 'Asset readiness',
      render: (paper) => (
        <div className="flex flex-wrap gap-1.5" aria-label="Required asset readiness">
          {REQUIRED_WRITING_ROLES.map((roleName) => {
            const ok = hasRole(paper, roleName);
            return (
              <span
                key={roleName}
                title={`${roleName}: ${ok ? 'present' : 'missing'}`}
                className={
                  'inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-semibold ' +
                  (ok ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger')
                }
              >
                {ok ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
                {ROLE_SHORT_LABELS[roleName]}
              </span>
            );
          })}
        </div>
      ),
    },
    { key: 'updated', header: 'Updated', render: (paper) => new Date(paper.updatedAt).toLocaleString() },
    {
      key: 'actions',
      header: 'Actions',
      render: (paper) => canWriteContent ? (
        <div className="flex gap-2">
          <Link
            href={`/admin/content/papers/${paper.id}`}
            className="inline-flex min-h-9 items-center rounded px-3 py-2 text-sm font-semibold text-navy hover:bg-background-light"
          >
            Edit
          </Link>
          {paper.status !== 'Archived' && (
            <Button variant="ghost" size="sm" onClick={() => void archive(paper.id)}>
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
    <AdminRouteWorkspace role="main" aria-label="Writing Papers">
      <AdminRouteSectionHeader
        icon={<PenSquare className="w-6 h-6" />}
        title="Writing Papers"
        description="Author and publish OET Writing tasks from real case-note and model-answer sources. Each published paper must have a letter type, source provenance, primary case notes, a primary model answer, and reviewed learner-facing authoring text."
      />

      <AdminRoutePanel title="Filters">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-5">
          <Select label="Status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} options={STATUSES} />
          <Select label="Letter type" value={filterLetterType} onChange={(e) => setFilterLetterType(e.target.value)} options={LETTER_TYPES} />
          <Input label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title or slug" />
          {canWriteContent ? (
            <div className="flex items-end gap-2 md:col-span-2">
              <Link
                href="/admin/content/papers?subtest=writing"
                className="inline-flex min-h-11 items-center justify-center rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white shadow-soft transition hover:bg-primary/90"
              >
                <Plus className="w-4 h-4 mr-1" /> Create Writing paper
              </Link>
              <Link
                href="/admin/content/papers/import"
                className="inline-flex min-h-11 items-center justify-center rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy transition hover:bg-background-light"
              >
                Bulk ZIP import
              </Link>
            </div>
          ) : null}
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title="Writing overview">
        <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
          <StatTile label="Total" value={stats.total} />
          <StatTile label="Published" value={stats.published} tone="success" />
          <StatTile label="Drafts" value={stats.draft} />
          <StatTile label="Missing assets" value={stats.missingAssets} tone="danger" />
          <StatTile label="Missing type" value={stats.missingLetterType} tone="warning" />
        </div>
      </AdminRoutePanel>

      <AsyncStateWrapper status={status}>
        <AdminRoutePanel title={`Writing papers (${rows.length})`}>
          <DataTable data={rows} columns={columns} keyExtractor={(paper) => paper.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <AdminRoutePanel>
        <details className="group">
          <summary className="cursor-pointer text-sm font-semibold text-navy">
            Authoring quick-reference: canonical Writing paper layout
          </summary>
          <div className="mt-3 space-y-3 text-sm text-muted">
            <div>
              <strong className="text-navy">Required asset roles:</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li><code>CaseNotes</code> — learner-facing stimulus PDF.</li>
                <li><code>ModelAnswer</code> — hidden reference answer PDF, shown only after submission through study views.</li>
              </ul>
            </div>
            <div>
              <strong className="text-navy">Required authoring fields:</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li>Canonical letter type, task prompt, case notes text, and model answer text.</li>
                <li>Source provenance before publish.</li>
                <li>Profession scope and access tier reviewed before learner release.</li>
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