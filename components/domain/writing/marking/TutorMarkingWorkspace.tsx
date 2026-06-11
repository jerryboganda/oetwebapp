'use client';

/**
 * TutorMarkingWorkspace — the shared composer behind both the tutor review
 * screen (`/tutor/writing/reviews/[submissionId]`) and the expert variant
 * (`/expert/review/writing/[reviewRequestId]`). Everything is driven from
 * `getTutorMarkingContext(submissionId)` (spec §14.2).
 *
 * Layout:
 *  - Left pane: case notes + writing task (sections, prompt, recipient, fixed
 *    instructions, word guide) above the response context, plus a collapsible
 *    tutor-only Model Answer panel.
 *  - Right pane: the student's response with the span-annotation tool, the
 *    rubric, the AI pre-analysis, the content checklist, per-criterion +
 *    overall feedback, and the double-marking / moderation surface.
 *
 * The page shells (TutorRouteWorkspace / hero, ExpertRouteWorkspace) are owned
 * by the two route files; this component renders the body between them and is
 * shell-agnostic so it can be embedded in either.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ClipboardCheck,
  FileText,
  Info,
  Mail,
  NotebookText,
  ShieldCheck,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import {
  createWritingAnnotation,
  deleteWritingAnnotation,
  finalizeWritingModeration,
  getTutorMarkingContext,
  getWritingModeration,
  submitWritingTutorReview,
  type WritingAnnotationCreatePayload,
} from '@/lib/writing/exam-api';
import type {
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingFeedbackAnnotationDto,
  WritingModerationDto,
  WritingTutorMarkingContextDto,
} from '@/lib/writing/types';
import { WritingStimulusViewer } from '@/components/domain/writing/WritingStimulusViewer';
import { AnnotationLayer } from './AnnotationLayer';
import { RubricPanel } from './RubricPanel';
import { AiPreAnalysisPanel } from './AiPreAnalysisPanel';
import { ModerationPanel } from './ModerationPanel';
import { FeedbackTemplateMenu } from './FeedbackTemplateMenu';
import { OverallVoiceNote } from './OverallVoiceNote';
import {
  CRITERION_CODES,
  CRITERION_FEEDBACK_TEMPLATES,
  CRITERION_LABEL,
  EMPTY_COMMENT_DRAFT,
  EMPTY_SCORE_DRAFT,
  draftToScoreOverride,
  scoresToDraft,
  type ScoreDraft,
} from './shared';

export interface TutorMarkingWorkspaceProps {
  submissionId: string;
  /** Persona acting on the screen (affects copy + moderation rights). */
  variant: 'tutor' | 'expert';
  /** Where to go after a successful submit (used when onComplete is absent). */
  onCompleteHref?: string;
  /** Called instead of internal navigation when provided. */
  onComplete?: (result: { moderation: WritingModerationDto | null }) => void;
}

function gradeToScores(context: WritingTutorMarkingContextDto): WritingCriteriaScoresDto | null {
  const g = context.aiGrade;
  if (!g) return null;
  return {
    c1: g.c1Purpose,
    c2: g.c2Content,
    c3: g.c3Conciseness,
    c4: g.c4Genre,
    c5: g.c5Organisation,
    c6: g.c6Language,
  };
}

export function TutorMarkingWorkspace({
  submissionId,
  variant,
  onCompleteHref,
  onComplete,
}: TutorMarkingWorkspaceProps) {
  const [context, setContext] = useState<WritingTutorMarkingContextDto | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // Editable marking state
  const [annotations, setAnnotations] = useState<WritingFeedbackAnnotationDto[]>([]);
  const [scoreDraft, setScoreDraft] = useState<ScoreDraft>(EMPTY_SCORE_DRAFT);
  const [comments, setComments] = useState<Record<WritingCriterionCode, string>>(EMPTY_COMMENT_DRAFT);
  const [freeText, setFreeText] = useState('');
  const [acceptedAi, setAcceptedAi] = useState(false);

  // Moderation (senior) state
  const [moderation, setModeration] = useState<WritingModerationDto | null>(null);
  const [finalDraft, setFinalDraft] = useState<ScoreDraft>(EMPTY_SCORE_DRAFT);
  const [finalNote, setFinalNote] = useState('');
  const [finalizing, setFinalizing] = useState(false);

  // UX
  const [submitting, setSubmitting] = useState(false);
  const [annotationBusy, setAnnotationBusy] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const aiSuggestion = useMemo(() => (context ? gradeToScores(context) : null), [context]);
  // Fallback suggestion source: the AI grade, else the pre-assessment bands.
  const suggestionSource = aiSuggestion ?? context?.preAssessment.estimatedBands ?? null;

  // The senior moderation editor is available to whichever persona is acting as
  // the senior marker for this submission (spec §14 — "user is acting as senior").
  const canModerate = context?.markerSequence === 'senior';

  // ── Load context ────────────────────────────────────────────────────────────
  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    getTutorMarkingContext(submissionId)
      .then((ctx) => {
        if (cancelled) return;
        setContext(ctx);
        setAnnotations(ctx.annotations ?? []);
        setModeration(ctx.moderation ?? null);

        // Pre-fill editable state from any existing in-progress review.
        const review = ctx.existingReview;
        if (review?.scoreOverride) setScoreDraft(scoresToDraft(review.scoreOverride));
        if (review?.perCriterionComments) {
          setComments({ ...EMPTY_COMMENT_DRAFT, ...review.perCriterionComments });
        }
        if (review?.freeTextFeedback) setFreeText(review.freeTextFeedback);

        // Seed the senior final-score editor with the most authoritative score
        // available so the moderator starts from a sensible default.
        const seed =
          ctx.moderation?.finalScore ??
          ctx.moderation?.secondScore ??
          ctx.moderation?.firstScore ??
          null;
        if (seed) setFinalDraft(scoresToDraft(seed));
        if (ctx.moderation?.finalDecisionNote) setFinalNote(ctx.moderation.finalDecisionNote);
      })
      .catch((err) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Could not load the marking context.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId]);

  // ── Annotations ───────────────────────────────────────────────────────────────
  const handleCreateAnnotation = useCallback(
    async (payload: WritingAnnotationCreatePayload) => {
      setAnnotationBusy(true);
      try {
        const created = await createWritingAnnotation(submissionId, payload);
        setAnnotations((prev) => [...prev, created]);
      } catch (err) {
        setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Could not save annotation.' });
        throw err;
      } finally {
        setAnnotationBusy(false);
      }
    },
    [submissionId],
  );

  const handleDeleteAnnotation = useCallback(
    async (annotationId: string) => {
      // Optimistic removal with rollback on failure.
      const prev = annotations;
      setAnnotations((cur) => cur.filter((a) => a.id !== annotationId));
      try {
        await deleteWritingAnnotation(submissionId, annotationId);
      } catch (err) {
        setAnnotations(prev);
        setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Could not delete annotation.' });
      }
    },
    [submissionId, annotations],
  );

  // ── AI pre-assessment confirm / reject ──────────────────────────────────────
  const applyAiSuggestion = useCallback(() => {
    if (!context) return;
    const bands = aiSuggestion ?? context.preAssessment.estimatedBands;
    setScoreDraft(scoresToDraft(bands));
    const suggested = context.preAssessment.suggestedCriterionFeedback;
    setComments((cur) => {
      const next = { ...cur };
      for (const c of CRITERION_CODES) {
        const s = suggested[c];
        if (s && !next[c].trim()) next[c] = s;
      }
      return next;
    });
    setAcceptedAi(true);
    setToast({ variant: 'success', message: 'AI suggestion applied. Review and adjust before submitting.' });
  }, [context, aiSuggestion]);

  const rejectAiSuggestion = useCallback(() => {
    setAcceptedAi(false);
  }, []);

  // ── Comment helpers ─────────────────────────────────────────────────────────
  const insertTemplate = useCallback((code: WritingCriterionCode, snippet: string) => {
    setComments((cur) => {
      const existing = cur[code].trim();
      return { ...cur, [code]: existing ? `${existing}\n${snippet}` : snippet };
    });
  }, []);

  // ── Submit review ────────────────────────────────────────────────────────────
  const handleSubmit = useCallback(async () => {
    if (!context) return;
    setSubmitting(true);
    setToast(null);
    try {
      // Normal writing: the tutor's qualitative feedback is the voice note only —
      // never persist written text or per-criterion comments. Mocks keep both.
      const isMockSubmit = context.submission.mode === 'mock';
      const cleanedComments: Partial<Record<WritingCriterionCode, string>> = {};
      if (isMockSubmit) {
        for (const c of CRITERION_CODES) {
          if (comments[c].trim()) cleanedComments[c] = comments[c].trim();
        }
      }
      const scoreOverride = draftToScoreOverride(scoreDraft);
      const result = await submitWritingTutorReview(submissionId, {
        freeTextFeedback: isMockSubmit ? (freeText.trim() || null) : null,
        perCriterionComments: isMockSubmit && Object.keys(cleanedComments).length > 0 ? cleanedComments : undefined,
        scoreOverride: Object.keys(scoreOverride).length > 0 ? scoreOverride : undefined,
        markerSequence: context.markerSequence,
        acceptedAiPreAssessment: acceptedAi,
      });
      setModeration(result.moderation ?? null);
      setToast({ variant: 'success', message: 'Review submitted.' });
      if (onComplete) {
        onComplete({ moderation: result.moderation ?? null });
      } else if (onCompleteHref) {
        window.setTimeout(() => {
          window.location.href = onCompleteHref;
        }, 700);
      }
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Submission failed.' });
    } finally {
      setSubmitting(false);
    }
  }, [context, submissionId, comments, scoreDraft, freeText, acceptedAi, onComplete, onCompleteHref]);

  // ── Finalize moderation (senior) ────────────────────────────────────────────
  const handleFinalize = useCallback(async () => {
    setFinalizing(true);
    setToast(null);
    try {
      const finalScore = draftToScoreOverride(finalDraft);
      await finalizeWritingModeration(submissionId, {
        finalScore,
        finalDecisionNote: finalNote.trim(),
      });
      // Refresh authoritative moderation state.
      const refreshed = await getWritingModeration(submissionId);
      setModeration(refreshed);
      setToast({ variant: 'success', message: 'Moderation finalized.' });
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Could not finalize moderation.' });
    } finally {
      setFinalizing(false);
    }
  }, [submissionId, finalDraft, finalNote]);

  // ── Render ───────────────────────────────────────────────────────────────────
  if (loading) {
    return (
      <Card padding="lg" className="text-sm text-muted">
        Loading marking context…
      </Card>
    );
  }

  if (loadError || !context) {
    return <InlineAlert variant="error">{loadError ?? 'Marking context unavailable.'}</InlineAlert>;
  }

  const { task, submission, preAssessment } = context;
  // Writing feedback rule: mocks get the full WRITTEN toolkit (annotations,
  // per-criterion comments, overall text) + a voice note, and ZERO AI. Normal
  // writing keeps AI + the rubric score, but the tutor's qualitative feedback is
  // a voice note only — written text channels are hidden.
  const isMock = submission.mode === 'mock';
  const liveScores = draftToScoreOverride(scoreDraft);
  const radarScores: WritingCriteriaScoresDto = {
    c1: liveScores.c1 ?? suggestionSource?.c1 ?? 0,
    c2: liveScores.c2 ?? suggestionSource?.c2 ?? 0,
    c3: liveScores.c3 ?? suggestionSource?.c3 ?? 0,
    c4: liveScores.c4 ?? suggestionSource?.c4 ?? 0,
    c5: liveScores.c5 ?? suggestionSource?.c5 ?? 0,
    c6: liveScores.c6 ?? suggestionSource?.c6 ?? 0,
  };

  const wordGuide = `${task.wordGuideMin}–${task.wordGuideMax} words`;

  return (
    <div className="space-y-4">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      {context.existingReview?.status === 'submitted' ? (
        <InlineAlert variant="info" title="Already submitted">
          A review has already been submitted for this response. Changes here will update your marking.
        </InlineAlert>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
        {/* ── LEFT PANE: case notes + task + model answer ── */}
        <aside className="space-y-3" aria-label="Task and case notes">
          <Card padding="md">
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-muted">
                  <FileText className="h-3.5 w-3.5" aria-hidden="true" /> Task
                </p>
                <h2 className="mt-1 text-base font-bold text-navy">{task.title}</h2>
              </div>
              <Badge variant="muted" size="sm">{task.letterType}</Badge>
            </div>
            <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
              <Badge variant="default" size="sm">{task.profession}</Badge>
              <Badge variant="default" size="sm">Difficulty {task.difficulty}</Badge>
              <Badge variant="default" size="sm">{wordGuide}</Badge>
            </div>
            {task.writerRole ? (
              <p className="mt-2 text-xs text-muted">
                Writing as: <span className="text-navy">{task.writerRole}</span>
                {task.todayDate ? ` · ${task.todayDate}` : ''}
              </p>
            ) : null}
          </Card>

          {/* Writing task instruction + recipient + fixed instructions */}
          <Card padding="md">
            <p className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-muted">
              <Mail className="h-3.5 w-3.5" aria-hidden="true" /> Writing task
            </p>
            {task.taskPromptMarkdown ? (
              <p className="mt-2 whitespace-pre-wrap text-sm text-navy">{task.taskPromptMarkdown}</p>
            ) : null}
            {task.fixedInstructions.length > 0 ? (
              <div className="mt-2">
                <p className="text-[11px] font-bold uppercase tracking-wider text-muted">In your answer</p>
                <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm text-navy">
                  {task.fixedInstructions.map((ins, i) => <li key={i}>{ins}</li>)}
                </ul>
              </div>
            ) : null}
            {task.expectedPurpose || task.expectedAction ? (
              <div className="mt-2 rounded-lg border border-primary/20 bg-lavender/30 p-2.5 text-xs text-navy">
                {task.expectedPurpose ? (
                  <p><span className="font-semibold">Expected purpose:</span> {task.expectedPurpose}</p>
                ) : null}
                {task.expectedAction ? (
                  <p className="mt-0.5"><span className="font-semibold">Expected action:</span> {task.expectedAction}</p>
                ) : null}
              </div>
            ) : null}
          </Card>

          {/* Case notes — real question-paper PDF when available. */}
          {task.stimulusPdfDownloadPath ? (
            <Card padding="md">
              <p className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-muted">
                <NotebookText className="h-3.5 w-3.5" aria-hidden="true" /> Case notes
              </p>
              <div className="mt-2 h-[60vh] overflow-hidden rounded-lg border border-border">
                <WritingStimulusViewer
                  downloadPath={task.stimulusPdfDownloadPath}
                  title="Question paper"
                  className="h-full"
                />
              </div>
            </Card>
          ) : null}
        </aside>

        {/* ── RIGHT PANE: response + marking ── */}
        <div className="space-y-4">
          {/* Response + annotations */}
          <Card padding="md">
            <div className="mb-2 flex items-center justify-between gap-2">
              <h3 className="flex items-center gap-1.5 text-sm font-bold text-navy">
                <ClipboardCheck className="h-4 w-4 text-primary" aria-hidden="true" /> Student response
              </h3>
              <span className="text-xs text-muted">
                {submission.wordCount} words · {submission.mode}
              </span>
            </div>
            {isMock ? (
              <AnnotationLayer
                responseText={submission.letterContent}
                annotations={annotations}
                onCreate={handleCreateAnnotation}
                onDelete={handleDeleteAnnotation}
                busy={annotationBusy}
              />
            ) : (
              // Normal writing: tutor reads the response to score it, but written
              // span annotations are not a feedback channel here (voice note only).
              <p className="whitespace-pre-wrap text-sm leading-relaxed text-foreground">
                {submission.letterContent}
              </p>
            )}
          </Card>

          {/* AI pre-analysis — hidden for mocks (zero AI on mock marking). */}
          {!isMock ? (
            <AiPreAnalysisPanel
              preAssessment={preAssessment}
              accepted={acceptedAi}
              onApply={applyAiSuggestion}
              onReject={rejectAiSuggestion}
            />
          ) : null}

          {/* Rubric + radar */}
          <Card padding="md">
            <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
              <RubricPanel draft={scoreDraft} onChange={setScoreDraft} aiSuggestion={suggestionSource} />
              <div>
                <p className="mb-1 text-xs font-bold uppercase tracking-wider text-muted">Live profile</p>
                <CriteriaRadar scores={radarScores} />
              </div>
            </div>
          </Card>

          {/* Per-criterion comments — mock only (written feedback channel). */}
          {isMock ? (
          <Card padding="md">
            <h3 className="text-sm font-bold text-navy">Per-criterion feedback</h3>
            <ul className="mt-2 space-y-2">
              {CRITERION_CODES.map((c) => (
                <li key={c} className="rounded-xl border border-border bg-background-light p-3">
                  <div className="flex items-center justify-between gap-2">
                    <label htmlFor={`comment-${c}`} className="text-sm font-bold text-navy">{CRITERION_LABEL[c]}</label>
                    <FeedbackTemplateMenu
                      templates={CRITERION_FEEDBACK_TEMPLATES[c]}
                      onInsert={(snippet) => insertTemplate(c, snippet)}
                    />
                  </div>
                  <textarea
                    id={`comment-${c}`}
                    rows={2}
                    value={comments[c]}
                    onChange={(e) => setComments((cur) => ({ ...cur, [c]: e.target.value }))}
                    placeholder={`Comment on ${CRITERION_LABEL[c].toLowerCase()}…`}
                    className="mt-2 w-full rounded-lg border border-border bg-surface p-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                  />
                </li>
              ))}
            </ul>
          </Card>
          ) : null}

          {/* Overall feedback — mock only (written feedback channel). */}
          {isMock ? (
          <Card padding="md">
            <label htmlFor="overall-feedback" className="text-sm font-bold text-navy">Overall feedback</label>
            <p className="text-xs text-muted">Summarise strengths and the top priorities for the learner.</p>
            <textarea
              id="overall-feedback"
              rows={5}
              value={freeText}
              onChange={(e) => setFreeText(e.target.value)}
              placeholder="Overall feedback for the learner…"
              className="mt-2 w-full rounded-lg border border-border bg-surface p-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
            />
            <p className="mt-1 text-right text-xs text-muted">{freeText.length} characters</p>
          </Card>
          ) : null}

          {/* Voice note — the tutor's spoken feedback, in BOTH mock and normal. */}
          <OverallVoiceNote submissionId={submissionId} existingVoiceNote={context.voiceNote} />

          {/* Double-marking / moderation */}
          <ModerationPanel
            moderation={moderation}
            markerSequence={context.markerSequence}
            canModerate={canModerate}
            finalDraft={finalDraft}
            onFinalDraftChange={setFinalDraft}
            finalNote={finalNote}
            onFinalNoteChange={setFinalNote}
            onFinalize={handleFinalize}
            finalizing={finalizing}
          />

          {/* Submit bar */}
          <div className="sticky bottom-0 z-10 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface/95 p-3 shadow-sm backdrop-blur supports-[backdrop-filter]:bg-surface/80">
            <p className="flex items-center gap-1.5 text-xs text-muted">
              {variant === 'tutor' ? (
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
              ) : (
                <Info className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              {context.markerSequence === 'second'
                ? 'Submitting as the second marker may trigger moderation if scores diverge.'
                : 'Your scores and feedback are advisory until finalized.'}
            </p>
            <div className="flex items-center gap-2">
              {onCompleteHref ? (
                <Button
                  variant="outline"
                  onClick={() => {
                    window.location.href = onCompleteHref;
                  }}
                >
                  Cancel
                </Button>
              ) : null}
              <Button onClick={() => void handleSubmit()} loading={submitting}>
                <ClipboardCheck className="h-4 w-4" aria-hidden="true" />
                Submit review
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
