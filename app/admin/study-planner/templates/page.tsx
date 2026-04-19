'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { FileText, Plus, Archive as ArchiveIcon, Settings } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  archivePlanTemplate, createPlanTemplate, getPlanTemplateDetail, listPlanTemplates, listTaskTemplates,
  replacePlanTemplateItems, updatePlanTemplate,
  type PlanTemplateDto, type PlanTemplateItemDto, type TaskTemplateDto,
} from '@/lib/study-planner-admin-api';

const SECTIONS = [
  { value: 'today', label: 'Today' },
  { value: 'thisWeek', label: 'This Week' },
  { value: 'nextCheckpoint', label: 'Next Checkpoint' },
  { value: 'weakSkillFocus', label: 'Weak-Skill Focus' },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface ItemDraft {
  key: string;
  taskTemplateId: string;
  weekOffset: number;
  dayOffsetWithinWeek: number;
  section: string;
  priority: number;
  isMandatory: boolean;
}

export default function StudyPlannerPlanTemplatesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [rows, setRows] = useState<PlanTemplateDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [saving, setSaving] = useState(false);
  const [fSlug, setFSlug] = useState('');
  const [fName, setFName] = useState('');
  const [fDescription, setFDescription] = useState('');
  const [fWeeks, setFWeeks] = useState('8');
  const [fHours, setFHours] = useState('10');

  const [editingItemsFor, setEditingItemsFor] = useState<PlanTemplateDto | null>(null);
  const [itemDrafts, setItemDrafts] = useState<ItemDraft[]>([]);
  const [taskLibrary, setTaskLibrary] = useState<TaskTemplateDto[]>([]);
  const [loadingItems, setLoadingItems] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await listPlanTemplates(false);
      setRows(data);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const onCreate = async () => {
    setSaving(true);
    try {
      await createPlanTemplate({
        slug: fSlug, name: fName, description: fDescription,
        durationWeeks: parseInt(fWeeks, 10) || 8,
        defaultHoursPerWeek: parseInt(fHours, 10) || 10,
      });
      setToast({ variant: 'success', message: 'Plan template created.' });
      setShowCreate(false);
      setFSlug(''); setFName(''); setFDescription(''); setFWeeks('8'); setFHours('10');
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  const openItemEditor = async (plan: PlanTemplateDto) => {
    setEditingItemsFor(plan);
    setLoadingItems(true);
    try {
      const [detail, lib] = await Promise.all([
        getPlanTemplateDetail(plan.id),
        listTaskTemplates({}),
      ]);
      setTaskLibrary(lib);
      setItemDrafts(detail.items.map((it) => ({
        key: it.id,
        taskTemplateId: it.taskTemplateId,
        weekOffset: it.weekOffset,
        dayOffsetWithinWeek: it.dayOffsetWithinWeek,
        section: it.section,
        priority: it.priority,
        isMandatory: it.isMandatory,
      })));
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoadingItems(false);
    }
  };

  const addDraft = () => {
    if (taskLibrary.length === 0) return;
    setItemDrafts((ds) => [...ds, {
      key: `draft-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
      taskTemplateId: taskLibrary[0].id,
      weekOffset: 0,
      dayOffsetWithinWeek: 0,
      section: 'today',
      priority: 50,
      isMandatory: true,
    }]);
  };

  const updateDraft = (key: string, update: Partial<ItemDraft>) => {
    setItemDrafts((ds) => ds.map((d) => d.key === key ? { ...d, ...update } : d));
  };

  const removeDraft = (key: string) => {
    setItemDrafts((ds) => ds.filter((d) => d.key !== key));
  };

  const saveItems = async () => {
    if (!editingItemsFor) return;
    setSaving(true);
    try {
      await replacePlanTemplateItems(editingItemsFor.id, itemDrafts.map((d, idx) => ({
        taskTemplateId: d.taskTemplateId,
        weekOffset: d.weekOffset,
        dayOffsetWithinWeek: d.dayOffsetWithinWeek,
        section: d.section,
        priority: d.priority,
        isMandatory: d.isMandatory,
        prerequisiteItemTemplateId: null,
        ordering: idx,
      })));
      setToast({ variant: 'success', message: 'Items saved.' });
      setEditingItemsFor(null);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  const onArchive = async (id: string) => {
    try {
      await archivePlanTemplate(id);
      setToast({ variant: 'success', message: 'Archived.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  };

  const columns: Column<PlanTemplateDto>[] = useMemo(() => [
    { key: 'name', header: 'Name', render: (r) => <span className={r.isArchived ? 'line-through text-muted' : 'font-medium'}>{r.name}</span> },
    { key: 'slug', header: 'Slug', render: (r) => <span className="font-mono text-xs">{r.slug}</span> },
    { key: 'weeks', header: 'Weeks', render: (r) => <Badge variant="muted">{r.durationWeeks}w</Badge> },
    { key: 'hours', header: 'Hours/wk', render: (r) => `${r.defaultHoursPerWeek}h` },
    { key: 'fam', header: 'Exam', render: (r) => <Badge variant="info">{r.examFamilyCode}</Badge> },
    {
      key: 'act', header: 'Actions', render: (r) => (
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => void openItemEditor(r)}>
            <Settings className="w-4 h-4 mr-1" /> Items
          </Button>
          {!r.isArchived && <Button variant="ghost" size="sm" onClick={() => void onArchive(r.id)}><ArchiveIcon className="w-4 h-4" /></Button>}
        </div>
      ),
    },
  ], []);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<FileText className="w-6 h-6" />}
        title="Plan Templates"
        description="Compose task templates into ordered plans. Assignment rules route each learner to one of these plans."
      />

      <AdminRoutePanel eyebrow="Library" title={`Plan templates (${rows.length})`} dense>
        <div className="mb-3">
          <Button variant="primary" onClick={() => setShowCreate(true)}>
            <Plus className="w-4 h-4 mr-1" /> New plan template
          </Button>
        </div>
        <AsyncStateWrapper status={status}>
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AsyncStateWrapper>
      </AdminRoutePanel>

      <Modal open={showCreate} onClose={() => setShowCreate(false)} title="Create plan template">
        <div className="grid grid-cols-2 gap-3">
          <Input label="Slug" value={fSlug} onChange={(e) => setFSlug(e.target.value)} placeholder="std-8w-medicine" />
          <Input label="Name" value={fName} onChange={(e) => setFName(e.target.value)} placeholder="Standard 8-Week (Medicine)" />
          <Input label="Duration (weeks)" type="number" value={fWeeks} onChange={(e) => setFWeeks(e.target.value)} />
          <Input label="Default hours/week" type="number" value={fHours} onChange={(e) => setFHours(e.target.value)} />
          <div className="col-span-2">
            <label className="text-sm font-semibold block mb-1">Description</label>
            <textarea className="w-full min-h-[80px] rounded-md border border-gray-300 p-2 text-sm" value={fDescription} onChange={(e) => setFDescription(e.target.value)} />
          </div>
        </div>
        <div className="flex gap-3 mt-4 justify-end">
          <Button variant="ghost" onClick={() => setShowCreate(false)}>Cancel</Button>
          <Button variant="primary" onClick={() => void onCreate()} loading={saving} disabled={!fSlug || !fName}>Create</Button>
        </div>
      </Modal>

      <Modal open={!!editingItemsFor} onClose={() => setEditingItemsFor(null)} title={`Items: ${editingItemsFor?.name ?? ''}`}>
        {loadingItems ? <p className="text-sm text-muted">Loading…</p> : (
          <div className="space-y-3">
            {itemDrafts.length === 0 && <p className="text-sm text-muted">No items yet.</p>}
            {itemDrafts.map((d, idx) => (
              <div key={d.key} className="p-3 border border-gray-200 rounded-lg grid grid-cols-6 gap-2 items-end">
                <div className="col-span-2">
                  <Select
                    label={`#${idx + 1} Task`}
                    value={d.taskTemplateId}
                    onChange={(e) => updateDraft(d.key, { taskTemplateId: e.target.value })}
                    options={taskLibrary.map((t) => ({ value: t.id, label: `${t.subtestCode}: ${t.title}` }))}
                  />
                </div>
                <Input label="Week" type="number" value={String(d.weekOffset)} onChange={(e) => updateDraft(d.key, { weekOffset: parseInt(e.target.value, 10) || 0 })} />
                <Input label="Day" type="number" value={String(d.dayOffsetWithinWeek)} onChange={(e) => updateDraft(d.key, { dayOffsetWithinWeek: parseInt(e.target.value, 10) || 0 })} />
                <Select label="Section" value={d.section} onChange={(e) => updateDraft(d.key, { section: e.target.value })} options={SECTIONS} />
                <div className="flex gap-1">
                  <Button variant="ghost" size="sm" onClick={() => removeDraft(d.key)}>Remove</Button>
                </div>
              </div>
            ))}
            <Button variant="ghost" onClick={addDraft}><Plus className="w-4 h-4 mr-1" /> Add item</Button>
          </div>
        )}
        <div className="flex gap-3 mt-4 justify-end">
          <Button variant="ghost" onClick={() => setEditingItemsFor(null)}>Cancel</Button>
          <Button variant="primary" onClick={() => void saveItems()} loading={saving}>Save items</Button>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
