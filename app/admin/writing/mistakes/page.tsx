'use client';

import { useCallback, useEffect, useState } from 'react';
import { NotebookTabs, Plus, RefreshCcw, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import type { WritingCommonMistakeDto } from '@/lib/writing/types';

interface MistakeForm {
  id?: string;
  category: string;
  summary: string;
  exampleWrong: string;
  exampleRight: string;
  canonRuleId: string;
  relatedSubSkill: string;
}

const EMPTY_FORM: MistakeForm = {
  category: 'style',
  summary: '',
  exampleWrong: '',
  exampleRight: '',
  canonRuleId: '',
  relatedSubSkill: '',
};

export default function AdminWritingMistakesPage() {
  const { user } = useCurrentUser();
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const [items, setItems] = useState<WritingCommonMistakeDto[]>([]);
  const [editing, setEditing] = useState<MistakeForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiClient.get<{ items: WritingCommonMistakeDto[] }>('/v1/admin/writing/mistakes');
      setItems(response.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load mistakes.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!editing) return;
    if (!canWriteContent) {
      setError('Content write permission is required to save mistake cards.');
      return;
    }
    setBusy('save');
    try {
      const payload = {
        category: editing.category.trim(),
        summary: editing.summary.trim(),
        exampleWrong: editing.exampleWrong,
        exampleRight: editing.exampleRight,
        canonRuleId: editing.canonRuleId.trim() || null,
        relatedSubSkill: editing.relatedSubSkill.trim() || null,
      };
      if (editing.id) {
        await apiClient.put(`/v1/admin/writing/mistakes/${encodeURIComponent(editing.id)}`, payload);
      } else {
        await apiClient.post('/v1/admin/writing/mistakes', payload);
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
    if (!window.confirm('Delete this mistake card?')) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/mistakes/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const edit = (mistake: WritingCommonMistakeDto) => setEditing({
    id: mistake.id,
    category: mistake.category,
    summary: mistake.summary,
    exampleWrong: mistake.exampleWrong,
    exampleRight: mistake.exampleRight,
    canonRuleId: mistake.canonRuleId ?? '',
    relatedSubSkill: mistake.relatedSubSkill ?? '',
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><NotebookTabs className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Mistakes</h1>
          <p className="mt-1 text-sm text-muted">Maintain the common mistake library and map entries to canon rules or W1-W8 sub-skills.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          {canWriteContent ? <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New mistake</Button> : null}
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Writing mistake cards">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Summary</th>
                <th className="text-left">Category</th>
                <th className="text-left">Rule</th>
                <th className="text-left">Skill</th>
                <th className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? <tr><td colSpan={5} className="py-4 text-center text-xs text-muted">No mistake cards yet.</td></tr> : null}
              {items.map((mistake) => (
                <tr key={mistake.id} className="border-b border-border/60 align-top">
                  <td className="max-w-xl py-2 font-bold text-navy">{mistake.summary}</td>
                  <td><Badge variant="info" size="sm">{mistake.category}</Badge></td>
                  <td>{mistake.canonRuleId ?? '-'}</td>
                  <td>{mistake.relatedSubSkill ?? '-'}</td>
                  <td className="text-right">
                    {canWriteContent ? (
                      <>
                        <Button size="sm" variant="outline" onClick={() => edit(mistake)}>Edit</Button>
                        <Button size="sm" variant="ghost" onClick={() => void remove(mistake.id)} loading={busy === `del-${mistake.id}`} aria-label="Delete mistake"><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
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
        <aside role="dialog" aria-modal="true" aria-label="Mistake editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-black/40">
          <div className="flex h-full w-full max-w-2xl flex-col gap-3 overflow-y-auto bg-surface p-5 shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.id ? 'Edit mistake' : 'New mistake'}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <div className="grid gap-2 md:grid-cols-3">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Category<input value={editing.category} onChange={(event) => setEditing({ ...editing, category: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Canon rule<input value={editing.canonRuleId} onChange={(event) => setEditing({ ...editing, canonRuleId: event.target.value })} placeholder="SC-012" className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Skill<select value={editing.relatedSubSkill} onChange={(event) => setEditing({ ...editing, relatedSubSkill: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm"><option value="">None</option>{['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'].map((skill) => <option key={skill} value={skill}>{skill}</option>)}</select></label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Summary<input value={editing.summary} onChange={(event) => setEditing({ ...editing, summary: event.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Wrong example<textarea rows={5} value={editing.exampleWrong} onChange={(event) => setEditing({ ...editing, exampleWrong: event.target.value })} className="rounded border border-border bg-background p-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Corrected example<textarea rows={5} value={editing.exampleRight} onChange={(event) => setEditing({ ...editing, exampleRight: event.target.value })} className="rounded border border-border bg-background p-2 text-sm" /></label>

            <footer className="mt-auto flex items-center justify-end gap-2">
              <Button variant="outline" onClick={() => setEditing(null)}>Cancel</Button>
              <Button onClick={() => void save()} loading={busy === 'save'} disabled={!canWriteContent}>Save</Button>
            </footer>
          </div>
        </aside>
      ) : null}
    </div>
  );
}