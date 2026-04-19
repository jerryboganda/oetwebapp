'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ClipboardList, Plus, Archive as ArchiveIcon } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Switch } from '@/components/ui/switch';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  archiveTaskTemplate, createTaskTemplate, listTaskTemplates, updateTaskTemplate,
  type TaskTemplateDto,
} from '@/lib/study-planner-admin-api';

const SUBTESTS = [
  { value: '', label: 'All subtests' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
  { value: 'mock', label: 'Mock' },
  { value: 'diagnostic', label: 'Diagnostic' },
];

const ITEM_TYPES = [
  { value: 'practice', label: 'Practice' },
  { value: 'roleplay', label: 'Role Play' },
  { value: 'drill', label: 'Drill' },
  { value: 'mock', label: 'Mock' },
  { value: 'diagnostic', label: 'Diagnostic' },
  { value: 'lesson', label: 'Lesson' },
  { value: 'review', label: 'Review' },
];

const SECTIONS = [
  { value: 'today', label: 'Today' },
  { value: 'thisWeek', label: 'This Week' },
  { value: 'nextCheckpoint', label: 'Next Checkpoint' },
  { value: 'weakSkillFocus', label: 'Weak-Skill Focus' },
];

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function StudyPlannerTaskTemplatesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<TaskTemplateDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [filterSubtest, setFilterSubtest] = useState('');
  const [search, setSearch] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);

  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<TaskTemplateDto | null>(null);
  const [saving, setSaving] = useState(false);

  const [fSlug, setFSlug] = useState('');
  const [fTitle, setFTitle] = useState('');
  const [fSubtest, setFSubtest] = useState('writing');
  const [fItemType, setFItemType] = useState('practice');
  const [fDuration, setFDuration] = useState('30');
  const [fRationale, setFRationale] = useState('');
  const [fSection, setFSection] = useState('thisWeek');
  const [fContentPaperId, setFContentPaperId] = useState('');
  const [fTagsCsv, setFTagsCsv] = useState('');
  const [fProfScope, setFProfScope] = useState('');
  const [fCountries, setFCountries] = useState('');

  const resetForm = () => {
    setEditing(null);
    setFSlug(''); setFTitle(''); setFSubtest('writing'); setFItemType('practice');
    setFDuration('30'); setFRationale(''); setFSection('thisWeek');
    setFContentPaperId(''); setFTagsCsv(''); setFProfScope(''); setFCountries('');
  };

  const startEdit = (row: TaskTemplateDto) => {
    setEditing(row);
    setFSlug(row.slug);
    setFTitle(row.title);
    setFSubtest(row.subtestCode);
    setFItemType(row.itemType);
    setFDuration(String(row.durationMinutes));
    setFRationale(row.rationaleMarkdown);
    setFSection(row.defaultSection);
    setFContentPaperId(row.defaultContentPaperId ?? '');
    setFTagsCsv(row.tagsCsv);
    try {
      const prof = JSON.parse(row.professionScopeJson || '[]') as string[];
      setFProfScope(prof.join(','));
    } catch { setFProfScope(''); }
    try {
      const c = JSON.parse(row.targetCountriesJson || '[]') as string[];
      setFCountries(c.join(','));
    } catch { setFCountries(''); }
    setShowForm(true);
  };

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await listTaskTemplates({
        subtest: filterSubtest || undefined,
        search: search || undefined,
        includeArchived,
      });
      setRows(data);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, [filterSubtest, search, includeArchived]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const onSubmit = async () => {
    setSaving(true);
    try {
      const payload = {
        slug: fSlug,
        title: fTitle,
        subtestCode: fSubtest,
        itemType: fItemType,
        durationMinutes: parseInt(fDuration, 10) || 30,
        rationaleMarkdown: fRationale,
        defaultSection: fSection,
        defaultContentPaperId: fContentPaperId || null,
        tagsCsv: fTagsCsv,
        professionScope: fProfScope.split(',').map((s) => s.trim()).filter(Boolean),
        targetCountries: fCountries.split(',').map((s) => s.trim()).filter(Boolean),
      };
      if (editing) {
        await updateTaskTemplate(editing.id, payload);
        setToast({ variant: 'success', message: 'Task template updated.' });
      } else {
        await createTaskTemplate(payload);
        setToast({ variant: 'success', message: 'Task template created.' });
      }
      setShowForm(false);
      resetForm();
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  const onArchive = useCallback(async (id: string) => {
    try {
      await archiveTaskTemplate(id);
      setToast({ variant: 'success', message: 'Archived.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, [load]);

  const columns: Column<TaskTemplateDto>[] = useMemo(() => [
    { key: 'subtest', header: 'Subtest', render: (r) => <Badge variant="info">{r.subtestCode}</Badge> },
    { key: 'title', header: 'Title', render: (r) => <span className={r.isArchived ? 'line-through text-muted' : 'font-medium'}>{r.title}</span> },
    { key: 'slug', header: 'Slug', render: (r) => <span className="font-mono text-xs">{r.slug}</span> },
    { key: 'type', header: 'Type', render: (r) => r.itemType },
    { key: 'dur', header: 'Duration', render: (r) => `${r.durationMinutes} min` },
    { key: 'sec', header: 'Default Section', render: (r) => <Badge variant="muted">{r.defaultSection}</Badge> },
    {
      key: 'act', header: 'Actions', render: (r) => (
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => startEdit(r)}>Edit</Button>
          {!r.isArchived && <Button variant="ghost" size="sm" onClick={() => void onArchive(r.id)}><ArchiveIcon className="w-4 h-4" /></Button>}
        </div>
      ),
    },
  ], [onArchive]);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<ClipboardList className="w-6 h-6" />}
        title="Task Templates"
        description="The library of reusable tasks that plans are composed from. Each has a title, rationale (shown to learners as 'Why this is recommended'), duration, and optional deep-link to an authored Content Paper."
      />

      <AdminRoutePanel eyebrow="Filters" title="Search task library" dense>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-3 items-end">
            <Select label="Subtest" value={filterSubtest} onChange={(e) => setFilterSubtest(e.target.value)} options={SUBTESTS} />
            <Input label="Search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Title or slug" />
            <Switch
              checked={includeArchived}
              onCheckedChange={setIncludeArchived}
              label="Include archived"
            />
            <Button variant="primary" onClick={() => { resetForm(); setShowForm(true); }}>
              <Plus className="w-4 h-4 mr-1" /> New task template
            </Button>
          </div>
      </AdminRoutePanel>

      <AsyncStateWrapper status={status}>
        <AdminRoutePanel eyebrow="Library" title={`Task templates (${rows.length})`} dense>
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={showForm} onClose={() => setShowForm(false)} title={editing ? 'Edit task template' : 'New task template'}>
        <div className="grid grid-cols-2 gap-3">
          <Input label="Slug" value={fSlug} onChange={(e) => setFSlug(e.target.value)} disabled={!!editing} placeholder="e.g. writing-referral-1" />
          <Input label="Title" value={fTitle} onChange={(e) => setFTitle(e.target.value)} placeholder="e.g. Writing: Referral Letter" />
          <Select label="Subtest" value={fSubtest} onChange={(e) => setFSubtest(e.target.value)} options={SUBTESTS.filter(s => s.value)} />
          <Select label="Item type" value={fItemType} onChange={(e) => setFItemType(e.target.value)} options={ITEM_TYPES} />
          <Input label="Duration (min)" type="number" value={fDuration} onChange={(e) => setFDuration(e.target.value)} />
          <Select label="Default section" value={fSection} onChange={(e) => setFSection(e.target.value)} options={SECTIONS} />
          <Input label="Default Content Paper ID (optional)" value={fContentPaperId} onChange={(e) => setFContentPaperId(e.target.value)} placeholder="paper-abc123" />
          <Input label="Tags (CSV)" value={fTagsCsv} onChange={(e) => setFTagsCsv(e.target.value)} placeholder="referral,medicine" />
          <Input label="Profession scope (CSV, empty = all)" value={fProfScope} onChange={(e) => setFProfScope(e.target.value)} placeholder="medicine,nursing" />
          <Input label="Target countries (CSV, empty = all)" value={fCountries} onChange={(e) => setFCountries(e.target.value)} placeholder="UK,IE,AU" />
          <div className="col-span-2">
            <Textarea
              label="Rationale (Markdown — shown as &ldquo;Why this is recommended&rdquo;)"
              rows={4}
              value={fRationale}
              onChange={(e) => setFRationale(e.target.value)}
              placeholder="Practice condensing patient notes into a concise referral letter. Focuses on purpose, context, and word economy."
            />
          </div>
        </div>
        <div className="flex gap-3 mt-4 justify-end">
          <Button variant="ghost" onClick={() => setShowForm(false)}>Cancel</Button>
          <Button variant="primary" onClick={() => void onSubmit()} loading={saving} disabled={!fSlug.trim() || !fTitle.trim()}>
            {editing ? 'Save' : 'Create'}
          </Button>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
