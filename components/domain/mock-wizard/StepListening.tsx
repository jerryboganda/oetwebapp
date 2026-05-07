'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { UploadSlot } from './UploadSlot';
import { useWizard } from './WizardShell';
import { ensureBundleSection, ensurePaperWithAssets, type PendingAsset } from './step-helpers';
import {
  setListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningPartCode,
  type ListeningQuestionType,
} from '@/lib/mock-wizard/api';

const PARTS: { code: ListeningPartCode; label: string; target: number }[] = [
  { code: 'A1', label: 'Part A1 (consultation 1)', target: 12 },
  { code: 'A2', label: 'Part A2 (consultation 2)', target: 12 },
  { code: 'B', label: 'Part B (workplace extracts)', target: 6 },
  { code: 'C1', label: 'Part C1 (presentation 1)', target: 6 },
  { code: 'C2', label: 'Part C2 (presentation 2)', target: 6 },
];

interface ItemDraft {
  id: string;
  number: number;
  partCode: ListeningPartCode;
  type: ListeningQuestionType;
  stem: string;
  optionsCsv: string;
  correctAnswer: string;
  acceptedSynonymsCsv: string;
  points: number;
}

function makeId() {
  return Math.random().toString(36).slice(2, 10);
}

function newItem(partCode: ListeningPartCode, number: number): ItemDraft {
  return {
    id: makeId(),
    number,
    partCode,
    type: 'short_answer',
    stem: '',
    optionsCsv: '',
    correctAnswer: '',
    acceptedSynonymsCsv: '',
    points: 1,
  };
}

export function StepListening() {
  const { bundle, refreshBundle, setSavingState, registerCanAdvance, registerStepSubmit } =
    useWizard();
  const existingSection = bundle.sections.find((s) => s.subtestCode === 'listening');
  const existingPaperId = existingSection?.contentPaperId ?? null;

  const [pending, setPending] = useState<Record<string, string>>({}); // role -> mediaAssetId
  const [items, setItems] = useState<ItemDraft[]>([]);
  const [activeTab, setActiveTab] = useState<'A' | 'B' | 'C'>('A');
  const [error, setError] = useState<string | null>(null);

  const counts = useMemo(() => {
    const c = { A: 0, B: 0, C: 0, total: items.length };
    for (const i of items) {
      if (i.partCode.startsWith('A')) c.A += 1;
      else if (i.partCode === 'B') c.B += 1;
      else c.C += 1;
    }
    return c;
  }, [items]);

  // Listening canonical shape is 24 (Part A) + 6 (Part B) + 12 (Part C) = 42 items.
  // Allow advance once shape is hit, OR if a paper already exists from a prior visit
  // (admin can come back and edit later via dedicated authoring page).
  useEffect(() => {
    const shapeOk = counts.A === 24 && counts.B === 6 && counts.C === 12;
    registerCanAdvance('listening', shapeOk || Boolean(existingPaperId));
  }, [counts.A, counts.B, counts.C, existingPaperId, registerCanAdvance]);

  const visibleItems = items.filter((i) => {
    if (activeTab === 'A') return i.partCode === 'A1' || i.partCode === 'A2';
    if (activeTab === 'B') return i.partCode === 'B';
    return i.partCode === 'C1' || i.partCode === 'C2';
  });

  function addItem(partCode: ListeningPartCode) {
    setItems((prev) => [...prev, newItem(partCode, prev.length + 1)]);
  }

  function updateItem(id: string, patch: Partial<ItemDraft>) {
    setItems((prev) => prev.map((i) => (i.id === id ? { ...i, ...patch } : i)));
  }

  function removeItem(id: string) {
    setItems((prev) =>
      prev.filter((i) => i.id !== id).map((i, idx) => ({ ...i, number: idx + 1 })),
    );
  }

  const submit = useCallback(async () => {
    setError(null);
    setSavingState(true);
    try {
      const pendingAssets: PendingAsset[] = Object.entries(pending).map(([role, mediaAssetId]) => ({
        role: role as PendingAsset['role'],
        mediaAssetId,
      }));
      const { paper } = await ensurePaperWithAssets({
        bundle,
        step: 'listening',
        existingPaperId,
        paperTitleSuffix: 'Listening',
        estimatedDurationMinutes: 40,
        pendingAssets,
      });
      if (items.length > 0) {
        const payload: ListeningAuthoredQuestion[] = items.map((i) => ({
          id: i.id,
          number: i.number,
          partCode: i.partCode,
          type: i.type,
          stem: i.stem,
          options: i.type === 'multiple_choice_3'
            ? i.optionsCsv.split('|').map((s) => s.trim()).filter(Boolean)
            : undefined,
          correctAnswer: i.correctAnswer,
          acceptedAnswers: i.acceptedSynonymsCsv
            .split(',')
            .map((s) => s.trim())
            .filter(Boolean),
          points: i.points,
        }));
        await setListeningStructure(paper.id, payload);
      }
      await ensureBundleSection(bundle, 'listening', paper.id);
      setPending({});
      await refreshBundle();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Listening save failed.');
      throw err;
    } finally {
      setSavingState(false);
    }
  }, [bundle, existingPaperId, items, pending, refreshBundle, setSavingState]);

  useEffect(() => {
    registerStepSubmit('listening', submit);
    return () => registerStepSubmit('listening', null);
  }, [registerStepSubmit, submit]);

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 2 — Listening</h2>
        <p className="text-sm text-muted">
          Upload the four required assets, then author the 42-item map. Targets: Part A 24, Part B
          6, Part C 12.
        </p>
        {existingPaperId ? (
          <p className="text-xs text-muted">
            Working on paper <code>{existingPaperId}</code>.
          </p>
        ) : null}
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {/* Asset uploads */}
      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Required assets</h3>
        <div className="grid gap-3 md:grid-cols-2">
          <UploadSlot
            paperId={existingPaperId}
            role="Audio"
            label="Audio (full extract MP3)"
            accept="audio/mpeg"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, Audio: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="QuestionPaper"
            label="Question paper (PDF)"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, QuestionPaper: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="AudioScript"
            label="Audio transcript (PDF or text)"
            accept="application/pdf,text/plain"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, AudioScript: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="AnswerKey"
            label="Answer key (PDF or JSON)"
            accept="application/pdf,application/json"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, AnswerKey: id }))}
          />
        </div>
      </section>

      {/* Items editor */}
      <section className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <div>
            <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Items</h3>
            <p className="text-xs text-muted">
              Total {counts.total} of 42 — Part A {counts.A}/24, Part B {counts.B}/6, Part C{' '}
              {counts.C}/12
            </p>
          </div>
          <div className="flex gap-1">
            {(['A', 'B', 'C'] as const).map((t) => (
              <button
                key={t}
                onClick={() => setActiveTab(t)}
                className={
                  'rounded-xl border px-3 py-1.5 text-xs font-bold ' +
                  (activeTab === t
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-surface text-navy hover:bg-background-light')
                }
              >
                Part {t}
              </button>
            ))}
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          {PARTS.filter((p) => p.code[0] === activeTab).map((p) => (
            <Button key={p.code} variant="outline" size="sm" onClick={() => addItem(p.code)}>
              <Plus className="mr-1 h-3 w-3" /> Add to {p.code}
            </Button>
          ))}
        </div>

        <div className="space-y-3">
          {visibleItems.map((item) => (
            <div key={item.id} className="rounded-2xl border border-border bg-surface p-3">
              <div className="mb-2 flex items-center justify-between gap-2">
                <Badge variant="info">
                  Q{item.number} · {item.partCode}
                </Badge>
                <button
                  onClick={() => removeItem(item.id)}
                  aria-label={`Remove question ${item.number}`}
                  className="text-muted hover:text-red-600"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
              <div className="grid gap-2 md:grid-cols-3">
                <Select
                  label="Part"
                  value={item.partCode}
                  onChange={(e) => updateItem(item.id, { partCode: e.target.value as ListeningPartCode })}
                  options={PARTS.map((p) => ({ value: p.code, label: p.code }))}
                />
                <Select
                  label="Type"
                  value={item.type}
                  onChange={(e) => updateItem(item.id, { type: e.target.value as ListeningQuestionType })}
                  options={[
                    { value: 'short_answer', label: 'Short answer' },
                    { value: 'multiple_choice_3', label: 'Multiple choice (3)' },
                  ]}
                />
                <Input
                  label="Points"
                  type="number"
                  value={String(item.points)}
                  onChange={(e) => updateItem(item.id, { points: Number(e.target.value) || 1 })}
                />
              </div>
              <div className="mt-2">
                <Textarea
                  label="Prompt / stem"
                  value={item.stem}
                  onChange={(e) => updateItem(item.id, { stem: e.target.value })}
                  rows={2}
                />
              </div>
              {item.type === 'multiple_choice_3' ? (
                <div className="mt-2">
                  <Input
                    label="Options (separate with |)"
                    value={item.optionsCsv}
                    onChange={(e) => updateItem(item.id, { optionsCsv: e.target.value })}
                    placeholder="A. Reduce dose | B. Refer | C. Continue"
                  />
                </div>
              ) : null}
              <div className="mt-2 grid gap-2 md:grid-cols-2">
                <Input
                  label="Correct answer"
                  value={item.correctAnswer}
                  onChange={(e) => updateItem(item.id, { correctAnswer: e.target.value })}
                />
                {item.type === 'short_answer' ? (
                  <Input
                    label="Accepted synonyms (CSV)"
                    value={item.acceptedSynonymsCsv}
                    onChange={(e) => updateItem(item.id, { acceptedSynonymsCsv: e.target.value })}
                  />
                ) : null}
              </div>
            </div>
          ))}
          {visibleItems.length === 0 ? (
            <p className="rounded-xl border border-dashed border-border bg-background-light p-4 text-center text-xs text-muted">
              No items in Part {activeTab} yet. Use the buttons above to add one.
            </p>
          ) : null}
        </div>
      </section>
    </div>
  );
}
