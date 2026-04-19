'use client';

import { useCallback, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import type { GrammarExerciseType } from '@/lib/grammar/types';

export interface ContentBlockDraft {
  id?: string;
  sortOrder: number;
  type: 'prose' | 'callout' | 'example' | 'note' | string;
  contentMarkdown: string;
}

export interface ExerciseDraft {
  id?: string;
  sortOrder: number;
  type: GrammarExerciseType | string;
  promptMarkdown: string;
  options: unknown;           // [] or array of {id,label} or array of {left,right}
  correctAnswer: unknown;      // string | string[] | array of {left,right}
  acceptedAnswers: string[];
  explanationMarkdown: string;
  difficulty: 'beginner' | 'intermediate' | 'advanced' | string;
  points: number;
}

export interface LessonDraft {
  examTypeCode: string;
  topicId: string | null;
  title: string;
  description: string;
  level: 'beginner' | 'intermediate' | 'advanced' | string;
  category: string;
  estimatedMinutes: number;
  sortOrder: number;
  sourceProvenance: string;
  prerequisiteLessonIds: string[];
  contentBlocks: ContentBlockDraft[];
  exercises: ExerciseDraft[];
}

export function emptyDraft(): LessonDraft {
  return {
    examTypeCode: 'oet',
    topicId: null,
    title: '',
    description: '',
    level: 'intermediate',
    category: '',
    estimatedMinutes: 12,
    sortOrder: 0,
    sourceProvenance: '',
    prerequisiteLessonIds: [],
    contentBlocks: [
      { sortOrder: 1, type: 'prose', contentMarkdown: '' },
    ],
    exercises: [],
  };
}

/**
 * Structured grammar lesson editor used by both the "new" and "edit"
 * admin pages. Emits a complete {@link LessonDraft} whenever the admin
 * hits Save. The parent is responsible for calling the API.
 */
export function GrammarLessonEditor({
  initial,
  topics,
  onSave,
  onPublish,
  publishable,
  saving,
  publishErrors,
}: {
  initial: LessonDraft;
  topics: Array<{ id: string; name: string; slug: string }>;
  onSave: (draft: LessonDraft) => void;
  onPublish?: () => void;
  publishable?: boolean;
  saving?: boolean;
  publishErrors?: string[] | null;
}) {
  const [draft, setDraft] = useState<LessonDraft>(initial);

  const update = useCallback(<K extends keyof LessonDraft>(key: K, value: LessonDraft[K]) => {
    setDraft((prev) => ({ ...prev, [key]: value }));
  }, []);

  const addBlock = useCallback(() => {
    setDraft((prev) => ({
      ...prev,
      contentBlocks: [
        ...prev.contentBlocks,
        { sortOrder: prev.contentBlocks.length + 1, type: 'prose', contentMarkdown: '' },
      ],
    }));
  }, []);

  const updateBlock = useCallback((i: number, patch: Partial<ContentBlockDraft>) => {
    setDraft((prev) => {
      const blocks = prev.contentBlocks.slice();
      blocks[i] = { ...blocks[i], ...patch };
      return { ...prev, contentBlocks: blocks };
    });
  }, []);

  const removeBlock = useCallback((i: number) => {
    setDraft((prev) => ({
      ...prev,
      contentBlocks: prev.contentBlocks.filter((_, idx) => idx !== i),
    }));
  }, []);

  const addExercise = useCallback((type: GrammarExerciseType) => {
    setDraft((prev) => ({
      ...prev,
      exercises: [
        ...prev.exercises,
        {
          sortOrder: prev.exercises.length + 1,
          type,
          promptMarkdown: '',
          options: type === 'mcq' ? [{ id: 'a', label: '' }, { id: 'b', label: '' }, { id: 'c', label: '' }]
                   : type === 'matching' ? [{ left: '', right: '' }]
                   : [],
          correctAnswer: type === 'matching' ? [] : '',
          acceptedAnswers: [],
          explanationMarkdown: '',
          difficulty: 'intermediate',
          points: 1,
        },
      ],
    }));
  }, []);

  const updateExercise = useCallback((i: number, patch: Partial<ExerciseDraft>) => {
    setDraft((prev) => {
      const list = prev.exercises.slice();
      list[i] = { ...list[i], ...patch };
      return { ...prev, exercises: list };
    });
  }, []);

  const removeExercise = useCallback((i: number) => {
    setDraft((prev) => ({
      ...prev,
      exercises: prev.exercises.filter((_, idx) => idx !== i),
    }));
  }, []);

  return (
    <div className="space-y-6">
      {/* ── Metadata ─────────────────────────────────────────────── */}
      <Card className="space-y-4 p-5">
        <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Lesson metadata</h2>
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Title" value={draft.title} onChange={(e) => update('title', e.target.value)} />
          <Input label="Category slug" value={draft.category} onChange={(e) => update('category', e.target.value)} placeholder="tenses" />
          <Select
            label="Exam"
            value={draft.examTypeCode}
            onChange={(e) => update('examTypeCode', e.target.value)}
            options={[
              { value: 'oet', label: 'OET' },
              { value: 'ielts', label: 'IELTS' },
              { value: 'pte', label: 'PTE' },
            ]}
          />
          <Select
            label="Topic"
            value={draft.topicId ?? ''}
            onChange={(e) => update('topicId', e.target.value || null)}
            options={[{ value: '', label: 'No topic' }, ...topics.map((t) => ({ value: t.id, label: t.name }))]}
          />
          <Select
            label="Level"
            value={draft.level}
            onChange={(e) => update('level', e.target.value)}
            options={[
              { value: 'beginner', label: 'Beginner' },
              { value: 'intermediate', label: 'Intermediate' },
              { value: 'advanced', label: 'Advanced' },
            ]}
          />
          <Input label="Estimated minutes" type="number" value={String(draft.estimatedMinutes)} onChange={(e) => update('estimatedMinutes', Number(e.target.value) || 0)} />
          <Input label="Sort order" type="number" value={String(draft.sortOrder)} onChange={(e) => update('sortOrder', Number(e.target.value) || 0)} />
          <Input label="Source provenance" value={draft.sourceProvenance} onChange={(e) => update('sourceProvenance', e.target.value)} placeholder="e.g. Dr Hesham OET Writing Rulebook v2.1" />
        </div>
        <Textarea label="Description" value={draft.description} onChange={(e) => update('description', e.target.value)} placeholder="Short lesson description surfaced on cards" />
      </Card>

      {/* ── Content blocks ───────────────────────────────────────── */}
      <Card className="space-y-4 p-5">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Content blocks ({draft.contentBlocks.length})</h2>
          <Button size="sm" onClick={addBlock} className="inline-flex items-center gap-1">
            <Plus className="h-3.5 w-3.5" /> Add block
          </Button>
        </div>
        {draft.contentBlocks.map((b, i) => (
          <Card key={i} className="space-y-2 border border-border p-3">
            <div className="flex items-center justify-between">
              <Select
                label=""
                value={b.type}
                onChange={(e) => updateBlock(i, { type: e.target.value })}
                className="w-40"
                options={[
                  { value: 'prose', label: 'Prose' },
                  { value: 'callout', label: 'Callout' },
                  { value: 'example', label: 'Example' },
                  { value: 'note', label: 'Note' },
                ]}
              />
              <Button size="sm" variant="outline" onClick={() => removeBlock(i)}>
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </div>
            <Textarea
              label=""
              value={b.contentMarkdown}
              onChange={(e) => updateBlock(i, { contentMarkdown: e.target.value })}
              placeholder="Markdown-ish content. **bold**, *italic*, `code`, blank lines for new paragraph."
              rows={4}
            />
          </Card>
        ))}
      </Card>

      {/* ── Exercises ────────────────────────────────────────────── */}
      <Card className="space-y-4 p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Exercises ({draft.exercises.length})</h2>
          <div className="flex flex-wrap gap-1">
            {(['mcq', 'fill_blank', 'error_correction', 'sentence_transformation', 'matching'] as GrammarExerciseType[]).map((t) => (
              <Button key={t} size="sm" variant="outline" onClick={() => addExercise(t)}>
                + {labelFor(t)}
              </Button>
            ))}
          </div>
        </div>
        {draft.exercises.map((ex, i) => (
          <ExerciseCard
            key={i}
            index={i}
            exercise={ex}
            onUpdate={(p) => updateExercise(i, p)}
            onRemove={() => removeExercise(i)}
          />
        ))}
      </Card>

      {/* ── Publish gate ─────────────────────────────────────────── */}
      {publishErrors && publishErrors.length > 0 ? (
        <Card className="border-rose-200 bg-rose-50 p-4 text-sm text-rose-800">
          <p className="font-semibold">Publish gate failures</p>
          <ul className="mt-1 list-disc pl-5">
            {publishErrors.map((e, i) => <li key={i}>{e}</li>)}
          </ul>
        </Card>
      ) : null}

      <div className="flex flex-wrap justify-end gap-2">
        <Button onClick={() => onSave(draft)} disabled={saving}>
          {saving ? 'Saving…' : 'Save lesson'}
        </Button>
        {onPublish ? (
          <Button variant={publishable ? 'primary' : 'outline'} disabled={!publishable || saving} onClick={onPublish}>
            Publish
          </Button>
        ) : null}
      </div>
    </div>
  );
}

function labelFor(t: GrammarExerciseType) {
  switch (t) {
    case 'mcq': return 'MCQ';
    case 'fill_blank': return 'Fill blank';
    case 'error_correction': return 'Error correction';
    case 'sentence_transformation': return 'Transformation';
    case 'matching': return 'Matching';
  }
}

function ExerciseCard({
  index,
  exercise,
  onUpdate,
  onRemove,
}: {
  index: number;
  exercise: ExerciseDraft;
  onUpdate: (patch: Partial<ExerciseDraft>) => void;
  onRemove: () => void;
}) {
  const type = exercise.type as GrammarExerciseType;

  return (
    <Card className="space-y-3 border border-border p-4">
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2">
          <Badge className="bg-primary/10 text-primary-dark">#{index + 1} · {labelFor(type)}</Badge>
          <Select
            label=""
            value={exercise.difficulty}
            onChange={(e) => onUpdate({ difficulty: e.target.value })}
            options={[
              { value: 'beginner', label: 'Beginner' },
              { value: 'intermediate', label: 'Intermediate' },
              { value: 'advanced', label: 'Advanced' },
            ]}
          />
          <Input label="" type="number" value={String(exercise.points)} onChange={(e) => onUpdate({ points: Number(e.target.value) || 1 })} className="w-20" />
        </div>
        <Button variant="outline" size="sm" onClick={onRemove}>
          <Trash2 className="h-3.5 w-3.5" />
        </Button>
      </div>

      <Textarea label="Prompt" value={exercise.promptMarkdown} onChange={(e) => onUpdate({ promptMarkdown: e.target.value })} rows={2} />

      {type === 'mcq' ? (
        <McqOptionsEditor exercise={exercise} onUpdate={onUpdate} />
      ) : type === 'matching' ? (
        <MatchingPairsEditor exercise={exercise} onUpdate={onUpdate} />
      ) : (
        <TextAnswerEditor exercise={exercise} onUpdate={onUpdate} />
      )}

      <Textarea
        label="Explanation"
        value={exercise.explanationMarkdown}
        onChange={(e) => onUpdate({ explanationMarkdown: e.target.value })}
        placeholder="Shown after submission. Supports **bold**, *italic*, `code`."
        rows={2}
      />
    </Card>
  );
}

function McqOptionsEditor({ exercise, onUpdate }: { exercise: ExerciseDraft; onUpdate: (p: Partial<ExerciseDraft>) => void }) {
  const opts = useMemo(() => {
    const raw = Array.isArray(exercise.options) ? (exercise.options as Array<{ id: string; label: string }>) : [];
    return raw.map((o) => ({ id: o.id ?? '', label: o.label ?? '' }));
  }, [exercise.options]);

  function setOpt(i: number, patch: { id?: string; label?: string }) {
    const next = opts.slice();
    next[i] = { ...next[i], ...patch };
    onUpdate({ options: next });
  }
  function addOpt() {
    const nextId = String.fromCharCode('a'.charCodeAt(0) + opts.length);
    onUpdate({ options: [...opts, { id: nextId, label: '' }] });
  }
  function removeOpt(i: number) {
    onUpdate({ options: opts.filter((_, idx) => idx !== i) });
  }

  const correctId = typeof exercise.correctAnswer === 'string' ? exercise.correctAnswer : '';

  return (
    <div className="space-y-2">
      {opts.map((o, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input label="" value={o.id} onChange={(e) => setOpt(i, { id: e.target.value })} className="w-16" />
          <Input label="" value={o.label} onChange={(e) => setOpt(i, { label: e.target.value })} placeholder="Option text" className="flex-1" />
          <label className="inline-flex items-center gap-1 text-xs text-muted">
            <input
              type="radio"
              name={`correct-${exercise.sortOrder}`}
              checked={correctId === o.id}
              onChange={() => onUpdate({ correctAnswer: o.id })}
            />
            correct
          </label>
          <Button size="sm" variant="outline" onClick={() => removeOpt(i)}>
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      ))}
      <Button size="sm" variant="outline" onClick={addOpt}>+ Option</Button>
    </div>
  );
}

function TextAnswerEditor({ exercise, onUpdate }: { exercise: ExerciseDraft; onUpdate: (p: Partial<ExerciseDraft>) => void }) {
  const correctStr = typeof exercise.correctAnswer === 'string'
    ? exercise.correctAnswer
    : Array.isArray(exercise.correctAnswer)
      ? (exercise.correctAnswer as unknown[]).filter((x) => typeof x === 'string').join(' || ')
      : '';
  const acceptedStr = (exercise.acceptedAnswers ?? []).join(' || ');
  return (
    <div className="space-y-2">
      <Input
        label="Correct answer (multiple alternatives separated by ` || `)"
        value={correctStr}
        onChange={(e) => {
          const parts = e.target.value.split('||').map((s) => s.trim()).filter(Boolean);
          onUpdate({ correctAnswer: parts.length <= 1 ? (parts[0] ?? '') : parts });
        }}
      />
      <Input
        label="Accepted synonyms (optional, ` || ` separated)"
        value={acceptedStr}
        onChange={(e) => onUpdate({ acceptedAnswers: e.target.value.split('||').map((s) => s.trim()).filter(Boolean) })}
      />
    </div>
  );
}

function MatchingPairsEditor({ exercise, onUpdate }: { exercise: ExerciseDraft; onUpdate: (p: Partial<ExerciseDraft>) => void }) {
  const options = Array.isArray(exercise.options) ? (exercise.options as Array<{ left: string; right: string }>) : [];
  const correct = Array.isArray(exercise.correctAnswer) ? (exercise.correctAnswer as Array<{ left: string; right: string }>) : options;

  function setPair(i: number, patch: { left?: string; right?: string }) {
    const next = options.slice();
    next[i] = { ...next[i], ...patch };
    onUpdate({ options: next, correctAnswer: next });
  }
  function addPair() {
    const next = [...options, { left: '', right: '' }];
    onUpdate({ options: next, correctAnswer: next });
  }
  function removePair(i: number) {
    const next = options.filter((_, idx) => idx !== i);
    onUpdate({ options: next, correctAnswer: next });
  }
  return (
    <div className="space-y-2">
      {options.map((p, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input label="" value={p.left} onChange={(e) => setPair(i, { left: e.target.value })} placeholder="Left item" className="flex-1" />
          <span className="text-muted">→</span>
          <Input label="" value={p.right} onChange={(e) => setPair(i, { right: e.target.value })} placeholder="Matching right item" className="flex-1" />
          <Button size="sm" variant="outline" onClick={() => removePair(i)}>
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      ))}
      <Button size="sm" variant="outline" onClick={addPair}>+ Pair</Button>
      <p className="text-[11px] text-muted">Correct answer is inferred from the pairs above.</p>
    </div>
  );
}

export function draftToApi(draft: LessonDraft) {
  return {
    examTypeCode: draft.examTypeCode,
    topicId: draft.topicId,
    title: draft.title,
    description: draft.description,
    level: draft.level,
    category: draft.category,
    estimatedMinutes: draft.estimatedMinutes,
    sortOrder: draft.sortOrder,
    prerequisiteLessonId: null,
    prerequisiteLessonIds: draft.prerequisiteLessonIds,
    sourceProvenance: draft.sourceProvenance,
    contentBlocks: draft.contentBlocks.map((b, i) => ({
      id: b.id ?? null,
      sortOrder: b.sortOrder || i + 1,
      type: b.type,
      contentMarkdown: b.contentMarkdown,
      content: null,
    })),
    exercises: draft.exercises.map((e, i) => ({
      id: e.id ?? null,
      sortOrder: e.sortOrder || i + 1,
      type: e.type,
      promptMarkdown: e.promptMarkdown,
      options: e.options ?? [],
      correctAnswer: e.correctAnswer,
      acceptedAnswers: e.acceptedAnswers ?? [],
      explanationMarkdown: e.explanationMarkdown,
      difficulty: e.difficulty,
      points: e.points,
    })),
  };
}
