'use client';

import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, Plus, Save, Trash2, X } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningStructure,
  patchListeningQuestion,
  type ListeningAuthoredQuestion,
  type ListeningAuthoredQuestionList,
  type ListeningDistractorCategory,
  type ListeningQuestionPatchBody,
  type ListeningSpeakerAttitude,
} from '@/lib/listening-authoring-api';

const DISTRACTOR_OPTIONS: { value: ListeningDistractorCategory | ''; label: string }[] = [
  { value: '', label: '— Not tagged —' },
  { value: 'too_strong', label: 'Too strong' },
  { value: 'too_weak', label: 'Too weak' },
  { value: 'wrong_speaker', label: 'Wrong speaker' },
  { value: 'opposite_meaning', label: 'Opposite meaning' },
  { value: 'reused_keyword', label: 'Reused keyword' },
  { value: 'out_of_scope', label: 'Out of scope' },
];

const ATTITUDE_OPTIONS: { value: ListeningSpeakerAttitude | ''; label: string }[] = [
  { value: '', label: '— Unspecified —' },
  { value: 'concerned', label: 'Concerned' },
  { value: 'optimistic', label: 'Optimistic' },
  { value: 'doubtful', label: 'Doubtful' },
  { value: 'critical', label: 'Critical' },
  { value: 'neutral', label: 'Neutral' },
  { value: 'other', label: 'Other' },
];

type LoadState = 'loading' | 'ready' | 'error';
type SaveState = 'idle' | 'saving' | 'saved' | 'error';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

interface FormState {
  stem: string;
  points: number;
  correctAnswer: string;
  acceptedAnswers: string[];
  options: string[];
  optionDistractorWhy: (string | null)[];
  optionDistractorCategory: (ListeningDistractorCategory | null)[];
  explanation: string;
  transcriptExcerpt: string;
  distractorExplanation: string;
  skillTag: string;
  speakerAttitude: ListeningSpeakerAttitude | '';
  transcriptEvidenceStartMs: number | '';
  transcriptEvidenceEndMs: number | '';
}

function fromQuestion(q: ListeningAuthoredQuestion): FormState {
  const isMcq = q.type === 'multiple_choice_3';
  return {
    stem: q.stem ?? '',
    points: q.points ?? 1,
    correctAnswer: q.correctAnswer ?? '',
    acceptedAnswers: q.acceptedAnswers ?? [],
    options: isMcq ? (q.options ?? ['', '', '']).slice(0, 3) : [],
    optionDistractorWhy: isMcq
      ? (q.optionDistractorWhy ?? [null, null, null]).slice(0, 3)
      : [],
    optionDistractorCategory: isMcq
      ? (q.optionDistractorCategory ?? [null, null, null]).slice(0, 3)
      : [],
    explanation: q.explanation ?? '',
    transcriptExcerpt: q.transcriptExcerpt ?? '',
    distractorExplanation: q.distractorExplanation ?? '',
    skillTag: q.skillTag ?? '',
    speakerAttitude: q.speakerAttitude ?? '',
    transcriptEvidenceStartMs: q.transcriptEvidenceStartMs ?? '',
    transcriptEvidenceEndMs: q.transcriptEvidenceEndMs ?? '',
  };
}

function diffPatch(initial: FormState, current: FormState, isMcq: boolean): ListeningQuestionPatchBody {
  const patch: ListeningQuestionPatchBody = {};
  if (current.stem !== initial.stem) patch.stem = current.stem;
  if (current.points !== initial.points) patch.points = current.points;
  if (current.correctAnswer !== initial.correctAnswer) patch.correctAnswer = current.correctAnswer;
  if (JSON.stringify(current.acceptedAnswers) !== JSON.stringify(initial.acceptedAnswers)) {
    patch.acceptedAnswers = current.acceptedAnswers;
  }
  if (isMcq && JSON.stringify(current.options) !== JSON.stringify(initial.options)) {
    patch.options = current.options;
  }
  if (isMcq && JSON.stringify(current.optionDistractorWhy) !== JSON.stringify(initial.optionDistractorWhy)) {
    patch.optionDistractorWhy = current.optionDistractorWhy;
  }
  if (isMcq && JSON.stringify(current.optionDistractorCategory) !== JSON.stringify(initial.optionDistractorCategory)) {
    patch.optionDistractorCategory = current.optionDistractorCategory;
  }
  if (current.explanation !== initial.explanation) patch.explanation = current.explanation || null;
  if (current.transcriptExcerpt !== initial.transcriptExcerpt) patch.transcriptExcerpt = current.transcriptExcerpt || null;
  if (current.distractorExplanation !== initial.distractorExplanation) {
    patch.distractorExplanation = current.distractorExplanation || null;
  }
  if (current.skillTag !== initial.skillTag) patch.skillTag = current.skillTag || null;
  if (current.speakerAttitude !== initial.speakerAttitude) {
    patch.speakerAttitude = current.speakerAttitude === '' ? null : current.speakerAttitude;
  }
  if (current.transcriptEvidenceStartMs !== initial.transcriptEvidenceStartMs) {
    patch.transcriptEvidenceStartMs = current.transcriptEvidenceStartMs === '' ? null : current.transcriptEvidenceStartMs;
  }
  if (current.transcriptEvidenceEndMs !== initial.transcriptEvidenceEndMs) {
    patch.transcriptEvidenceEndMs = current.transcriptEvidenceEndMs === '' ? null : current.transcriptEvidenceEndMs;
  }
  return patch;
}

export default function AdminListeningQuestionEditorPage() {
  const params = useParams<{ paperId?: string | string[]; qid?: string | string[] }>();
  const router = useRouter();
  const paperId = firstParam(params?.paperId);
  const questionId = firstParam(params?.qid);
  const { isAuthenticated, role } = useAdminAuth();

  const [load, setLoad] = useState<LoadState>('loading');
  const [doc, setDoc] = useState<ListeningAuthoredQuestionList | null>(null);
  const [target, setTarget] = useState<ListeningAuthoredQuestion | null>(null);
  const [initial, setInitial] = useState<FormState | null>(null);
  const [form, setForm] = useState<FormState | null>(null);
  const [save, setSave] = useState<SaveState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [variantDraft, setVariantDraft] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  // Authors mark the correct option by changing `correctAnswer` to that
  // option's text. This keeps the wire shape (single string) in sync with
  // the radio UI; the per-option distractor metadata stays valid because
  // the form keeps option text in lockstep with `correctAnswer`.

  const refresh = useCallback(async () => {
    if (!paperId || !questionId) return;
    setLoad('loading');
    try {
      const result = await getListeningStructure(paperId);
      const found = result.questions.find((q) => q.id === questionId) ?? null;
      setDoc(result);
      setTarget(found);
      const state = found ? fromQuestion(found) : null;
      setInitial(state);
      setForm(state);
      setLoad('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load question.');
      setLoad('error');
    }
  }, [paperId, questionId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void refresh();
  }, [isAuthenticated, role, refresh]);

  const isMcq = target?.type === 'multiple_choice_3';
  const isPartC = target?.partCode === 'C1' || target?.partCode === 'C2';
  const isPartA = target?.partCode === 'A1' || target?.partCode === 'A2';

  const patch = useMemo(() => {
    if (!initial || !form) return {};
    return diffPatch(initial, form, isMcq ?? false);
  }, [initial, form, isMcq]);

  const hasChanges = Object.keys(patch).length > 0;

  const onSave = useCallback(async () => {
    if (!paperId || !questionId || !hasChanges) return;
    setSave('saving');
    setError(null);
    try {
      const result = await patchListeningQuestion(paperId, questionId, patch);
      const found = result.questions.find((q) => q.id === questionId) ?? null;
      setDoc(result);
      setTarget(found);
      const state = found ? fromQuestion(found) : null;
      setInitial(state);
      setForm(state);
      setSave('saved');
      setToast({ variant: 'success', message: 'Saved. Question version bumped.' });
    } catch (e) {
      setSave('error');
      const msg = e instanceof Error ? e.message : 'Could not save question.';
      setError(msg);
      setToast({ variant: 'error', message: msg });
    }
  }, [paperId, questionId, hasChanges, patch]);

  const setField = useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((current) => (current ? { ...current, [key]: value } : current));
  }, []);

  const setOption = useCallback((index: number, value: string) => {
    setForm((current) => {
      if (!current) return current;
      const options = [...current.options];
      const prevText = options[index];
      options[index] = value;
      // Keep correctAnswer pointing at the same option after rename.
      const correctAnswer = current.correctAnswer === prevText ? value : current.correctAnswer;
      return { ...current, options, correctAnswer };
    });
  }, []);

  const setOptionDistractorWhy = useCallback((index: number, value: string) => {
    setForm((current) => {
      if (!current) return current;
      const arr = [...current.optionDistractorWhy];
      arr[index] = value === '' ? null : value;
      return { ...current, optionDistractorWhy: arr };
    });
  }, []);

  const setOptionDistractorCategory = useCallback((index: number, value: string) => {
    setForm((current) => {
      if (!current) return current;
      const arr = [...current.optionDistractorCategory];
      arr[index] = value === '' ? null : (value as ListeningDistractorCategory);
      return { ...current, optionDistractorCategory: arr };
    });
  }, []);

  const addVariant = useCallback(() => {
    const trimmed = variantDraft.trim();
    if (!trimmed || !form) return;
    if (form.acceptedAnswers.includes(trimmed)) {
      setVariantDraft('');
      return;
    }
    setField('acceptedAnswers', [...form.acceptedAnswers, trimmed]);
    setVariantDraft('');
  }, [variantDraft, form, setField]);

  const removeVariant = useCallback((value: string) => {
    if (!form) return;
    setField('acceptedAnswers', form.acceptedAnswers.filter((v) => v !== value));
  }, [form, setField]);

  const onVariantKey = useCallback((event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      addVariant();
    }
  }, [addVariant]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening question editor">
      <AdminRouteSectionHeader
        icon={<Save className="w-6 h-6" />}
        title="Listening — question editor"
        description={target
          ? `Paper ${paperId}. Editing Q${target.number} (${target.partCode}).`
          : 'Loading question…'}
      />

      <div className="flex flex-wrap items-center gap-3">
        <Button variant="ghost" asChild>
          <Link href={`/admin/content/listening/${paperId}/structure`} className="gap-2">
            <ArrowLeft className="h-4 w-4" />
            Back to structure
          </Link>
        </Button>
        {target && (
          <>
            <Badge variant="info">Q{target.number}</Badge>
            <Badge variant="default">{target.partCode}</Badge>
            <Badge variant="default">{isMcq ? 'MCQ' : 'Gap fill'}</Badge>
          </>
        )}
      </div>

      {load === 'loading' && <Skeleton className="h-96 rounded-2xl" />}
      {load === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}
      {load === 'ready' && !target && (
        <InlineAlert variant="error">
          Question not found on this paper. It may have been removed or renumbered.
        </InlineAlert>
      )}

      {load === 'ready' && target && form && (
        <>
          <AdminRoutePanel title="Stem + answer">
            <div className="grid gap-4">
              <Textarea
                label="Stem"
                rows={3}
                value={form.stem}
                onChange={(e) => setField('stem', e.target.value)}
                placeholder={isMcq ? 'What does the speaker imply about…?' : 'Patient reports pain located in the ____'}
              />

              <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
                <Input
                  label="Points"
                  type="number"
                  min={0}
                  max={9}
                  value={form.points}
                  onChange={(e) => setField('points', Number(e.target.value) || 0)}
                />
                <Input
                  label="Skill tag"
                  value={form.skillTag}
                  onChange={(e) => setField('skillTag', e.target.value)}
                  placeholder="numbers_units"
                />
                {isPartC && (
                  <Select
                    label="Speaker attitude"
                    value={form.speakerAttitude}
                    onChange={(e) => setField('speakerAttitude', e.target.value as ListeningSpeakerAttitude | '')}
                    options={ATTITUDE_OPTIONS}
                  />
                )}
              </div>

              {isMcq ? (
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Options</p>
                  <p className="mt-1 text-xs text-muted">{"Authors mark the correct option via the radio. The correct option's text is mirrored into `correctAnswer`."}</p>
                  <div className="mt-3 grid gap-3">
                    {form.options.map((option, index) => {
                      const letter = String.fromCharCode(65 + index);
                      const isCorrect = form.correctAnswer === option && option !== '';
                      return (
                        <div key={index} className="grid gap-2 rounded-xl border border-border bg-surface p-3">
                          <div className="flex items-start gap-3">
                            <label className="flex items-center gap-2 pt-2">
                              <input
                                type="radio"
                                name="correctOption"
                                value={letter}
                                checked={isCorrect}
                                onChange={() => setField('correctAnswer', option)}
                                aria-label={`Mark option ${letter} as correct`}
                              />
                              <Badge variant={isCorrect ? 'success' : 'default'}>{letter}</Badge>
                            </label>
                            <div className="flex-1">
                              <Textarea
                                aria-label={`Option ${letter} text`}
                                rows={2}
                                value={option}
                                onChange={(e) => setOption(index, e.target.value)}
                              />
                            </div>
                          </div>
                          {!isCorrect && (
                            <div className="grid gap-2 pl-12 md:grid-cols-[1fr_2fr]">
                              <Select
                                label="Distractor category"
                                value={form.optionDistractorCategory[index] ?? ''}
                                onChange={(e) => setOptionDistractorCategory(index, e.target.value)}
                                options={DISTRACTOR_OPTIONS}
                              />
                              <Textarea
                                label="Why wrong (post-submit copy)"
                                rows={2}
                                value={form.optionDistractorWhy[index] ?? ''}
                                onChange={(e) => setOptionDistractorWhy(index, e.target.value)}
                              />
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              ) : (
                <Input
                  label="Canonical answer"
                  value={form.correctAnswer}
                  onChange={(e) => setField('correctAnswer', e.target.value)}
                  placeholder="cholesterol"
                />
              )}

              {isPartA && (
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Accepted variants</p>
                  <p className="mt-1 text-xs text-muted">UK/US spelling, abbreviations, plurals. Keep tight — OET expects exact wording.</p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    {form.acceptedAnswers.map((v) => (
                      <span key={v} className="inline-flex items-center gap-1 rounded-full bg-info/10 px-3 py-1 text-sm text-info">
                        {v}
                        <button
                          type="button"
                          aria-label={`Remove variant ${v}`}
                          onClick={() => removeVariant(v)}
                          className="rounded-full p-0.5 hover:bg-info/20"
                        >
                          <X className="h-3 w-3" aria-hidden="true" />
                        </button>
                      </span>
                    ))}
                  </div>
                  <div className="mt-3 flex items-center gap-2">
                    <Input
                      placeholder="Add variant…"
                      value={variantDraft}
                      onChange={(e) => setVariantDraft(e.target.value)}
                      onKeyDown={onVariantKey}
                    />
                    <Button type="button" variant="outline" onClick={addVariant} disabled={!variantDraft.trim()}>
                      <Plus className="h-4 w-4" />
                      Add
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </AdminRoutePanel>

          <AdminRoutePanel title="Transcript evidence + explanation">
            <div className="grid gap-4">
              <Textarea
                label="Transcript excerpt (verbatim)"
                rows={3}
                value={form.transcriptExcerpt}
                onChange={(e) => setField('transcriptExcerpt', e.target.value)}
              />
              <div className="grid gap-3 md:grid-cols-2">
                <Input
                  label="Evidence start (ms)"
                  type="number"
                  min={0}
                  value={form.transcriptEvidenceStartMs}
                  onChange={(e) => {
                    const raw = e.target.value;
                    setField('transcriptEvidenceStartMs', raw === '' ? '' : Number(raw));
                  }}
                />
                <Input
                  label="Evidence end (ms)"
                  type="number"
                  min={0}
                  value={form.transcriptEvidenceEndMs}
                  onChange={(e) => {
                    const raw = e.target.value;
                    setField('transcriptEvidenceEndMs', raw === '' ? '' : Number(raw));
                  }}
                />
              </div>
              <Textarea
                label="Explanation (markdown)"
                rows={4}
                value={form.explanation}
                onChange={(e) => setField('explanation', e.target.value)}
              />
              <Textarea
                label="Distractor / paraphrase note"
                rows={2}
                value={form.distractorExplanation}
                onChange={(e) => setField('distractorExplanation', e.target.value)}
              />
            </div>
          </AdminRoutePanel>

          <div className="flex items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4">
            <div className="text-sm text-muted">
              {hasChanges ? `${Object.keys(patch).length} unsaved field(s).` : 'No unsaved changes.'}
              {save === 'saving' && ' Saving…'}
              {save === 'saved' && !hasChanges && ' Saved.'}
            </div>
            <div className="flex gap-2">
              <Button
                variant="ghost"
                onClick={() => {
                  setForm(initial);
                  setSave('idle');
                }}
                disabled={!hasChanges || save === 'saving'}
              >
                <Trash2 className="h-4 w-4" />
                Discard
              </Button>
              <Button onClick={onSave} disabled={!hasChanges || save === 'saving'} loading={save === 'saving'}>
                <Save className="h-4 w-4" />
                Save changes
              </Button>
            </div>
          </div>
        </>
      )}

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
