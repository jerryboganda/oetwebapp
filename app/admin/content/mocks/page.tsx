'use client';

import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Archive, BarChart3, CalendarClock, CheckCircle, Layers, Pencil, Plus, Search, ShieldAlert, Sparkles } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { type Column } from '@/components/ui/data-table';
import {
  AdminManagedTable,
  type ManagedBulkAction,
  type BulkResult,
} from '@/components/admin/managed-table/admin-managed-table';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import {
  addAdminMockBundleSection,
  bulkAdminMockBundles,
  createAdminMockBundle,
  fetchAdminMockBundles,
  listAdminMockLeakReports,
  publishAdminMockBundle,
  updateAdminMockBundle,
} from '@/lib/api';
import { listContentPapers, type ContentPaperDto } from '@/lib/content-upload-api';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

type MockBundleRow = {
  id: string;
  title: string;
  mockType: 'full' | 'lrw' | 'sub' | 'part' | 'diagnostic' | 'final_readiness' | 'remedial';
  subtestCode: string | null;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  status: 'draft' | 'published' | 'archived';
  estimatedDurationMinutes: number;
  priority?: number;
  tagsCsv?: string;
  sourceProvenance: string | null;
  difficulty?: string;
  sourceStatus?: string;
  qualityStatus?: string;
  releasePolicy?: string;
  topicTagsCsv?: string;
  skillTagsCsv?: string;
  watermarkEnabled?: boolean;
  randomiseQuestions?: boolean;
  sections: {
    id: string;
    sectionOrder: number;
    subtestCode: string;
    contentPaperId: string;
    contentPaperTitle?: string | null;
    contentPaperStatus?: string | null;
    timeLimitMinutes: number;
    reviewEligible: boolean;
  }[];
};

type MockBundleFormState = {
  title: string;
  mockType: MockBundleRow['mockType'];
  subtestCode: string;
  professionId: string;
  appliesToAllProfessions: boolean;
  sourceProvenance: string;
  priority: string;
  difficulty: string;
  sourceStatus: string;
  qualityStatus: string;
  releasePolicy: string;
  topicTagsCsv: string;
  skillTagsCsv: string;
  watermarkEnabled: boolean;
  randomiseQuestions: boolean;
};

const mockTypeOptions = ['full', 'lrw', 'sub', 'part', 'diagnostic', 'final_readiness', 'remedial'] as const;
const subtestOptions = ['listening', 'reading', 'writing', 'speaking'];

function isSubtestScopedType(type: MockBundleRow['mockType']) {
  return type === 'sub' || type === 'part' || type === 'remedial';
}

function toEditForm(row: MockBundleRow): MockBundleFormState {
  return {
    title: row.title,
    mockType: row.mockType,
    subtestCode: row.subtestCode ?? 'reading',
    professionId: row.professionId ?? '',
    appliesToAllProfessions: row.appliesToAllProfessions,
    sourceProvenance: row.sourceProvenance ?? '',
    priority: String(row.priority ?? 0),
    difficulty: row.difficulty ?? 'exam_ready',
    sourceStatus: row.sourceStatus ?? 'needs_review',
    qualityStatus: row.qualityStatus ?? 'draft',
    releasePolicy: row.releasePolicy ?? 'instant',
    topicTagsCsv: row.topicTagsCsv ?? '',
    skillTagsCsv: row.skillTagsCsv ?? '',
    watermarkEnabled: row.watermarkEnabled ?? true,
    randomiseQuestions: row.randomiseQuestions ?? false,
  };
}

export default function AdminMockBundlesPage() {
  const { user } = useCurrentUser();
  const userPermissions = user?.adminPermissions;
  const canManageBundles = hasPermission(userPermissions, AdminPermission.ContentWrite);
  const canPublishBundles = hasPermission(userPermissions, AdminPermission.ContentPublish);
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<MockBundleRow[]>([]);
  const [status, setStatus] = useState('');
  const [mockType, setMockType] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [newTitle, setNewTitle] = useState('');
  const [newType, setNewType] = useState<MockBundleRow['mockType']>('full');
  const [newSubtest, setNewSubtest] = useState('reading');
  const [newProvenance, setNewProvenance] = useState('Source: Admin-authored mock bundle assembled from published platform content.');
  const [newDifficulty, setNewDifficulty] = useState('exam_ready');
  const [newSourceStatus, setNewSourceStatus] = useState('needs_review');
  const [newQualityStatus, setNewQualityStatus] = useState('draft');
  const [newReleasePolicy, setNewReleasePolicy] = useState('instant');
  const [newTopicTags, setNewTopicTags] = useState('');
  const [newSkillTags, setNewSkillTags] = useState('');
  const [newWatermarkEnabled, setNewWatermarkEnabled] = useState(true);
  const [newRandomiseQuestions, setNewRandomiseQuestions] = useState(false);
  const [sectionBundleId, setSectionBundleId] = useState('');
  const [sectionPaperId, setSectionPaperId] = useState('');
  const [sectionTimeLimit, setSectionTimeLimit] = useState('60');
  const [paperSearch, setPaperSearch] = useState('');
  const [paperSubtest, setPaperSubtest] = useState('');
  const [paperOptions, setPaperOptions] = useState<ContentPaperDto[]>([]);
  const [paperLoading, setPaperLoading] = useState(false);
  const [editingRow, setEditingRow] = useState<MockBundleRow | null>(null);
  const [editForm, setEditForm] = useState<MockBundleFormState | null>(null);
  const [savingEdit, setSavingEdit] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetchAdminMockBundles({
        status: status || undefined,
        mockType: mockType || undefined,
      }) as { items?: MockBundleRow[] };
      setRows(response.items ?? []);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load mock bundles.' });
    } finally {
      setLoading(false);
    }
  }, [mockType, status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleCreate() {
    try {
      await createAdminMockBundle({
        title: newTitle,
        mockType: newType,
        subtestCode: newType === 'sub' || newType === 'part' || newType === 'remedial' ? newSubtest : null,
        professionId: null,
        appliesToAllProfessions: true,
        sourceProvenance: newProvenance,
        priority: 0,
        tagsCsv: '',
        difficulty: newDifficulty,
        sourceStatus: newSourceStatus,
        qualityStatus: newQualityStatus,
        releasePolicy: newReleasePolicy,
        topicTagsCsv: newTopicTags,
        skillTagsCsv: newSkillTags,
        watermarkEnabled: newWatermarkEnabled,
        randomiseQuestions: newRandomiseQuestions,
      });
      setToast({ variant: 'success', message: 'Mock bundle created.' });
      setNewTitle('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create failed.' });
    }
  }

  async function handleAddSection() {
    try {
      await addAdminMockBundleSection(sectionBundleId, {
        contentPaperId: sectionPaperId,
        timeLimitMinutes: Number(sectionTimeLimit),
      });
      setToast({ variant: 'success', message: 'Section added.' });
      setSectionPaperId('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Section add failed.' });
    }
  }

  async function handleSearchPapers() {
    setPaperLoading(true);
    try {
      const papers = await listContentPapers({
        status: 'Published',
        subtest: paperSubtest || undefined,
        search: paperSearch || undefined,
        pageSize: 50,
      });
      setPaperOptions(papers);
      if (!sectionPaperId && papers[0]) {
        setSectionPaperId(papers[0].id);
      }
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Could not load published papers.' });
    } finally {
      setPaperLoading(false);
    }
  }

  function openEdit(row: MockBundleRow) {
    setEditingRow(row);
    setEditForm(toEditForm(row));
  }

  function closeEdit() {
    setEditingRow(null);
    setEditForm(null);
    setSavingEdit(false);
  }

  function updateEditField<K extends keyof MockBundleFormState>(key: K, value: MockBundleFormState[K]) {
    setEditForm((current) => current ? { ...current, [key]: value } : current);
  }

  async function handleUpdateBundle(event: FormEvent) {
    event.preventDefault();
    if (!editingRow || !editForm || savingEdit) return;
    if (!editForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    setSavingEdit(true);
    try {
      await updateAdminMockBundle(editingRow.id, {
        title: editForm.title.trim(),
        mockType: editForm.mockType,
        subtestCode: isSubtestScopedType(editForm.mockType) ? editForm.subtestCode : null,
        professionId: editForm.appliesToAllProfessions ? null : editForm.professionId.trim() || null,
        appliesToAllProfessions: editForm.appliesToAllProfessions,
        sourceProvenance: editForm.sourceProvenance.trim() || null,
        priority: Number(editForm.priority) || 0,
        difficulty: editForm.difficulty,
        sourceStatus: editForm.sourceStatus,
        qualityStatus: editForm.qualityStatus,
        releasePolicy: editForm.releasePolicy,
        topicTagsCsv: editForm.topicTagsCsv,
        skillTagsCsv: editForm.skillTagsCsv,
        watermarkEnabled: editForm.watermarkEnabled,
        randomiseQuestions: editForm.randomiseQuestions,
      });
      setToast({ variant: 'success', message: 'Mock bundle updated.' });
      closeEdit();
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Update failed.' });
      setSavingEdit(false);
    }
  }

  async function handlePublish(id: string) {
    try {
      await publishAdminMockBundle(id);
      setToast({ variant: 'success', message: 'Mock bundle published.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish gate failed.' });
    }
  }

  const pagedRows = useMemo(
    () => rows.slice((page - 1) * pageSize, page * pageSize),
    [rows, page, pageSize],
  );

  const columns = useMemo<Column<MockBundleRow>[]>(() => [
    {
      key: 'title',
      header: 'Title',
      render: (row) => (
        <div className="min-w-0">
          <p className="font-semibold text-admin-fg-strong line-clamp-1">{row.title}</p>
          <p className="mt-0.5 font-mono text-xs text-admin-fg-muted line-clamp-1">{row.id}</p>
        </div>
      ),
    },
    {
      key: 'mockType',
      header: 'Type',
      render: (row) => <Badge variant="default">{row.mockType.replace(/_/g, ' ')}</Badge>,
    },
    {
      key: 'subtest',
      header: 'Subtest',
      render: (row) => (row.subtestCode ? <Badge variant="default">{row.subtestCode}</Badge> : <span className="text-admin-fg-muted">—</span>),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'info'}>
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'quality',
      header: 'Quality',
      render: (row) => (row.qualityStatus
        ? <Badge variant={row.qualityStatus === 'approved' ? 'success' : row.qualityStatus === 'draft' ? 'muted' : 'warning'}>{row.qualityStatus}</Badge>
        : <span className="text-admin-fg-muted">—</span>),
      hideOnMobile: true,
    },
    {
      key: 'sections',
      header: 'Sections',
      render: (row) => <span className="text-sm text-admin-fg-default">{row.sections.length}</span>,
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link
            href={`/admin/content/mocks/${encodeURIComponent(row.id)}/item-analysis`}
            aria-label={`Item analysis for ${row.title}`}
            className="inline-flex items-center rounded-lg border border-border px-2.5 py-1.5 text-sm font-medium text-admin-fg-strong hover:bg-admin-bg-subtle"
          >
            <BarChart3 className="h-4 w-4" />
          </Link>
          {canPublishBundles && row.status !== 'published' ? (
            <Button size="sm" variant="primary" onClick={() => handlePublish(row.id)} aria-label={`Publish ${row.title}`}>
              <CheckCircle className="mr-1 h-4 w-4" /> Publish
            </Button>
          ) : null}
          {canManageBundles && row.status !== 'archived' ? (
            <Button size="sm" variant="outline" onClick={() => openEdit(row)} aria-label={`Edit ${row.title}`}>
              <Pencil className="mr-1 h-4 w-4" /> Edit
            </Button>
          ) : null}
          {canManageBundles ? (
            <Button size="sm" variant="outline" onClick={() => setSectionBundleId(row.id)} aria-label={`Add section to ${row.title}`}>
              <Plus className="mr-1 h-4 w-4" /> Section
            </Button>
          ) : null}
        </div>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [canManageBundles, canPublishBundles]);

  const bulkActions = useMemo<ManagedBulkAction<MockBundleRow>[]>(() => {
    if (!canManageBundles) return [];
    const actions: ManagedBulkAction<MockBundleRow>[] = [];
    if (canPublishBundles) {
      actions.push({
        key: 'publish',
        label: 'Publish',
        icon: <CheckCircle className="h-4 w-4" />,
        variant: 'primary',
        isEligible: (row) => row.status === 'draft',
        run: (ids) => bulkAdminMockBundles('publish', ids),
      });
    }
    actions.push({
      key: 'archive',
      label: 'Archive',
      icon: <Archive className="h-4 w-4" />,
      variant: 'danger',
      isEligible: (row) => row.status !== 'archived',
      confirm: {
        title: (n) => `Archive ${n} ${n === 1 ? 'bundle' : 'bundles'}?`,
        description: (n) =>
          `${n} ${n === 1 ? 'bundle' : 'bundles'} will be archived (soft) and hidden from learners. An admin can restore them later.`,
        confirmLabel: 'Archive',
        destructive: true,
      },
      run: (ids) => bulkAdminMockBundles('archive', ids),
    });
    return actions;
  }, [canManageBundles, canPublishBundles]);

  function handleBulkResult(action: ManagedBulkAction<MockBundleRow>, result: BulkResult) {
    const verb = action.key === 'publish' ? 'Published' : 'Archived';
    const noun = result.succeeded === 1 ? 'bundle' : 'bundles';
    setToast({
      variant: 'success',
      message: `${verb} ${result.succeeded} ${noun} (${result.skipped ?? 0} skipped, ${result.failed ?? 0} failed)`,
    });
    void load();
  }

  function handleBulkError() {
    setToast({ variant: 'error', message: 'Bulk action failed.' });
  }

  return (
    <>
      <AdminOperationsLayout
        eyebrow="Content"
        title="Mock Bundles"
        description="Assemble learner mock routes from published ContentPaper sections and publish them through the mock-specific gate."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Mocks' },
        ]}
      >
        <Card>
          <CardContent className="p-5 space-y-6">
          {canManageBundles ? (
          <div className="rounded-admin border border-[var(--admin-primary-tint-strong)] bg-[var(--admin-primary-tint)] p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-sm font-bold text-admin-fg-strong">Build a complete mock end-to-end</p>
                <p className="text-xs text-admin-fg-muted">
                  The wizard walks you through Bundle metadata → Listening → Reading → Writing →
                  Speaking → Review &amp; publish in one flow.
                </p>
              </div>
              <Button variant="primary" asChild>
<Link href="/admin/content/mocks/wizard">
                  <Sparkles className="mr-2 h-4 w-4" />
                  Build a complete mock with the wizard
                </Link>
</Button>
            </div>
          </div>
          ) : null}

          <div className="mb-6 flex flex-wrap gap-3">
            <Link
              href="/admin/content/mocks/item-analysis"
              className="inline-flex items-center rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-2 text-sm font-bold text-admin-fg-strong hover:bg-admin-bg-subtle"
            >
              <BarChart3 className="mr-2 h-4 w-4" /> Item analysis
            </Link>
            <Link
              href="/admin/content/mocks/operations"
              className="inline-flex items-center rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-2 text-sm font-bold text-admin-fg-strong hover:bg-admin-bg-subtle"
            >
              <CalendarClock className="mr-2 h-4 w-4" /> Mock operations
            </Link>
            <LeakReportsLink />
          </div>

          {canManageBundles ? (
          <div className="mb-6 grid gap-4 rounded-admin border border-admin-border bg-admin-bg-subtle p-4 lg:grid-cols-[1.2fr_0.8fr]">
            <div className="space-y-3">
              <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">Create bundle</p>
              <div className="grid gap-3 md:grid-cols-2">
                <Input label="Title" value={newTitle} onChange={(e) => setNewTitle(e.target.value)} placeholder="Full Mock Route 1" />
                <Input label="Provenance" value={newProvenance} onChange={(e) => setNewProvenance(e.target.value)} />
                <Input label="Topic tags" value={newTopicTags} onChange={(e) => setNewTopicTags(e.target.value)} placeholder="cardiology, discharge" />
                <Input label="Skill tags" value={newSkillTags} onChange={(e) => setNewSkillTags(e.target.value)} placeholder="inference, purpose, fluency" />
              </div>
              <div className="flex flex-wrap gap-2">
                {mockTypeOptions.map((type) => (
                  <Button key={type} variant={newType === type ? 'primary' : 'secondary'} onClick={() => setNewType(type)}>
                    {type.replace(/_/g, ' ')}
                  </Button>
                ))}
                {newType === 'sub' || newType === 'part' || newType === 'remedial' ? (
                  <div className="flex flex-wrap gap-2">
                    {subtestOptions.map((subtest) => (
                      <Button key={subtest} variant={newSubtest === subtest ? 'primary' : 'secondary'} onClick={() => setNewSubtest(subtest)}>
                        {subtest}
                      </Button>
                    ))}
                  </div>
                ) : null}
                <Button variant="primary" onClick={handleCreate} disabled={!newTitle.trim()}>
                  <Plus className="mr-1 h-4 w-4" /> Create
                </Button>
              </div>
              <div className="grid gap-3 md:grid-cols-4">
                <NativeSelect label="Difficulty" value={newDifficulty} onChange={(e) => setNewDifficulty(e.target.value)} options={[
                  { value: 'foundation', label: 'Foundation' },
                  { value: 'developing', label: 'Developing' },
                  { value: 'exam_ready', label: 'Exam ready' },
                  { value: 'stretch', label: 'Stretch' },
                ]} />
                <NativeSelect label="Source status" value={newSourceStatus} onChange={(e) => setNewSourceStatus(e.target.value)} options={[
                  { value: 'needs_review', label: 'Needs review' },
                  { value: 'original', label: 'Original' },
                  { value: 'licensed', label: 'Licensed' },
                  { value: 'official_sample', label: 'Official sample' },
                ]} />
                <NativeSelect label="QA status" value={newQualityStatus} onChange={(e) => setNewQualityStatus(e.target.value)} options={[
                  { value: 'draft', label: 'Draft' },
                  { value: 'in_review', label: 'In review' },
                  { value: 'approved', label: 'Approved' },
                  { value: 'pilot', label: 'Pilot' },
                  { value: 'retired', label: 'Retired' },
                ]} />
                <NativeSelect label="Release policy" value={newReleasePolicy} onChange={(e) => setNewReleasePolicy(e.target.value)} options={[
                  { value: 'instant', label: 'Instant' },
                  { value: 'after_teacher_marking', label: 'After teacher marking' },
                  { value: 'scheduled', label: 'Scheduled' },
                ]} />
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm font-semibold text-admin-fg-strong">
                  <input
                    type="checkbox"
                    checked={newWatermarkEnabled}
                    onChange={(e) => setNewWatermarkEnabled(e.target.checked)}
                    className="h-4 w-4 rounded border-border"
                  />
                  Watermark learner player/report
                </label>
                <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm font-semibold text-admin-fg-strong">
                  <input
                    type="checkbox"
                    checked={newRandomiseQuestions}
                    onChange={(e) => setNewRandomiseQuestions(e.target.checked)}
                    className="h-4 w-4 rounded border-border"
                  />
                  Randomise question order with seed
                </label>
              </div>
            </div>

            <div className="space-y-3">
              <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">Add section</p>
              <Input label="Bundle ID" value={sectionBundleId} onChange={(e) => setSectionBundleId(e.target.value)} placeholder="mock-bundle-..." />
              <div className="grid gap-3 sm:grid-cols-[1fr_auto]">
                <Input label="Find published paper" value={paperSearch} onChange={(e) => setPaperSearch(e.target.value)} placeholder="title, topic, paper id" />
                <Button className="self-end" variant="secondary" onClick={handleSearchPapers} loading={paperLoading} disabled={paperLoading}>
                  <Search className="h-4 w-4" /> Search
                </Button>
              </div>
              <NativeSelect
                label="Paper subtest filter"
                value={paperSubtest}
                onChange={(e) => setPaperSubtest(e.target.value)}
                options={[{ value: '', label: 'All subtests' }, ...subtestOptions.map((subtest) => ({ value: subtest, label: subtest }))]}
              />
              {paperOptions.length > 0 ? (
                <NativeSelect
                  label="Published ContentPaper"
                  value={sectionPaperId}
                  onChange={(e) => setSectionPaperId(e.target.value)}
                  placeholder="Choose a published paper"
                  options={paperOptions.map((paper) => ({
                    value: paper.id,
                    label: `${paper.title} · ${paper.subtestCode} · ${paper.status}`,
                  }))}
                />
              ) : null}
              <Input label="Content paper ID" value={sectionPaperId} onChange={(e) => setSectionPaperId(e.target.value)} placeholder="paper-..." hint="Use the picker above, or paste a known paper id for a precise attach." />
              <Input label="Time limit minutes" value={sectionTimeLimit} onChange={(e) => setSectionTimeLimit(e.target.value)} />
              <Button variant="primary" onClick={handleAddSection} disabled={!sectionBundleId || !sectionPaperId}>
                Add section
              </Button>
            </div>
          </div>
          ) : null}

          <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:max-w-xl">
            <NativeSelect label="Status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} options={[
              { value: '', label: 'All' },
              { value: 'draft', label: 'Draft' },
              { value: 'published', label: 'Published' },
              { value: 'archived', label: 'Archived' },
            ]} />
            <NativeSelect label="Type" value={mockType} onChange={(e) => { setMockType(e.target.value); setPage(1); }} options={[
              { value: '', label: 'All' },
              { value: 'full', label: 'Full' },
              { value: 'lrw', label: 'LRW' },
              { value: 'sub', label: 'Sub-test' },
              { value: 'part', label: 'Part' },
              { value: 'diagnostic', label: 'Diagnostic' },
              { value: 'final_readiness', label: 'Final readiness' },
              { value: 'remedial', label: 'Remedial' },
            ]} />
          </div>

          <AdminManagedTable
            columns={columns}
            data={pagedRows}
            keyExtractor={(r) => r.id}
            total={rows.length}
            loading={loading}
            page={page}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={(s) => { setPageSize(s); setPage(1); }}
            pageSizeOptions={[10, 25, 50, 100]}
            itemLabel="bundle"
            itemLabelPlural="bundles"
            bulkActions={bulkActions}
            onResult={handleBulkResult}
            onError={handleBulkError}
            emptyState={
              loading ? undefined : (
                <EmptyState
                  icon={<Layers className="h-6 w-6" />}
                  title="No mock bundles yet"
                  description="Create a bundle, attach published ContentPaper sections, then publish it for learners."
                />
              )
            }
          />
          </CardContent>
        </Card>
      </AdminOperationsLayout>

      <Modal open={Boolean(editingRow && editForm)} onClose={closeEdit} title="Edit mock bundle" size="lg">
        {editForm ? (
          <form className="space-y-5" onSubmit={handleUpdateBundle}>
            <div className="grid gap-3 md:grid-cols-2">
              <Input label="Edit title" value={editForm.title} onChange={(e) => updateEditField('title', e.target.value)} />
              <Input label="Edit provenance" value={editForm.sourceProvenance} onChange={(e) => updateEditField('sourceProvenance', e.target.value)} />
              <Input label="Edit topic tags" value={editForm.topicTagsCsv} onChange={(e) => updateEditField('topicTagsCsv', e.target.value)} />
              <Input label="Edit skill tags" value={editForm.skillTagsCsv} onChange={(e) => updateEditField('skillTagsCsv', e.target.value)} />
            </div>
            <div className="grid gap-3 md:grid-cols-4">
              <NativeSelect
                label="Edit mock type"
                value={editForm.mockType}
                onChange={(e) => updateEditField('mockType', e.target.value as MockBundleRow['mockType'])}
                options={mockTypeOptions.map((type) => ({ value: type, label: type.replace(/_/g, ' ') }))}
              />
              <NativeSelect
                label="Edit subtest"
                value={editForm.subtestCode}
                onChange={(e) => updateEditField('subtestCode', e.target.value)}
                disabled={!isSubtestScopedType(editForm.mockType)}
                options={subtestOptions.map((subtest) => ({ value: subtest, label: subtest }))}
              />
              <NativeSelect
                label="Edit difficulty"
                value={editForm.difficulty}
                onChange={(e) => updateEditField('difficulty', e.target.value)}
                options={[
                  { value: 'foundation', label: 'Foundation' },
                  { value: 'developing', label: 'Developing' },
                  { value: 'exam_ready', label: 'Exam ready' },
                  { value: 'stretch', label: 'Stretch' },
                ]}
              />
              <Input label="Edit priority" value={editForm.priority} onChange={(e) => updateEditField('priority', e.target.value)} inputMode="numeric" />
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              <NativeSelect
                label="Edit source status"
                value={editForm.sourceStatus}
                onChange={(e) => updateEditField('sourceStatus', e.target.value)}
                options={[
                  { value: 'needs_review', label: 'Needs review' },
                  { value: 'original', label: 'Original' },
                  { value: 'licensed', label: 'Licensed' },
                  { value: 'official_sample', label: 'Official sample' },
                ]}
              />
              <NativeSelect
                label="Edit QA status"
                value={editForm.qualityStatus}
                onChange={(e) => updateEditField('qualityStatus', e.target.value)}
                options={[
                  { value: 'draft', label: 'Draft' },
                  { value: 'in_review', label: 'In review' },
                  { value: 'approved', label: 'Approved' },
                  { value: 'pilot', label: 'Pilot' },
                  { value: 'retired', label: 'Retired' },
                ]}
              />
              <NativeSelect
                label="Edit release policy"
                value={editForm.releasePolicy}
                onChange={(e) => updateEditField('releasePolicy', e.target.value)}
                options={[
                  { value: 'instant', label: 'Instant' },
                  { value: 'after_teacher_marking', label: 'After teacher marking' },
                  { value: 'scheduled', label: 'Scheduled' },
                ]}
              />
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm font-semibold text-admin-fg-strong">
                <input type="checkbox" checked={editForm.appliesToAllProfessions} onChange={(e) => updateEditField('appliesToAllProfessions', e.target.checked)} className="h-4 w-4 rounded border-border" />
                Applies to all professions
              </label>
              <Input label="Edit profession id" value={editForm.professionId} onChange={(e) => updateEditField('professionId', e.target.value)} disabled={editForm.appliesToAllProfessions} />
              <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm font-semibold text-admin-fg-strong">
                <input type="checkbox" checked={editForm.watermarkEnabled} onChange={(e) => updateEditField('watermarkEnabled', e.target.checked)} className="h-4 w-4 rounded border-border" />
                Watermark enabled
              </label>
              <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm font-semibold text-admin-fg-strong">
                <input type="checkbox" checked={editForm.randomiseQuestions} onChange={(e) => updateEditField('randomiseQuestions', e.target.checked)} className="h-4 w-4 rounded border-border" />
                Randomise questions
              </label>
            </div>
            <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
              <Button type="button" variant="ghost" onClick={closeEdit}>Cancel</Button>
              <Button type="submit" variant="primary" loading={savingEdit} disabled={savingEdit}>Save bundle</Button>
            </div>
          </form>
        ) : null}
      </Modal>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}

function LeakReportsLink() {
  const [openCount, setOpenCount] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    listAdminMockLeakReports({ status: 'open', limit: 50 })
      .then((response) => {
        if (!cancelled) setOpenCount(response.items?.length ?? 0);
      })
      .catch(() => {
        if (!cancelled) setOpenCount(null);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <Link
      href="/admin/content/mocks/leak-reports"
      className="inline-flex items-center rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-2 text-sm font-bold text-admin-fg-strong hover:bg-admin-bg-subtle"
    >
      <ShieldAlert className="mr-2 h-4 w-4" /> Leak reports
      {openCount !== null && openCount > 0 ? (
        <span className="ml-2 rounded-full bg-red-100 px-2 py-0.5 text-xs font-bold text-red-700">
          {openCount}
        </span>
      ) : null}
    </Link>
  );
}
