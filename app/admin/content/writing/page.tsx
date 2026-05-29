'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Archive as ArchiveIcon, CheckCircle2, PenSquare, Plus, XCircle } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  approvePublishWritingPaper,
  archiveContentPaper,
  listContentPapers,
  rejectWritingPaper,
  submitWritingPaperForReview,
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
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<ContentPaperDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [filterStatus, setFilterStatus] = useState('');
  const [filterLetterType, setFilterLetterType] = useState('');
  const [search, setSearch] = useState('');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

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

  const submitForReview = useCallback(async (id: string) => {
    if (!canWriteContent) return;
    try {
      await submitWritingPaperForReview(id);
      setToast({ variant: 'success', message: 'Paper submitted for review.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Submit for review failed: ${(e as Error).message}` });
    }
  }, [canWriteContent, load]);

  const approvePublish = useCallback(async (id: string) => {
    if (!canPublishContent) return;
    if (!confirm('Approve and publish this writing paper? Learners will see it immediately.')) return;
    try {
      await approvePublishWritingPaper(id);
      setToast({ variant: 'success', message: 'Paper approved and published.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Approve & publish failed: ${(e as Error).message}` });
    }
  }, [canPublishContent, load]);

  const reject = useCallback(async (id: string) => {
    if (!canPublishContent) return;
    const reason = prompt('Rejection reason (shown on audit trail):');
    if (!reason || !reason.trim()) return;
    try {
      await rejectWritingPaper(id, reason.trim());
      setToast({ variant: 'success', message: 'Paper rejected.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Reject failed: ${(e as Error).message}` });
    }
  }, [canPublishContent, load]);

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
        <Link href={`/admin/content/papers/${paper.id}`} className="font-medium hover:text-[var(--admin-primary)]">
          {paper.title}
        </Link>
      ) : <span className="font-medium">{paper.title}</span>,
    },
    {
      key: 'letterType',
      header: 'Letter type',
      render: (paper) => paper.letterType
        ? <Badge variant="info" intensity="tinted">{letterTypeLabel(paper.letterType)}</Badge>
        : <Badge variant="danger" intensity="tinted">Missing</Badge>,
    },
    {
      key: 'scope',
      header: 'Profession scope',
      render: (paper) => paper.appliesToAllProfessions
        ? <Badge variant="default" intensity="tinted">All professions</Badge>
        : <Badge variant="info" intensity="tinted">{paper.professionId ?? '—'}</Badge>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (paper) => <Badge variant={
        paper.status === 'Published' ? 'success'
          : paper.status === 'Archived' ? 'default'
          : paper.status === 'InReview' ? 'warning' : 'secondary'
      } intensity="tinted">{paper.status}</Badge>,
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
                  (ok
                    ? 'bg-[var(--admin-success-tint)] text-[var(--admin-success)]'
                    : 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]')
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
        <div className="flex flex-wrap gap-2">
          <Button asChild variant="ghost" size="sm">
            <Link href={`/admin/content/papers/${paper.id}`}>Edit</Link>
          </Button>
          {paper.status === 'Draft' && (
            <Button variant="ghost" size="sm" onClick={() => void submitForReview(paper.id)}>
              Submit for review
            </Button>
          )}
          {paper.status === 'InReview' && (
            <Button asChild variant="ghost" size="sm">
              <Link href={`/admin/writing/tasks/${paper.id}/review`}>Review</Link>
            </Button>
          )}
          {paper.status === 'InReview' && canPublishContent && (
            <>
              <Button variant="ghost" size="sm" onClick={() => void approvePublish(paper.id)}>
                Approve &amp; publish
              </Button>
              <Button variant="ghost" size="sm" onClick={() => void reject(paper.id)}>
                Reject
              </Button>
            </>
          )}
          {paper.status !== 'Archived' && (
            <Button variant="ghost" size="sm" onClick={() => void archive(paper.id)}>
              <ArchiveIcon className="w-4 h-4" /> Archive
            </Button>
          )}
        </div>
      ) : <span className="text-xs text-admin-fg-muted">Read only</span>,
    },
  ], [approvePublish, archive, canPublishContent, canWriteContent, reject, submitForReview]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout
        title="Writing Papers"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Writing' },
        ]}
      >
        <div className="p-6 text-sm text-admin-fg-muted">Admin access required.</div>
      </AdminTableLayout>
    );
  }

  const headerActions = canWriteContent ? (
    <>
      <Button asChild variant="primary" size="sm">
        <Link href="/admin/writing/tasks/new">
          <Plus className="w-4 h-4" /> Create Writing task
        </Link>
      </Button>
      <Button asChild variant="secondary" size="sm">
        <Link href="/admin/content/papers/import">Bulk ZIP import</Link>
      </Button>
    </>
  ) : null;

  const banner = (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <KpiTile size="sm" label="Total" value={stats.total} />
        <KpiTile size="sm" label="Published" value={stats.published} tone="success" />
        <KpiTile size="sm" label="Drafts" value={stats.draft} />
        <KpiTile size="sm" label="Missing assets" value={stats.missingAssets} tone="danger" />
        <KpiTile size="sm" label="Missing type" value={stats.missingLetterType} tone="warning" />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Filters</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Select label="Status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} options={STATUSES} />
            <Select label="Letter type" value={filterLetterType} onChange={(e) => setFilterLetterType(e.target.value)} options={LETTER_TYPES} />
            <Input label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title or slug" />
          </div>
        </CardContent>
      </Card>
    </div>
  );

  const footer = (
    <Card>
      <CardContent>
        <details className="group">
          <summary className="cursor-pointer text-sm font-semibold text-admin-fg-strong">
            Authoring quick-reference: canonical Writing paper layout
          </summary>
          <div className="mt-3 space-y-3 text-sm text-admin-fg-muted">
            <div>
              <strong className="text-admin-fg-strong">Required asset roles:</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li><code>CaseNotes</code>: learner-facing stimulus PDF.</li>
                <li><code>ModelAnswer</code>: hidden reference answer PDF, shown only after submission through study views.</li>
              </ul>
            </div>
            <div>
              <strong className="text-admin-fg-strong">Required authoring fields:</strong>
              <ul className="ml-5 mt-1 list-disc space-y-1">
                <li>Canonical letter type, task prompt, case notes text, and model answer text.</li>
                <li>Source provenance before publish.</li>
                <li>Profession scope and access tier reviewed before learner release.</li>
              </ul>
            </div>
          </div>
        </details>
      </CardContent>
    </Card>
  );

  return (
    <>
      <AdminTableLayout
        title="Writing Papers"
        description="Author and publish OET Writing tasks from real case-note and model-answer sources. Each published paper must have a letter type, source provenance, primary case notes, a primary model answer, and reviewed learner-facing authoring text."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Writing' },
        ]}
        eyebrow="Catalog"
        actions={headerActions}
        banner={banner}
        footer={footer}
      >
        <AsyncStateWrapper status={status}>
          <div className="p-4 sm:p-5">
            <div className="mb-3 flex items-center gap-2">
              <PenSquare className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
              <p className="text-sm font-medium text-admin-fg-default">
                Writing papers ({rows.length})
              </p>
            </div>
            <DataTable
              data={rows}
              columns={columns}
              keyExtractor={(paper) => paper.id}
              selectable
              selectedKeys={selectedKeys}
              onSelectionChange={setSelectedKeys}
            />
            <BulkActionBar
              selectedCount={selectedKeys.size}
              onClearSelection={() => setSelectedKeys(new Set())}
              actions={[
                { key: 'archive', label: 'Archive selected', variant: 'danger', onClick: () => {} },
                { key: 'publish', label: 'Publish selected', onClick: () => {} },
              ]}
            />
          </div>
        </AsyncStateWrapper>
      </AdminTableLayout>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
