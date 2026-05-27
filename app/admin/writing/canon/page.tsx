'use client';

import { useCallback, useEffect, useState } from 'react';
import { BookOpen, Plus, RefreshCcw, TestTube, Trash2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import type { WritingCanonRuleV2Dto } from '@/lib/writing/types';

interface CanonRuleUpsertForm {
  id: string;
  category: string;
  appliesToLetterTypes: string;
  appliesToProfessions: string;
  severity: 'high' | 'medium' | 'low';
  ruleText: string;
  correctExamples: string;
  incorrectExamples: string;
  detectionType: 'regex' | 'llm' | 'structural';
  detectionConfig: string;
  lessonId: string;
  version: string;
  active: boolean;
  isNew: boolean;
}

const EMPTY_FORM: CanonRuleUpsertForm = {
  id: '',
  category: 'style',
  appliesToLetterTypes: '',
  appliesToProfessions: '',
  severity: 'medium',
  ruleText: '',
  correctExamples: '',
  incorrectExamples: '',
  detectionType: 'regex',
  detectionConfig: '{}',
  lessonId: '',
  version: '1.0',
  active: true,
  isNew: true,
};

export default function AdminWritingCanonPage() {
  const [rules, setRules] = useState<WritingCanonRuleV2Dto[]>([]);
  const [editing, setEditing] = useState<CanonRuleUpsertForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [testText, setTestText] = useState('');
  const [testRuleId, setTestRuleId] = useState('');
  const [testResult, setTestResult] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const r = await apiClient.get<{ items: WritingCanonRuleV2Dto[] }>('/v1/admin/writing/canon');
      setRules(r.items.slice().sort((a, b) => a.id.localeCompare(b.id)));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load canon rules.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!editing) return;
    setBusy('save');
    try {
      let detectionConfig: unknown = {};
      try {
        detectionConfig = JSON.parse(editing.detectionConfig);
      } catch {
        setError('Detection config must be valid JSON.');
        setBusy(null);
        return;
      }
      const payload = {
        id: editing.id,
        category: editing.category,
        appliesToLetterTypes: editing.appliesToLetterTypes.split(',').map((s) => s.trim()).filter(Boolean),
        appliesToProfessions: editing.appliesToProfessions.split(',').map((s) => s.trim()).filter(Boolean),
        severity: editing.severity,
        ruleText: editing.ruleText,
        correctExamples: editing.correctExamples.split('\n').map((s) => s.trim()).filter(Boolean),
        incorrectExamples: editing.incorrectExamples.split('\n').map((s) => s.trim()).filter(Boolean),
        detectionType: editing.detectionType,
        detectionConfig,
        lessonId: editing.lessonId || null,
        version: editing.version,
        active: editing.active,
      };
      if (editing.isNew) {
        await apiClient.post('/v1/admin/writing/canon', payload);
      } else {
        await apiClient.put(`/v1/admin/writing/canon/${encodeURIComponent(editing.id)}`, payload);
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
    if (!window.confirm(`Delete rule ${id}?`)) return;
    setBusy(`del-${id}`);
    try {
      await apiClient.delete(`/v1/admin/writing/canon/${encodeURIComponent(id)}`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusy(null);
    }
  };

  const runTest = async () => {
    if (!testRuleId || !testText) return;
    setBusy('test');
    setTestResult(null);
    try {
      const r = await apiClient.post<{ ruleId: string; triggered: boolean; violations: Array<{ snippet: string; charStart: number; charEnd: number }> }>(
        `/v1/admin/writing/canon/${encodeURIComponent(testRuleId)}/test`,
        { letter: testText },
      );
      setTestResult(`${r.ruleId}: ${r.triggered ? `${r.violations.length} violation(s)` : 'no match'}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Test failed.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><BookOpen className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Canon Rules</h1>
          <p className="mt-1 text-sm text-muted">Dr Ahmed&apos;s rule library. Versioning is manual — bump <code>version</code> before publishing breaking changes.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button onClick={() => void load()} variant="outline"><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
          <Button onClick={() => setEditing({ ...EMPTY_FORM })}><Plus className="h-4 w-4" aria-hidden="true" /> New rule</Button>
        </div>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <h2 className="text-base font-bold text-navy"><TestTube className="mr-1 inline h-4 w-4 text-amber-600" aria-hidden="true" /> Test rule detection</h2>
          <p className="mt-1 text-xs text-muted">Paste a draft letter and pick a rule id — we&apos;ll show any violations the engine flags.</p>
          <div className="mt-3 grid gap-2 md:grid-cols-3">
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Rule id
              <input type="text" value={testRuleId} onChange={(e) => setTestRuleId(e.target.value)} placeholder="SC-012" className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
            </label>
            <div className="md:col-span-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Letter
                <textarea rows={4} value={testText} onChange={(e) => setTestText(e.target.value)} placeholder="Paste a letter to scan…" className="rounded border border-border bg-background p-2 text-sm" />
              </label>
            </div>
          </div>
          <div className="mt-3 flex items-center justify-end gap-2">
            <Button onClick={() => void runTest()} loading={busy === 'test'} disabled={!testText || !testRuleId}>Run test</Button>
          </div>
          {testResult ? <p role="status" className="mt-2 rounded border border-emerald-300 bg-emerald-50 p-2 text-xs text-emerald-800">{testResult}</p> : null}
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Canon rules">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Id</th>
                <th className="text-left">Category</th>
                <th className="text-left">Severity</th>
                <th className="text-left">Detection</th>
                <th className="text-left">Version</th>
                <th className="text-left">Active</th>
                <th className="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {rules.length === 0 ? <tr><td colSpan={7} className="py-4 text-center text-xs text-muted">No rules yet.</td></tr> : null}
              {rules.map((rule) => (
                <tr key={rule.id} className="border-b border-border/60">
                  <td className="py-2 font-bold text-navy">{rule.id}</td>
                  <td>{rule.category}</td>
                  <td><Badge variant={rule.severity === 'high' ? 'danger' : rule.severity === 'medium' ? 'warning' : 'muted'} size="sm">{rule.severity}</Badge></td>
                  <td className="capitalize">{rule.detectionType}</td>
                  <td>{rule.version}</td>
                  <td>{rule.active ? 'Yes' : 'No'}</td>
                  <td className="text-right">
                    <Button size="sm" variant="outline" onClick={() => setEditing({ id: rule.id, category: rule.category, appliesToLetterTypes: rule.appliesToLetterTypes.join(', '), appliesToProfessions: rule.appliesToProfessions.join(', '), severity: rule.severity, ruleText: rule.ruleText, correctExamples: rule.correctExamples.join('\n'), incorrectExamples: rule.incorrectExamples.join('\n'), detectionType: rule.detectionType, detectionConfig: '{}', lessonId: rule.lessonId ?? '', version: rule.version, active: rule.active, isNew: false })}>Edit</Button>
                    <Button size="sm" variant="ghost" onClick={() => void remove(rule.id)} loading={busy === `del-${rule.id}`} aria-label="Delete"><Trash2 className="h-4 w-4" aria-hidden="true" /></Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      {editing ? (
        <aside role="dialog" aria-modal="true" aria-label="Canon rule editor" className="fixed inset-0 z-50 flex items-stretch justify-end bg-black/40">
          <div className="flex h-full w-full max-w-2xl flex-col gap-3 overflow-y-auto bg-surface p-5 shadow-2xl">
            <header className="flex items-center justify-between gap-2">
              <h2 className="text-lg font-bold text-navy">{editing.isNew ? 'New rule' : `Edit ${editing.id}`}</h2>
              <Button variant="ghost" onClick={() => setEditing(null)}>Close</Button>
            </header>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Rule id (e.g. SC-026)
              <input type="text" value={editing.id} onChange={(e) => setEditing({ ...editing, id: e.target.value })} disabled={!editing.isNew} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
            </label>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Category
                <input type="text" value={editing.category} onChange={(e) => setEditing({ ...editing, category: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Severity
                <select value={editing.severity} onChange={(e) => setEditing({ ...editing, severity: e.target.value as 'high' | 'medium' | 'low' })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  <option value="high">High</option>
                  <option value="medium">Medium</option>
                  <option value="low">Low</option>
                </select>
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Detection type
                <select value={editing.detectionType} onChange={(e) => setEditing({ ...editing, detectionType: e.target.value as 'regex' | 'llm' | 'structural' })} className="min-h-9 rounded border border-border bg-background px-2 text-sm">
                  <option value="regex">Regex</option>
                  <option value="llm">LLM</option>
                  <option value="structural">Structural</option>
                </select>
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Version
                <input type="text" value={editing.version} onChange={(e) => setEditing({ ...editing, version: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Rule text
              <textarea rows={3} value={editing.ruleText} onChange={(e) => setEditing({ ...editing, ruleText: e.target.value })} className="rounded border border-border bg-background p-2 text-sm" />
            </label>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Correct examples (one per line)
              <textarea rows={3} value={editing.correctExamples} onChange={(e) => setEditing({ ...editing, correctExamples: e.target.value })} className="rounded border border-border bg-background p-2 text-sm" />
            </label>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Incorrect examples (one per line)
              <textarea rows={3} value={editing.incorrectExamples} onChange={(e) => setEditing({ ...editing, incorrectExamples: e.target.value })} className="rounded border border-border bg-background p-2 text-sm" />
            </label>

            <div className="grid gap-2 md:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Applies to letter types (comma-separated)
                <input type="text" value={editing.appliesToLetterTypes} onChange={(e) => setEditing({ ...editing, appliesToLetterTypes: e.target.value })} placeholder="LT-RR, LT-DG" className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Applies to professions (comma-separated)
                <input type="text" value={editing.appliesToProfessions} onChange={(e) => setEditing({ ...editing, appliesToProfessions: e.target.value })} placeholder="medicine, nursing" className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Lesson id (optional)
                <input type="text" value={editing.lessonId} onChange={(e) => setEditing({ ...editing, lessonId: e.target.value })} className="min-h-9 rounded border border-border bg-background px-2 text-sm" />
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={editing.active} onChange={(e) => setEditing({ ...editing, active: e.target.checked })} className="accent-primary" />
                Active
              </label>
            </div>

            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
              Detection config (JSON)
              <textarea rows={5} value={editing.detectionConfig} onChange={(e) => setEditing({ ...editing, detectionConfig: e.target.value })} className="rounded border border-border bg-background p-2 text-sm font-mono" />
            </label>

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
