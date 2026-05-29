'use client';

import { useCallback, useEffect, useState } from 'react';
import { Award, Plus, RefreshCcw, TestTube, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import type { WritingExemplarDto, WritingExemplarAnnotationDto } from '@/lib/writing/types';

interface AnnotationDraft {
  charStart: number;
  charEnd: number;
  ruleId: string;
  note: string;
}

interface ExemplarUpsertForm {
  id?: string;
  scenarioId: string;
  profession: string;
  letterType: string;
  difficulty: number;
  targetBand: string;
  letterContent: string;
  annotations: AnnotationDraft[];
  authorNote: string;
  status: 'draft' | 'published' | 'archived';
}

const EMPTY_FORM: ExemplarUpsertForm = {
  scenarioId: '',
  profession: 'medicine',
  letterType: 'LT-RR',
  difficulty: 3,
  targetBand: 'A',
  letterContent: '',
  annotations: [],
  authorNote: '',
  status: 'draft',
};

export default function AdminWritingExemplarsPage() {
  const [items, setItems] = useState<WritingExemplarDto[]>([]);
  const [editing, setEditing] = useState<ExemplarUpsertForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const r = await apiClient.get<{ items: WritingExemplarDto[]; total: number }>('/v1/admin/writing/exemplars?pageSize=100');
      setItems(r.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load exemplars.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!editing) return;
    setBusy('save');
    try {
      const payload = {
        scenarioId: editing.scenarioId || null,
        profession: editing.profession,
        letterType: editing.letterType,
        difficulty: editing.difficulty,
        targetBand: editing.targetBand,
        letterContent: editing.letterContent,
        annotations: editing.annotations.map((a) => ({
          charStart: a.charStart,
          charEnd: a.charEnd,
          ruleId: a.ruleId || null,
          note: a.note,
        })),
        authorNote: editing.authorNote || null,
        status: editing.status,
      };
      if (editing.id) {
        await apiClient.put(`/v1/admin/writing/exemplars/${encodeURIComponent(editing.id)}`, payload);
      } else {
        await apiClient.post('/v1/admin/writing/exemplars', payload);
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
    if (!window.confirm('Delete this exemplar?')) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/exemplars/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const testGrade = async (id: string) => {
    setBusy(`test-${id}`);
    setTestResult(null);
    try {
      const r = await apiClient.post<{ exemplarId: string; passesQualityBar: boolean; grade: { bandLabel: string; rawTotal: number } }>(
        `/v1/admin/writing/exemplars/${encodeURIComponent(id)}/test-grade`,
        {},
      );
      setTestResult(`${r.exemplarId}: ${r.passesQualityBar ? 'PASS' : 'FAIL'} (band ${r.grade.bandLabel}, raw ${r.grade.rawTotal}/38)`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Test grade failed.');
    } finally {
      setBusy(null);
    }
  };

  const annotateSelection = () => {
    if (!editing) return;
    const textarea = document.getElementById('exemplar-letter') as HTMLTextAreaElement | null;
    if (!textarea) return;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    if (start === end) {
      setError('Select text in the letter to annotate first.');
      return;
    }
    const ruleId = window.prompt('Cite a canon rule id (e.g. SC-012, or leave blank):', '') ?? '';
    const note = window.prompt('Annotation note:', '') ?? '';
    if (!note) return;
    setEditing({
      ...editing,
      annotations: [...editing.annotations, { charStart: start, charEnd: end, ruleId, note }],
    });
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><Award className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Exemplars</h1>
          <p className="mt-1 text-sm text-muted">Gold-standard letters used for side-by-side comparison and similarity matching.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New exemplar</Button>
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}
      {testResult ? <p role="status" className="rounded border border-emerald-300 bg-emerald-50 p-3 text-sm text-emerald-800">{testResult}</p> : null}

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Exemplar list">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th scope="col" className="py-2 text-left">Scenario</th>
                <th scope="col" className="text-left">Profession</th>
                <th scope="col" className="text-left">Letter</th>
                <th scope="col" className="text-left">Band</th>
                <th scope="col" className="text-left">Status</th>
                <th scope="col" className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? <tr><td colSpan={6} className="py-4 text-center text-xs text-muted">No exemplars yet.</td></tr> : null}
              {items.map((ex) => (
                <tr key={ex.id} className="border-b border-border/60">
                  <td className="py-2 text-xs">{ex.scenarioId ?? '—'}</td>
                  <td className="capitalize">{ex.profession}</td>
                  <td>{ex.letterType}</td>
                  <td><Badge variant="success" size="sm">{ex.targetBand}</Badge></td>
                  <td><Badge variant={ex.status === 'published' ? 'success' : ex.status === 'archived' ? 'muted' : 'warning'} size="sm">{ex.status}</Badge></td>
                  <td className="text-right">
                    <Button size="sm" variant="ghost" onClick={() => void testGrade(ex.id)} loading={busy === `test-${ex.id}`} aria-label="Test grade"><TestTube className="h-4 w-4" aria-hidden="true" /></Button>
                    <Button size="sm" variant="outline" onClick={() => setEditing({ id: ex.id, scenarioId: ex.scenarioId ?? '', profession: ex.profession, letterType: ex.letterType, difficulty: ex.difficulty, targetBand: ex.targetBand, letterContent: ex.letterContent, annotations: ex.annotations.map((a) => ({ charStart: a.charStart, charEnd: a.charEnd, ruleId: a.ruleId ?? '', note: a.note })), authorNote: ex.authorNote ?? '', status: ex.status })}>Edit</Button>
                    <Button size="sm" variant="ghost" onClick={() => void remove(ex.id)} loading={busy === `del-${ex.id}`} aria-label="Delete"><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      {editing ? (
        <aside role="dialog" aria-modal="true" aria-label="Exemplar editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-navy/40">
          <div className="flex h-full w-full max-w-3xl flex-col gap-3 overflow-y-auto bg-surface p-5 shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.id ? 'Edit exemplar' : 'New exemplar'}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Scenario id (optional)
                <input type="text" value={editing.scenarioId} onChange={(e) => setEditing({ ...editing, scenarioId: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Target band
                <select value={editing.targetBand} onChange={(e) => setEditing({ ...editing, targetBand: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  {['A', 'B+', 'B'].map((b) => <option key={b} value={b}>{b}</option>)}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Letter type
                <select value={editing.letterType} onChange={(e) => setEditing({ ...editing, letterType: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  {['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-RP', 'LT-NM'].map((lt) => <option key={lt} value={lt}>{lt}</option>)}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Profession
                <select value={editing.profession} onChange={(e) => setEditing({ ...editing, profession: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  {['medicine', 'pharmacy', 'nursing', 'other'].map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Difficulty
                <input type="number" min={1} max={5} value={editing.difficulty} onChange={(e) => setEditing({ ...editing, difficulty: Number(e.target.value) })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Status
                <select value={editing.status} onChange={(e) => setEditing({ ...editing, status: e.target.value as 'draft' | 'published' | 'archived' })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  <option value="draft">Draft</option>
                  <option value="published">Published</option>
                  <option value="archived">Archived</option>
                </select>
              </label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Author note (optional)
              <input type="text" value={editing.authorNote} onChange={(e) => setEditing({ ...editing, authorNote: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
            </label>

            <div>
              <div className="flex items-center justify-between gap-2">
                <label htmlFor="exemplar-letter" className="text-xs font-bold uppercase tracking-wider text-muted">
                  Letter content
                </label>
                <Button type="button" variant="outline" size="sm" onClick={annotateSelection}>Annotate selection</Button>
              </div>
              <textarea id="exemplar-letter" rows={14} value={editing.letterContent} onChange={(e) => setEditing({ ...editing, letterContent: e.target.value })} className="mt-1 w-full rounded border border-border bg-background p-2 text-sm font-mono" />
            </div>

            {editing.annotations.length > 0 ? (
              <ol className="space-y-1 text-xs">
                <li className="font-bold uppercase tracking-wider text-muted">Annotations</li>
                {editing.annotations.map((a, idx) => (
                  <li key={idx} className="flex items-center justify-between gap-2 rounded border border-border bg-background p-2">
                    <span>[{a.charStart}-{a.charEnd}] {a.ruleId ? `${a.ruleId}: ` : ''}{a.note}</span>
                    <Button variant="ghost" size="sm" aria-label={`Remove annotation ${idx + 1}`} onClick={() => setEditing({ ...editing, annotations: editing.annotations.filter((_, i) => i !== idx) })}><Trash2 className="h-3 w-3" aria-hidden="true" /></Button>
                  </li>
                ))}
              </ol>
            ) : null}

            <footer className="mt-auto flex items-center justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={() => void save()} loading={busy === 'save'}>Save</Button>
            </footer>
          </div>
        </aside>
      ) : null}
    </div>
  );
}
