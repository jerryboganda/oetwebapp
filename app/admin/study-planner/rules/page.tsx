'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Scale, Plus, Play } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  createAssignmentRule, deleteAssignmentRule, listAssignmentRules, listPlanTemplates, previewRuleMatch, updateAssignmentRule,
  type AssignmentRuleDto, type PlanTemplateDto,
} from '@/lib/study-planner-admin-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function StudyPlannerRulesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [rows, setRows] = useState<AssignmentRuleDto[]>([]);
  const [templates, setTemplates] = useState<PlanTemplateDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<AssignmentRuleDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [fName, setFName] = useState('');
  const [fPriority, setFPriority] = useState('100');
  const [fWeight, setFWeight] = useState('50');
  const [fCondition, setFCondition] = useState('{}');
  const [fTarget, setFTarget] = useState('');

  const [showPreview, setShowPreview] = useState(false);
  const [pvProf, setPvProf] = useState('medicine');
  const [pvCountry, setPvCountry] = useState('UK');
  const [pvWeeks, setPvWeeks] = useState('8');
  const [pvHours, setPvHours] = useState('10');
  const [pvWeak, setPvWeak] = useState('writing');
  const [pvResult, setPvResult] = useState<{ templateId: string | null; matchedRuleIds: string[]; reason: string } | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const [rules, tpls] = await Promise.all([
        listAssignmentRules(true),
        listPlanTemplates(false),
      ]);
      setRows(rules);
      setTemplates(tpls);
      if (tpls[0]) setFTarget((prev) => prev || tpls[0].id);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const startNew = () => {
    setEditing(null);
    setFName(''); setFPriority('100'); setFWeight('50'); setFCondition('{}');
    setFTarget(templates[0]?.id ?? '');
    setShowForm(true);
  };

  const startEdit = (r: AssignmentRuleDto) => {
    setEditing(r);
    setFName(r.name); setFPriority(String(r.priority)); setFWeight(String(r.weight));
    setFCondition(r.conditionJson || '{}');
    setFTarget(r.targetTemplateId);
    setShowForm(true);
  };

  const onSubmit = async () => {
    setSaving(true);
    try {
      if (editing) {
        await updateAssignmentRule(editing.id, {
          name: fName, priority: parseInt(fPriority, 10), weight: parseInt(fWeight, 10),
          conditionJson: fCondition, targetTemplateId: fTarget,
        });
      } else {
        await createAssignmentRule({
          name: fName, priority: parseInt(fPriority, 10), weight: parseInt(fWeight, 10),
          conditionJson: fCondition, targetTemplateId: fTarget, isActive: true,
        });
      }
      setToast({ variant: 'success', message: 'Saved.' });
      setShowForm(false);
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  const onDelete = useCallback(async (id: string) => {
    try {
      await deleteAssignmentRule(id);
      setToast({ variant: 'success', message: 'Deleted.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  }, [load]);

  const onPreview = async () => {
    try {
      const result = await previewRuleMatch({
        userId: 'preview',
        professionId: pvProf || undefined,
        examFamilyCode: 'oet',
        targetCountry: pvCountry || undefined,
        weeksToExam: parseInt(pvWeeks, 10) || undefined,
        hoursPerWeek: parseInt(pvHours, 10) || undefined,
        weakSubtests: pvWeak.split(',').map((s) => s.trim()).filter(Boolean),
      });
      setPvResult(result);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    }
  };

  const columns: Column<AssignmentRuleDto>[] = useMemo(() => [
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium">{r.name}</span> },
    { key: 'prio', header: 'Priority', render: (r) => r.priority },
    { key: 'w', header: 'Weight', render: (r) => r.weight },
    { key: 'tpl', header: 'Target', render: (r) => <span className="font-mono text-xs">{r.targetTemplateId.slice(0, 20)}</span> },
    { key: 'act', header: 'Active', render: (r) => <Badge variant={r.isActive ? 'success' : 'muted'}>{r.isActive ? 'Active' : 'Inactive'}</Badge> },
    {
      key: 'a', header: 'Actions', render: (r) => (
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => startEdit(r)}>Edit</Button>
          <Button variant="ghost" size="sm" onClick={() => void onDelete(r.id)}>Delete</Button>
        </div>
      ),
    },
  ], [onDelete]);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  const templateName = (id: string) => templates.find((t) => t.id === id)?.name ?? id;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<Scale className="w-6 h-6" />}
        title="Assignment Rules"
        description="Define how learner profiles (profession, country, target, weeks-to-exam, weak subtests) route to plan templates. Higher weight wins; priority breaks ties (lower first)."
      />

      <AdminRoutePanel eyebrow="Preview" title="Dry-run against a sample profile" dense>
        <div className="grid grid-cols-6 gap-3 items-end">
          <Input label="Profession" value={pvProf} onChange={(e) => setPvProf(e.target.value)} />
          <Input label="Country" value={pvCountry} onChange={(e) => setPvCountry(e.target.value)} />
          <Input label="Weeks to exam" type="number" value={pvWeeks} onChange={(e) => setPvWeeks(e.target.value)} />
          <Input label="Hours/week" type="number" value={pvHours} onChange={(e) => setPvHours(e.target.value)} />
          <Input label="Weak (CSV)" value={pvWeak} onChange={(e) => setPvWeak(e.target.value)} />
          <Button variant="primary" onClick={() => void onPreview()}><Play className="w-4 h-4 mr-1" /> Run</Button>
        </div>
        {pvResult && (
          <div className="mt-4 rounded-lg border border-blue-200 bg-blue-50 p-3 text-sm dark:border-blue-800 dark:bg-blue-950/30">
            <p><strong>Match:</strong> {pvResult.templateId ? templateName(pvResult.templateId) : 'No rule matched (fallback used)'}</p>
            <p className="text-xs mt-1 text-muted">Rules matched: {pvResult.matchedRuleIds.join(', ') || 'none'}</p>
          </div>
        )}
      </AdminRoutePanel>

      <AsyncStateWrapper status={status}>
        <AdminRoutePanel eyebrow="Library" title={`Rules (${rows.length})`} dense>
          <div className="mb-3">
            <Button variant="primary" onClick={startNew}><Plus className="w-4 h-4 mr-1" /> New rule</Button>
          </div>
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={showForm} onClose={() => setShowForm(false)} title={editing ? 'Edit rule' : 'New rule'}>
        <div className="grid grid-cols-2 gap-3">
          <Input label="Name" value={fName} onChange={(e) => setFName(e.target.value)} placeholder="e.g. Medicine UK" />
          <Select label="Target template" value={fTarget} onChange={(e) => setFTarget(e.target.value)} options={templates.map((t) => ({ value: t.id, label: t.name }))} />
          <Input label="Priority (lower = higher prio)" type="number" value={fPriority} onChange={(e) => setFPriority(e.target.value)} />
          <Input label="Weight (higher = stronger match)" type="number" value={fWeight} onChange={(e) => setFWeight(e.target.value)} />
          <div className="col-span-2">
            <Textarea
              label="Condition (JSON)"
              rows={5}
              value={fCondition}
              onChange={(e) => setFCondition(e.target.value)}
              placeholder='{"professions":["medicine"],"countries":["UK","IE"],"maxWeeksToExam":8,"weakSubtests":["writing"]}'
              className="font-mono text-xs"
            />
          </div>
        </div>
        <div className="flex gap-3 mt-4 justify-end">
          <Button variant="ghost" onClick={() => setShowForm(false)}>Cancel</Button>
          <Button variant="primary" onClick={() => void onSubmit()} loading={saving} disabled={!fName || !fTarget}>Save</Button>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
