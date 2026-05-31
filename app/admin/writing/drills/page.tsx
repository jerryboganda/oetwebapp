'use client';

import { useCallback, useEffect, useState } from 'react';
import { ClipboardList, Plus, RefreshCcw, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { WritingDrillDto } from '@/lib/writing/types';

type DrillStatus = WritingDrillDto['status'];

interface DrillForm {
  id?: string;
  originalStatus?: DrillStatus;
  drillType: string;
  inputVariant: string;
  targetSubSkill: string;
  targetCanonRuleId: string;
  appliesToProfessions: string;
  appliesToLetterTypes: string;
  difficulty: number;
  promptMarkdown: string;
  expectedAnswer: string;
  alternatives: string;
  options: string;
  gradingMethod: string;
  status: DrillStatus;
}

const EMPTY_FORM: DrillForm = {
  drillType: 'opening-builder',
  inputVariant: 'open',
  targetSubSkill: 'W2',
  targetCanonRuleId: '',
  appliesToProfessions: 'medicine, pharmacy, nursing',
  appliesToLetterTypes: 'LT-RR',
  difficulty: 3,
  promptMarkdown: '',
  expectedAnswer: '',
  alternatives: '',
  options: '',
  gradingMethod: 'exact',
  status: 'draft',
};

const statusTone = (status: DrillStatus) => status === 'published' ? 'success' : status === 'archived' ? 'muted' : 'warning';
const splitComma = (value: string) => value.split(',').map((item) => item.trim()).filter(Boolean);
const splitLines = (value: string) => value.split('\n').map((item) => item.trim()).filter(Boolean);

export default function AdminWritingDrillsPage() {
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
  const [items, setItems] = useState<WritingDrillDto[]>([]);
  const [editing, setEditing] = useState<DrillForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiClient.get<{ items: WritingDrillDto[]; total: number }>('/v1/admin/writing/drills?pageSize=100');
      setItems(response.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load drills.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!editing) return;
    if (!canWriteContent) {
      setError('Content write permission is required to save drills.');
      return;
    }
    if ((editing.originalStatus === 'published' || editing.status === 'published') && !canPublishContent) {
      setError('Content publish permission is required to modify published drills.');
      return;
    }
    setBusy('save');
    try {
      const payload = {
        drillType: editing.drillType,
        inputVariant: editing.inputVariant,
        targetSubSkill: editing.targetSubSkill,
        targetCanonRuleId: editing.targetCanonRuleId.trim() || null,
        appliesToProfessions: splitComma(editing.appliesToProfessions),
        appliesToLetterTypes: splitComma(editing.appliesToLetterTypes),
        difficulty: editing.difficulty,
        promptMarkdown: editing.promptMarkdown,
        expectedAnswer: editing.expectedAnswer.trim() || null,
        alternatives: splitLines(editing.alternatives),
        options: splitLines(editing.options),
        gradingMethod: editing.gradingMethod,
        status: editing.status,
      };
      if (editing.id) {
        await apiClient.put(`/v1/admin/writing/drills/${encodeURIComponent(editing.id)}`, payload);
      } else {
        await apiClient.post('/v1/admin/writing/drills', payload);
      }
      setEditing(null);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setBusy(null);
    }
  };

  const remove = async (id: string) => {
    if (!window.confirm('Delete this drill?')) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/drills/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const edit = (drill: WritingDrillDto) => setEditing({
    id: drill.id,
    originalStatus: drill.status,
    drillType: drill.drillType,
    inputVariant: drill.inputVariant,
    targetSubSkill: drill.targetSubSkill,
    targetCanonRuleId: drill.targetCanonRuleId ?? '',
    appliesToProfessions: drill.appliesToProfessions.join(', '),
    appliesToLetterTypes: drill.appliesToLetterTypes.join(', '),
    difficulty: drill.difficulty,
    promptMarkdown: drill.promptMarkdown,
    expectedAnswer: drill.expectedAnswer ?? '',
    alternatives: (drill.alternatives ?? []).join('\n'),
    options: (drill.options ?? []).join('\n'),
    gradingMethod: drill.gradingMethod,
    status: drill.status,
  });

  const publishLocked = (editing?.originalStatus === 'published' || editing?.status === 'published') && !canPublishContent;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><ClipboardList className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Drills</h1>
          <p className="mt-1 text-sm text-muted">Manage sentence-level drill items for W1-W8 practice and canon reinforcement.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          {canWriteContent ? <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New drill</Button> : null}
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Writing drills">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Type</th>
                <th className="text-left">Skill</th>
                <th className="text-left">Input</th>
                <th className="text-left">Difficulty</th>
                <th className="text-left">Status</th>
                <th className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? <tr><td colSpan={6} className="py-4 text-center text-xs text-muted">No drills yet.</td></tr> : null}
              {items.map((drill) => (
                <tr key={drill.id} className="border-b border-border/60">
                  <td className="py-2 font-bold text-navy">{drill.drillType}</td>
                  <td>{drill.targetSubSkill}</td>
                  <td>{drill.inputVariant}</td>
                  <td>{drill.difficulty}</td>
                  <td><Badge variant={statusTone(drill.status)} size="sm">{drill.status}</Badge></td>
                  <td className="text-right">
                    {canWriteContent && (drill.status !== 'published' || canPublishContent) ? (
                      <>
                        <Button size="sm" variant="outline" onClick={() => edit(drill)}>Edit</Button>
                        <Button size="sm" variant="ghost" onClick={() => void remove(drill.id)} loading={busy === `del-${drill.id}`} aria-label="Delete drill"><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
                      </>
                    ) : <span className="text-xs text-muted">View only</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      {editing ? (
        <aside role="dialog" aria-modal="true" aria-label="Drill editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-black/40">
          <div className="flex h-full w-full max-w-2xl flex-col gap-3 overflow-y-auto bg-surface p-5 shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.id ? 'Edit drill' : 'New drill'}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Drill type<input value={editing.drillType} onChange={(event) => setEditing({ ...editing, drillType: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Input variant<select value={editing.inputVariant} onChange={(event) => setEditing({ ...editing, inputVariant: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm"><option value="mcq">MCQ</option><option value="fill">Fill</option><option value="open">Open</option><option value="drag-drop">Drag/drop</option></select></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Target skill<select value={editing.targetSubSkill} onChange={(event) => setEditing({ ...editing, targetSubSkill: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">{['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'].map((skill) => <option key={skill} value={skill}>{skill}</option>)}</select></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Canon rule<input value={editing.targetCanonRuleId} onChange={(event) => setEditing({ ...editing, targetCanonRuleId: event.target.value })} placeholder="SC-012" className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Difficulty<input type="number" min={1} max={5} value={editing.difficulty} onChange={(event) => setEditing({ ...editing, difficulty: Number(event.target.value) })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Grading method<select value={editing.gradingMethod} onChange={(event) => setEditing({ ...editing, gradingMethod: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm"><option value="exact">Exact</option><option value="regex">Regex</option><option value="llm">LLM</option><option value="multiple-choice">Multiple choice</option></select></label>
            </div>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Professions<input value={editing.appliesToProfessions} onChange={(event) => setEditing({ ...editing, appliesToProfessions: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Letter types<input value={editing.appliesToLetterTypes} onChange={(event) => setEditing({ ...editing, appliesToLetterTypes: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Prompt<textarea rows={7} value={editing.promptMarkdown} onChange={(event) => setEditing({ ...editing, promptMarkdown: event.target.value })} className="rounded border border-border bg-background p-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Expected answer<input value={editing.expectedAnswer} onChange={(event) => setEditing({ ...editing, expectedAnswer: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Alternatives (one per line)<textarea rows={4} value={editing.alternatives} onChange={(event) => setEditing({ ...editing, alternatives: event.target.value })} className="rounded border border-border bg-background p-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Options (one per line)<textarea rows={4} value={editing.options} onChange={(event) => setEditing({ ...editing, options: event.target.value })} className="rounded border border-border bg-background p-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Status<select value={editing.status} onChange={(event) => setEditing({ ...editing, status: event.target.value as DrillStatus })} className="min-h-9 rounded border border-border bg-background px-2 text-sm"><option value="draft">Draft</option>{canPublishContent || editing.status === 'published' ? <option value="published" disabled={!canPublishContent}>Published</option> : null}<option value="archived">Archived</option></select></label>
            {publishLocked ? <p className="rounded border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900">Content publish permission is required to modify a published drill.</p> : null}

            <footer className="mt-auto flex items-center justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={() => void save()} loading={busy === 'save'} disabled={publishLocked || !canWriteContent}>Save</Button>
            </footer>
          </div>
        </aside>
      ) : null}
    </div>
  );
}