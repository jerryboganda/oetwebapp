'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import {
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Tag,
  XCircle,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/form-controls';
import { isApiError } from '@/lib/api';
import {
  getListeningExpertBundle,
  submitListeningExpertFeedback,
} from '@/lib/expert-listening-api';
import type {
  ListeningExpertAnswerItem,
  ListeningExpertBundle,
  SubmitListeningFeedbackRequest,
} from '@/lib/types/expert';

// ── Part groups ────────────────────────────────────────────────────────────

const PART_ORDER = ['A1', 'A2', 'B', 'C1', 'C2'] as const;
type PartCode = (typeof PART_ORDER)[number];

function groupByPart(
  answers: ListeningExpertAnswerItem[],
): Map<string, ListeningExpertAnswerItem[]> {
  const map = new Map<string, ListeningExpertAnswerItem[]>();
  for (const item of answers) {
    const part = item.partCode ?? 'Other';
    if (!map.has(part)) map.set(part, []);
    map.get(part)!.push(item);
  }
  return map;
}

const PART_LABELS: Record<string, string> = {
  A1: 'Part A1: Consultation',
  A2: 'Part A2: Consultation (continued)',
  B: 'Part B: Short Extracts',
  C1: 'Part C1: Case Study',
  C2: 'Part C2: Case Study (continued)',
};

// Human-readable label for a snake_case distractor / attitude tag, e.g.
// "wrong_speaker" → "wrong speaker". Matches the learner review wording.
function humaniseTag(tag: string): string {
  return tag.replace(/_/g, ' ');
}

// ── Score override UI ──────────────────────────────────────────────────────

function ScoreOverrideSection({
  maxRaw,
  override,
  overrideReason,
  onOverrideChange,
  onReasonChange,
}: {
  maxRaw: number;
  override: string;
  overrideReason: string;
  onOverrideChange: (v: string) => void;
  onReasonChange: (v: string) => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <div className="mt-4 rounded-xl border border-amber-200 bg-amber-50">
      <button
        type="button"
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-sm font-semibold text-amber-800"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
      >
        <span>Override Score</span>
        {open ? (
          <ChevronDown className="h-4 w-4 shrink-0" />
        ) : (
          <ChevronRight className="h-4 w-4 shrink-0" />
        )}
      </button>
      {open && (
        <div className="space-y-3 px-4 pb-4">
          <div>
            <label
              htmlFor="score-override"
              className="mb-1 block text-xs font-medium text-amber-900"
            >
              Raw score override (0 – {maxRaw})
            </label>
            <input
              id="score-override"
              type="number"
              min={0}
              max={maxRaw}
              value={override}
              onChange={(e) => onOverrideChange(e.target.value)}
              className="w-32 rounded-lg border border-warning bg-surface px-3 py-1.5 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-warning/40"
            />
          </div>
          <Textarea
            label="Reason for override"
            placeholder="Explain why you are changing the system-calculated score..."
            value={overrideReason}
            onChange={(e) => onReasonChange(e.target.value)}
            rows={2}
          />
        </div>
      )}
    </div>
  );
}

// ── Recommended areas tag input ────────────────────────────────────────────

const SUGGESTED_AREAS = [
  'speaker_attitude',
  'numbers_units',
  'specific_detail',
  'distractor_confusion',
  'paraphrase',
  'spelling',
  'part_a_gap_fill',
  'part_b_mcq',
  'part_c_extended',
];

function RecommendedAreasInput({
  areas,
  onChange,
}: {
  areas: string[];
  onChange: (next: string[]) => void;
}) {
  const [inputVal, setInputVal] = useState('');

  function add(tag: string) {
    const trimmed = tag.trim().toLowerCase().replace(/\s+/g, '_');
    if (!trimmed || areas.includes(trimmed)) return;
    onChange([...areas, trimmed]);
    setInputVal('');
  }

  function remove(tag: string) {
    onChange(areas.filter((a) => a !== tag));
  }

  return (
    <div>
      <p className="mb-1 text-xs font-medium text-navy">Recommended Practice Areas</p>
      <div className="flex flex-wrap gap-1.5 rounded-xl border border-border bg-surface p-2">
        {areas.map((tag) => (
          <span
            key={tag}
            className="flex items-center gap-1 rounded-full border border-primary/30 bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary"
          >
            <Tag className="h-3 w-3" />
            {tag.replace(/_/g, ' ')}
            <button
              type="button"
              onClick={() => remove(tag)}
              className="ml-0.5 text-primary/60 hover:text-danger"
              aria-label={`Remove ${tag}`}
            >
              &times;
            </button>
          </span>
        ))}
        <input
          type="text"
          value={inputVal}
          onChange={(e) => setInputVal(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ',') {
              e.preventDefault();
              add(inputVal);
            }
          }}
          placeholder="Add area, press Enter..."
          className="min-w-[140px] flex-1 bg-transparent text-sm text-navy placeholder:text-muted focus:outline-none"
        />
      </div>
      <div className="mt-1.5 flex flex-wrap gap-1">
        {SUGGESTED_AREAS.filter((a) => !areas.includes(a)).map((a) => (
          <button
            key={a}
            type="button"
            onClick={() => add(a)}
            className="rounded-full border border-border bg-muted px-2 py-0.5 text-xs text-muted hover:border-primary/30 hover:bg-primary/5 hover:text-primary"
          >
            + {a.replace(/_/g, ' ')}
          </button>
        ))}
      </div>
    </div>
  );
}

// ── Metadata card ──────────────────────────────────────────────────────────

function MetadataCard({
  bundle,
  scoreOverride,
  overrideReason,
  onOverrideChange,
  onReasonChange,
}: {
  bundle: ListeningExpertBundle;
  scoreOverride: string;
  overrideReason: string;
  onOverrideChange: (v: string) => void;
  onReasonChange: (v: string) => void;
}) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">
        Attempt
      </h2>
      <p className="mt-2 text-lg font-bold text-navy">
        {bundle.learnerDisplayName}
      </p>
      <p className="text-sm text-muted">{bundle.paperTitle}</p>
      <p className="mt-1 text-xs text-muted">
        Submitted {new Date(bundle.submittedAt).toLocaleDateString(undefined, {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        })}
      </p>

      <div className="mt-4 grid grid-cols-3 gap-3 rounded-xl border border-border bg-muted p-3 text-center text-sm">
        <div>
          <p className="text-xs text-muted">Raw</p>
          <p className="font-bold text-navy">
            {bundle.rawScore}/{bundle.maxRawScore}
          </p>
        </div>
        <div>
          <p className="text-xs text-muted">Scaled</p>
          <p className="font-bold text-navy">{bundle.scaledScore}</p>
        </div>
        <div>
          <p className="text-xs text-muted">Correct</p>
          <p className="font-bold text-navy">
            {bundle.answers.filter((a) => a.isCorrect).length}/
            {bundle.answers.length}
          </p>
        </div>
      </div>

      {bundle.existingFeedback && (
        <div className="mt-4 rounded-xl border border-success/30 bg-success/10 px-3 py-2 text-xs text-success">
          Feedback already submitted on{' '}
          {new Date(bundle.existingFeedback.submittedAt).toLocaleDateString()}
        </div>
      )}

      <ScoreOverrideSection
        maxRaw={bundle.maxRawScore}
        override={scoreOverride}
        overrideReason={overrideReason}
        onOverrideChange={onOverrideChange}
        onReasonChange={onReasonChange}
      />
    </div>
  );
}

// ── Question card ──────────────────────────────────────────────────────────

function QuestionCard({
  item,
  selected,
  onSelect,
}: {
  item: ListeningExpertAnswerItem;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className={`w-full rounded-xl border p-4 text-left transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 ${
        selected
          ? 'border-primary/40 bg-primary/5 shadow-sm'
          : 'border-border bg-surface hover:border-primary/50 hover-primary'
      }`}
      aria-pressed={selected}
    >
      <div className="flex items-start gap-3">
        <div className="mt-0.5 shrink-0">
          {item.isCorrect ? (
            <CheckCircle2 className="h-4 w-4 text-success" />
          ) : (
            <XCircle className="h-4 w-4 text-danger" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-widest text-muted">
              Q{item.questionNumber}
            </span>
            <Badge
              variant={item.isCorrect ? 'success' : 'danger'}
              className="text-[10px]"
            >
              {item.isCorrect ? 'Correct' : 'Incorrect'}
            </Badge>
          </div>
          <p className="mt-1 text-sm font-medium text-navy line-clamp-2">
            {item.stem}
          </p>
          <div className="mt-2 grid grid-cols-2 gap-2 text-xs">
            <div>
              <span className="font-semibold text-muted">Your answer: </span>
              <span
                className={
                  item.isCorrect ? 'text-success' : 'text-danger'
                }
              >
                {item.userAnswer ?? 'N/A'}
              </span>
            </div>
            <div>
              <span className="font-semibold text-muted">Correct: </span>
              <span className="text-navy">{item.correctAnswer}</span>
            </div>
          </div>

          {/* WORK-STREAM 7a — distractor taxonomy + Part C speaker-attitude */}
          {(item.selectedDistractorCategory || item.speakerAttitude) && (
            <div className="mt-2 flex flex-wrap items-center gap-1.5">
              {item.selectedDistractorCategory && (
                <Badge variant="warning" className="text-[10px] capitalize">
                  Trap: {humaniseTag(item.selectedDistractorCategory)}
                </Badge>
              )}
              {item.speakerAttitude && (
                <Badge variant="violet" className="text-[10px] capitalize">
                  Attitude: {humaniseTag(item.speakerAttitude)}
                </Badge>
              )}
            </div>
          )}

          {/* WORK-STREAM 7a — per-option distractor breakdown (Part B/C) */}
          {item.optionAnalysis && item.optionAnalysis.length > 0 && (
            <ul className="mt-2 space-y-1.5">
              {item.optionAnalysis.map((opt) => (
                <li
                  key={opt.key}
                  className={`rounded-lg border px-2 py-1.5 text-xs ${
                    opt.isCorrect
                      ? 'border-success/30 bg-success/5'
                      : 'border-border bg-muted'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-navy">
                      {opt.key}. {opt.text}
                    </span>
                    {opt.isCorrect ? (
                      <Badge variant="success" className="shrink-0 text-[10px]">
                        Correct
                      </Badge>
                    ) : opt.distractorCategory ? (
                      <Badge
                        variant="muted"
                        className="shrink-0 text-[10px] capitalize"
                      >
                        {humaniseTag(opt.distractorCategory)}
                      </Badge>
                    ) : null}
                  </div>
                  {!opt.isCorrect && opt.whyWrong && (
                    <p className="mt-1 leading-5 text-muted">{opt.whyWrong}</p>
                  )}
                </li>
              ))}
            </ul>
          )}

          {item.transcriptEvidence && (
            <blockquote className="mt-2 rounded-lg bg-info/5 px-2 py-1 text-xs italic text-info">
              {item.transcriptEvidence}
            </blockquote>
          )}
          {item.existingComment && (
            <p className="mt-2 rounded-lg bg-primary/5 px-2 py-1 text-xs text-primary">
              {item.existingComment}
            </p>
          )}
        </div>
      </div>
    </button>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────

export default function ListeningReviewWorkspacePage() {
  const params = useParams();
  const router = useRouter();
  const rawId = params?.attemptId;
  const attemptId = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  const [bundle, setBundle] = useState<ListeningExpertBundle | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [selectedQuestion, setSelectedQuestion] =
    useState<ListeningExpertAnswerItem | null>(null);
  const [perQuestionComments, setPerQuestionComments] = useState<
    Record<number, string>
  >({});
  const [overallFeedback, setOverallFeedback] = useState('');
  const [recommendedAreas, setRecommendedAreas] = useState<string[]>([]);
  const [scoreOverride, setScoreOverride] = useState('');
  const [overrideReason, setOverrideReason] = useState('');

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [toast, setToast] = useState<{
    variant: 'success' | 'error';
    message: string;
  } | null>(null);

  // Load bundle
  useEffect(() => {
    if (!attemptId) return;
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    getListeningExpertBundle(attemptId)
      .then((data) => {
        if (cancelled) return;
        setBundle(data);
        if (data.existingFeedback) {
          setOverallFeedback(
            data.existingFeedback.overallFeedbackMarkdown ?? '',
          );
          if (data.existingFeedback.recommendedAreasJson) {
            try {
              const areas = JSON.parse(
                data.existingFeedback.recommendedAreasJson,
              ) as string[];
              setRecommendedAreas(Array.isArray(areas) ? areas : []);
            } catch {
              /* ignore */
            }
          }
          if (data.existingFeedback.rawScoreOverride != null) {
            setScoreOverride(
              String(data.existingFeedback.rawScoreOverride),
            );
          }
          setOverrideReason(
            data.existingFeedback.scoreOverrideReason ?? '',
          );
          if (data.existingFeedback.perQuestionFeedbackJson) {
            try {
              const pq = JSON.parse(
                data.existingFeedback.perQuestionFeedbackJson,
              ) as Array<{ questionNumber: number; comment: string }>;
              const map: Record<number, string> = {};
              if (Array.isArray(pq)) {
                pq.forEach(({ questionNumber, comment }) => {
                  map[questionNumber] = comment;
                });
              }
              setPerQuestionComments(map);
            } catch {
              /* ignore */
            }
          }
        }
        // Seed per-question comments from existingComment on each answer
        data.answers.forEach((a) => {
          if (a.existingComment) {
            setPerQuestionComments((prev) => ({
              ...prev,
              [a.questionNumber]: a.existingComment!,
            }));
          }
        });
      })
      .catch((err) => {
        if (!cancelled) {
          setLoadError(
            isApiError(err)
              ? err.userMessage
              : 'Failed to load attempt bundle.',
          );
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [attemptId]);

  const handleSubmit = useCallback(async () => {
    if (!overallFeedback.trim()) {
      setToast({
        variant: 'error',
        message: 'Please write overall feedback before submitting.',
      });
      return;
    }

    const perQuestionFeedback = Object.entries(perQuestionComments)
      .filter(([, comment]) => comment.trim())
      .map(([qn, comment]) => ({
        questionNumber: Number(qn),
        comment: comment.trim(),
      }));

    const req: SubmitListeningFeedbackRequest = {
      overallFeedback: overallFeedback.trim(),
      ...(perQuestionFeedback.length > 0 ? { perQuestionFeedback } : {}),
      ...(recommendedAreas.length > 0 ? { recommendedAreas } : {}),
      ...(scoreOverride !== '' && Number.isFinite(Number(scoreOverride))
        ? {
            rawScoreOverride: Number(scoreOverride),
            scoreOverrideReason: overrideReason.trim() || undefined,
          }
        : {}),
    };

    setIsSubmitting(true);
    try {
      await submitListeningExpertFeedback(attemptId, req);
      setToast({ variant: 'success', message: 'Feedback submitted successfully.' });
      setTimeout(() => router.push('/expert/listening'), 1200);
    } catch (err) {
      setToast({
        variant: 'error',
        message: isApiError(err)
          ? err.userMessage
          : 'Failed to submit feedback.',
      });
    } finally {
      setIsSubmitting(false);
    }
  }, [
    attemptId,
    overallFeedback,
    overrideReason,
    perQuestionComments,
    recommendedAreas,
    router,
    scoreOverride,
  ]);

  // ── Render: loading ────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="min-h-screen bg-background-light p-6">
        <div className="mx-auto grid max-w-7xl gap-6 lg:grid-cols-[280px_1fr_360px]">
          <Skeleton className="h-64 rounded-2xl" />
          <div className="space-y-4">
            {[1, 2, 3, 4].map((i) => (
              <Skeleton key={i} className="h-32 rounded-2xl" />
            ))}
          </div>
          <Skeleton className="h-96 rounded-2xl" />
        </div>
      </div>
    );
  }

  if (loadError || !bundle) {
    return (
      <div className="mx-auto max-w-xl p-8">
        <InlineAlert variant="error">
          {loadError ?? 'Attempt not found.'}
        </InlineAlert>
        <div className="mt-4 flex justify-end">
          <Button onClick={() => router.push('/expert/listening')}>
            Back to Listening Attempts
          </Button>
        </div>
      </div>
    );
  }

  const grouped = groupByPart(bundle.answers);
  const partsInOrder = (
    PART_ORDER.filter((p) => grouped.has(p)) as string[]
  ).concat(
    [...grouped.keys()].filter(
      (p) => !PART_ORDER.includes(p as PartCode),
    ),
  );

  return (
    <div className="min-h-screen bg-background-light">
      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}

      {/* Header */}
      <div className="sticky top-0 z-10 border-b border-border bg-surface px-6 py-3">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-4">
          <div>
            <h1 className="text-base font-semibold text-navy">
              Listening Feedback: {bundle.paperTitle}
            </h1>
            <p className="text-xs text-muted">
              {bundle.learnerDisplayName} · {bundle.rawScore}/{bundle.maxRawScore} raw · {bundle.scaledScore} scaled
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => router.push('/expert/listening')}
            >
              Back
            </Button>
            <Button
              size="sm"
              onClick={() => void handleSubmit()}
              disabled={isSubmitting}
              loading={isSubmitting}
            >
              {isSubmitting ? 'Submitting...' : 'Submit Feedback'}
            </Button>
          </div>
        </div>
      </div>

      {/* 3-column layout */}
      <div className="mx-auto grid max-w-7xl gap-6 p-6 lg:grid-cols-[280px_1fr_360px]">
        {/* Left — metadata */}
        <aside className="space-y-4">
          <MetadataCard
            bundle={bundle}
            scoreOverride={scoreOverride}
            overrideReason={overrideReason}
            onOverrideChange={setScoreOverride}
            onReasonChange={setOverrideReason}
          />
        </aside>

        {/* Center — question list */}
        <main className="space-y-6">
          {partsInOrder.map((part) => {
            const items = grouped.get(part) ?? [];
            return (
              <section key={part}>
                <h3 className="mb-3 text-xs font-bold uppercase tracking-widest text-muted">
                  {PART_LABELS[part] ?? `Part ${part}`}
                </h3>
                <div className="space-y-3">
                  {items.map((item) => (
                    <QuestionCard
                      key={item.questionNumber}
                      item={item}
                      selected={
                        selectedQuestion?.questionNumber ===
                        item.questionNumber
                      }
                      onSelect={() =>
                        setSelectedQuestion((prev) =>
                          prev?.questionNumber === item.questionNumber
                            ? null
                            : item,
                        )
                      }
                    />
                  ))}
                </div>
              </section>
            );
          })}
        </main>

        {/* Right — feedback panel */}
        <aside className="space-y-4">
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-semibold text-navy">
              Feedback Panel
            </h2>

            {/* Per-question comment — shown when a question is selected */}
            {selectedQuestion && (
              <div className="mb-4 rounded-xl border border-primary/20 bg-primary/5 p-4">
                <p className="mb-2 text-xs font-semibold text-primary">
                  Comment, Q{selectedQuestion.questionNumber}:{' '}
                  <span className="font-normal text-navy">
                    {selectedQuestion.stem.slice(0, 60)}
                    {selectedQuestion.stem.length > 60 ? '...' : ''}
                  </span>
                </p>
                <textarea
                  className="w-full rounded-lg border border-primary/20 bg-surface px-3 py-2 text-sm text-navy placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-primary/30"
                  rows={3}
                  placeholder="Add a comment for this question..."
                  value={
                    perQuestionComments[selectedQuestion.questionNumber] ?? ''
                  }
                  onChange={(e) =>
                    setPerQuestionComments((prev) => ({
                      ...prev,
                      [selectedQuestion.questionNumber]: e.target.value,
                    }))
                  }
                  aria-label={`Comment for question ${selectedQuestion.questionNumber}`}
                />
                <p className="mt-1 text-right text-xs text-muted">
                  {Object.values(perQuestionComments).filter((c) => c.trim())
                    .length}{' '}
                  question(s) annotated
                </p>
              </div>
            )}

            {!selectedQuestion && (
              <p className="mb-4 rounded-xl border border-dashed border-border p-3 text-xs text-muted">
                Click a question in the centre panel to add a per-question
                comment here.
              </p>
            )}

            {/* Overall feedback */}
            <Textarea
              label="Overall Coaching Feedback"
              placeholder="Write overall coaching feedback... (supports Markdown)"
              value={overallFeedback}
              onChange={(e) => setOverallFeedback(e.target.value)}
              rows={7}
              aria-label="Overall coaching feedback"
            />
            <p className="mt-1 text-right text-xs text-muted">
              {overallFeedback.length} characters
            </p>

            {/* Recommended areas */}
            <div className="mt-4">
              <RecommendedAreasInput
                areas={recommendedAreas}
                onChange={setRecommendedAreas}
              />
            </div>

            {/* Submit */}
            <Button
              className="mt-6 w-full"
              onClick={() => void handleSubmit()}
              disabled={isSubmitting || !overallFeedback.trim()}
              loading={isSubmitting}
            >
              {isSubmitting ? 'Submitting...' : 'Submit Feedback'}
            </Button>
          </div>
        </aside>
      </div>
    </div>
  );
}
