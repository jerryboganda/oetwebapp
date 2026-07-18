'use client';

import { useCallback, useEffect, useState } from 'react';
import { FileText, Plus, RefreshCcw, Sparkles, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import type { WritingScenarioDto } from '@/lib/writing/types';

interface ScenarioUpsertForm {
  id?: string;
  title: string;
  letterType: string;
  profession: string;
  subDiscipline: string;
  topics: string;
  difficulty: number;
  caseNotesMarkdown: string;
  isDiagnostic: boolean;
  status: 'draft' | 'published' | 'archived';
}

const EMPTY_FORM: ScenarioUpsertForm = {
  title: '',
  letterType: 'LT-RR',
  profession: 'medicine',
  subDiscipline: '',
  topics: '',
  difficulty: 3,
  caseNotesMarkdown: '',
  isDiagnostic: false,
  status: 'draft',
};

export default function AdminWritingScenariosPage() {
  const [items, setItems] = useState<WritingScenarioDto[]>([]);
  const [editing, setEditing] = useState<ScenarioUpsertForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [generatorBriefing, setGeneratorBriefing] = useState('');

  const load = useCallback(async () => {
    try {
      const r = await apiClient.get<{ items: WritingScenarioDto[]; total: number }>('/v1/admin/writing/scenarios?pageSize=100');
      setItems(r.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load scenarios.');
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
        title: editing.title.trim(),
        letterType: editing.letterType,
        profession: editing.profession,
        subDiscipline: editing.subDiscipline.trim() || null,
        topics: editing.topics.split(',').map((t) => t.trim()).filter(Boolean),
        difficulty: editing.difficulty,
        caseNotesMarkdown: editing.caseNotesMarkdown,
        isDiagnostic: editing.isDiagnostic,
        status: editing.status,
      };
      if (editing.id) {
        await apiClient.put(`/v1/admin/writing/scenarios/${encodeURIComponent(editing.id)}`, payload);
      } else {
        await apiClient.post('/v1/admin/writing/scenarios', payload);
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
    if (!window.confirm('Delete this scenario? This cannot be undone.')) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/scenarios/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const generate = async () => {
    setBusy('gen');
    try {
      const r = await apiClient.post<WritingScenarioDto>('/v1/admin/writing/scenarios/generate', {
        profession: 'medicine',
        letterType: 'LT-RR',
        difficulty: 3,
        instructions: generatorBriefing,
      });
      if (r) {
        setEditing({
          id: r.id,
          title: r.title,
          letterType: r.letterType,
          profession: r.profession,
          subDiscipline: r.subDiscipline ?? '',
          topics: r.topics.join(', '),
          difficulty: r.difficulty,
          caseNotesMarkdown: '',
          isDiagnostic: r.isDiagnostic,
          status: r.status,
        });
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Generation failed.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><FileText className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Scenarios</h1>
          <p className="mt-1 text-sm text-muted">Manage the V2 scenario library. Status governs visibility to learners.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New scenario</Button>
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <h2 className="text-base font-bold text-navy"><Sparkles className="mr-1 inline h-4 w-4 text-amber-600" aria-hidden="true" /> Generate with AI</h2>
          <p className="mt-1 text-xs text-muted">Provide a short briefing. The model returns a draft scenario you can edit before publishing.</p>
          <div className="mt-3 flex flex-wrap items-end gap-2">
            <label className="flex flex-1 flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Briefing
              <input
                type="text"
                value={generatorBriefing}
                onChange={(e) => setGeneratorBriefing(e.target.value)}
                placeholder="e.g. discharge letter for a 72-year-old with COPD"
                className="min-h-9 rounded border border-border bg-background px-2 text-sm"
              />
            </label>
            <Button onClick={() => void generate()} loading={busy === 'gen'} disabled={!generatorBriefing.trim()}>Generate draft</Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Scenario list">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th scope="col" className="py-2 text-left">Title</th>
                <th scope="col" className="text-left">Profession</th>
                <th scope="col" className="text-left">Letter</th>
                <th scope="col" className="text-left">Difficulty</th>
                <th scope="col" className="text-left">Status</th>
                <th scope="col" className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? (
                <tr><td colSpan={6} className="py-4 text-center text-xs text-muted">No scenarios yet.</td></tr>
              ) : null}
              {items.map((s) => (
                <tr key={s.id} className="border-b border-border/60">
                  <td className="py-2 font-bold text-navy">{s.title}</td>
                  <td className="capitalize">{s.profession}</td>
                  <td>{s.letterType}</td>
                  <td>{s.difficulty}</td>
                  <td><Badge variant={s.status === 'published' ? 'success' : s.status === 'archived' ? 'muted' : 'warning'} size="sm">{s.status}</Badge></td>
                  <td className="text-right">
                    <Button size="sm" variant="outline" onClick={() => setEditing({ id: s.id, title: s.title, letterType: s.letterType, profession: s.profession, subDiscipline: s.subDiscipline ?? '', topics: s.topics.join(', '), difficulty: s.difficulty, caseNotesMarkdown: '', isDiagnostic: s.isDiagnostic, status: s.status })}>Edit</Button>
                    <Button size="sm" variant="ghost" onClick={() => void remove(s.id)} loading={busy === `del-${s.id}`} aria-label={`Delete ${s.title}`}><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      {editing ? (
        <aside role="dialog" aria-modal="true" aria-label="Scenario editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-navy/40">
          <div className="flex h-full w-full max-w-2xl flex-col gap-3 overflow-y-auto bg-surface p-5 pt-[max(1.25rem,env(safe-area-inset-top))] pb-[max(1.25rem,env(safe-area-inset-bottom))] shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.id ? 'Edit scenario' : 'New scenario'}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Title
              <input
                type="text"
                value={editing.title}
                onChange={(e) => setEditing({ ...editing, title: e.target.value })}
                className="min-h-9 rounded border border-border bg-background px-2 text-sm"
                required
              />
            </label>

            <div className="grid gap-2 md:grid-cols-2">
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
                Sub-discipline
                <input type="text" value={editing.subDiscipline} onChange={(e) => setEditing({ ...editing, subDiscipline: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Difficulty
                <input type="number" min={1} max={5} value={editing.difficulty} onChange={(e) => setEditing({ ...editing, difficulty: Number(e.target.value) })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Topics (comma-separated)
              <input type="text" value={editing.topics} onChange={(e) => setEditing({ ...editing, topics: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
            </label>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Case notes (markdown)
              <textarea rows={12} value={editing.caseNotesMarkdown} onChange={(e) => setEditing({ ...editing, caseNotesMarkdown: e.target.value })} className="rounded border border-border bg-background p-2 text-sm font-mono" required />
            </label>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={editing.isDiagnostic} onChange={(e) => setEditing({ ...editing, isDiagnostic: e.target.checked })} className="accent-primary" />
                Diagnostic scenario
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
