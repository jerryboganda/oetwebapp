'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Loader2, Save, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import {
  adminListeningGetStructure,
  adminListeningPatchQuestion,
} from '@/lib/api';
import type {
  ListeningAuthoredQuestion,
  ListeningAuthoredQuestionList,
} from '@/lib/types/admin/listening-authoring';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

const SKILL_TAGS = [
  { value: 'purpose', label: 'Purpose' },
  { value: 'gist', label: 'Gist' },
  { value: 'detail', label: 'Detail' },
  { value: 'opinion', label: 'Opinion' },
  { value: 'warning', label: 'Warning' },
  { value: 'attitude', label: 'Attitude' },
  { value: 'note_completion', label: 'Note completion' },
  { value: 'other', label: 'Other' },
];

export function StructureTab({ paperId, onToast }: Props) {
  const [doc, setDoc] = useState<ListeningAuthoredQuestionList | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [edits, setEdits] = useState<Record<string, Partial<ListeningAuthoredQuestion>>>({});

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListeningGetStructure(paperId);
      setDoc(data);
      setEdits({});
    } catch (e) {
      onToast('error', `Load failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const updateEdit = useCallback((id: string, patch: Partial<ListeningAuthoredQuestion>) => {
    setEdits((prev) => ({ ...prev, [id]: { ...prev[id], ...patch } }));
  }, []);

  const saveQuestion = useCallback(
    async (q: ListeningAuthoredQuestion) => {
      const patch = edits[q.id];
      if (!patch) return;
      setSavingId(q.id);
      try {
        const next = await adminListeningPatchQuestion(paperId, q.id, {
          stem: patch.stem,
          correctAnswer: patch.correctAnswer,
          options: patch.options ?? undefined,
          explanation: patch.explanation ?? undefined,
          skillTag: patch.skillTag ?? undefined,
          acceptedAnswers: patch.acceptedAnswers ?? undefined,
          transcriptExcerpt: patch.transcriptExcerpt ?? undefined,
        });
        setDoc(next);
        setEdits((prev) => {
          const c = { ...prev };
          delete c[q.id];
          return c;
        });
        onToast('success', `Q${q.number} saved.`);
      } catch (e) {
        onToast('error', `Save Q${q.number} failed: ${(e as Error).message}`);
      } finally {
        setSavingId(null);
      }
    },
    [edits, onToast, paperId],
  );

  const grouped = useMemo(() => {
    if (!doc) return {} as Record<string, ListeningAuthoredQuestion[]>;
    const out: Record<string, ListeningAuthoredQuestion[]> = {};
    for (const q of doc.questions) {
      const k = String(q.partCode);
      (out[k] ||= []).push(q);
    }
    return out;
  }, [doc]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading structure…
      </div>
    );
  }
  if (!doc) return null;

  const c = doc.counts;
  const expected = c.partACount === 24 && c.partBCount === 6 && c.partCCount === 12 && c.totalItems === 42;

  return (
    <div className="space-y-4">
      <AdminRoutePanel title="Item counts">
        <div className="flex flex-wrap gap-2 text-sm">
          <Badge variant={c.partACount === 24 ? 'success' : 'danger'}>Part A: {c.partACount}/24</Badge>
          <Badge variant={c.partBCount === 6 ? 'success' : 'danger'}>Part B: {c.partBCount}/6</Badge>
          <Badge variant={c.partCCount === 12 ? 'success' : 'danger'}>Part C: {c.partCCount}/12</Badge>
          <Badge variant={expected ? 'success' : 'warning'}>Total: {c.totalItems}/42</Badge>
        </div>
      </AdminRoutePanel>

      {(['A1', 'A2', 'B', 'C1', 'C2'] as const).map((code) => {
        const items = grouped[code] ?? [];
        if (items.length === 0) return null;
        return (
          <AdminRoutePanel key={code} title={`Part ${code} — ${items.length} items`}>
            <div className="space-y-3">
              {items.map((q) => {
                const e = edits[q.id] ?? {};
                const stem = e.stem ?? q.stem;
                const correct = e.correctAnswer ?? q.correctAnswer;
                const skill = (e.skillTag ?? q.skillTag) || '';
                const explanation = e.explanation ?? q.explanation ?? '';
                const isMcq = q.type === 'multiple_choice_3';
                const opts = (e.options ?? q.options ?? []).slice();
                const dirty = !!edits[q.id];
                return (
                  <div key={q.id} className="rounded-xl border border-border bg-background-light p-3">
                    <div className="flex items-start justify-between gap-2">
                      <div className="text-xs font-semibold text-muted">
                        Q{q.number} · <span className="font-mono">{q.id}</span> · {q.type}
                      </div>
                      <Button
                        variant="primary"
                        size="sm"
                        disabled={!dirty || savingId === q.id}
                        onClick={() => void saveQuestion(q)}
                      >
                        {savingId === q.id ? (
                          <Loader2 className="h-3 w-3 animate-spin" />
                        ) : (
                          <Save className="h-3 w-3" />
                        )}{' '}
                        Save
                      </Button>
                    </div>
                    <div className="mt-2 grid gap-2 md:grid-cols-2">
                      <Textarea
                        label="Stem"
                        value={stem}
                        onChange={(ev) => updateEdit(q.id, { stem: ev.target.value })}
                      />
                      <div className="space-y-2">
                        {isMcq ? (
                          <>
                            {(['A', 'B', 'C'] as const).map((key, idx) => (
                              <Input
                                key={key}
                                label={`Option ${key}`}
                                value={opts[idx] ?? ''}
                                onChange={(ev) => {
                                  const next = opts.slice();
                                  next[idx] = ev.target.value;
                                  updateEdit(q.id, { options: next });
                                }}
                              />
                            ))}
                            <Select
                              label="Correct answer"
                              value={correct}
                              onChange={(ev) => updateEdit(q.id, { correctAnswer: ev.target.value })}
                              options={[
                                { value: 'A', label: 'A' },
                                { value: 'B', label: 'B' },
                                { value: 'C', label: 'C' },
                              ]}
                            />
                          </>
                        ) : (
                          <Input
                            label="Correct answer"
                            value={correct}
                            onChange={(ev) => updateEdit(q.id, { correctAnswer: ev.target.value })}
                          />
                        )}
                        <Select
                          label="Skill tag"
                          value={skill}
                          onChange={(ev) => updateEdit(q.id, { skillTag: ev.target.value })}
                          options={SKILL_TAGS}
                        />
                      </div>
                    </div>
                    <div className="mt-2">
                      <Textarea
                        label="Explanation"
                        value={explanation}
                        onChange={(ev) => updateEdit(q.id, { explanation: ev.target.value })}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </AdminRoutePanel>
        );
      })}

      {Object.keys(grouped).length === 0 && (
        <div className="flex items-center gap-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
          <AlertTriangle className="h-4 w-4" /> No authored questions yet. Use the Extracts tab → AI Propose to seed.
        </div>
      )}
    </div>
  );
}
