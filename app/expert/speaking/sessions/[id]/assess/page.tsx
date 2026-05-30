'use client';

/**
 * Expert: speaking session assessment screen.
 *
 * Layout:
 * - Top: read-only AI column (so the tutor can use it as reference)
 * - Left: transcript player with timestamped comment composer
 * - Right: criterion rubric form
 * - Bottom: Save Draft + Submit buttons
 *
 * Flow:
 * 1. Fetch dual assessment on mount (AI is expected, tutor draft optional).
 * 2. Hydrate rubric form from existing tutor draft if present.
 * 3. Save draft (POST if none, PATCH if exists).
 * 4. Submit publishes the assessment as final.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, ListChecks, Loader2, Save, Scale, Send } from 'lucide-react';

import { ExpertDashboardShell } from '@/components/layout';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { CriterionRubricForm, EMPTY_RUBRIC_VALUE, type CriterionRubricFormValue } from '@/components/domain/speaking/CriterionRubricForm';
import { DualAssessmentColumn } from '@/components/domain/speaking/DualAssessmentColumn';
import { TranscriptPlayerWithComments, type TranscriptPayload } from '@/components/domain/speaking/TranscriptPlayerWithComments';
import {
  SpeakingAssessmentApiError,
  moderationOpenCase,
  tutorAddTimestampedComment,
  tutorCreateDraft,
  tutorGetDualAssessment,
  tutorGetSessionContext,
  tutorGetSessionRecordingObjectUrl,
  tutorSubmitAssessment,
  tutorUpdateDraft,
  type DualAssessmentResponse,
  type TimestampedComment,
  type TutorAssessment,
} from '@/lib/api/speaking-assessments';

interface SessionContext {
  recordingUrl?: string | null;
  transcript: TranscriptPayload;
  comments: TimestampedComment[];
}

function hydrateFromTutorAssessment(t: TutorAssessment | null): CriterionRubricFormValue {
  if (!t) return EMPTY_RUBRIC_VALUE;
  return {
    intelligibility: t.intelligibility ?? null,
    fluency: t.fluency ?? null,
    appropriateness: t.appropriateness ?? null,
    grammarExpression: t.grammarExpression ?? null,
    relationshipBuilding: t.relationshipBuilding ?? null,
    patientPerspective: t.patientPerspective ?? null,
    structure: t.structure ?? null,
    informationGathering: t.informationGathering ?? null,
    informationGiving: t.informationGiving ?? null,
    overallFeedbackMarkdown: t.overallFeedbackMarkdown ?? '',
    strengths: t.strengths ?? [],
    improvements: t.improvements ?? [],
    recommendedDrills: t.recommendedDrills ?? [],
    recommendedRulebookEntries: t.recommendedRulebookEntries ?? [],
  };
}

function isComplete(v: CriterionRubricFormValue): boolean {
  return (
    v.intelligibility !== null &&
    v.fluency !== null &&
    v.appropriateness !== null &&
    v.grammarExpression !== null &&
    v.relationshipBuilding !== null &&
    v.patientPerspective !== null &&
    v.structure !== null &&
    v.informationGathering !== null &&
    v.informationGiving !== null
  );
}

export default function AssessSpeakingSessionPage() {
  const params = useParams();
  const router = useRouter();
  const rawId = params?.id;
  const sessionId = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  const [data, setData] = useState<DualAssessmentResponse | null>(null);
  const [context, setContext] = useState<SessionContext | null>(null);
  const [recordingObjectUrl, setRecordingObjectUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [rubric, setRubric] = useState<CriterionRubricFormValue>(EMPTY_RUBRIC_VALUE);
  const [draftId, setDraftId] = useState<string | null>(null);
  const [mode, setMode] = useState<'draft' | 'submit'>('draft');
  const [savingDraft, setSavingDraft] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [sendingToModeration, setSendingToModeration] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [comments, setComments] = useState<TimestampedComment[]>([]);

  const load = useCallback(async () => {
    if (!sessionId) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const [response, sessionContext] = await Promise.all([
        tutorGetDualAssessment(sessionId),
        tutorGetSessionContext(sessionId),
      ]);
      setData(response);
      if (response.tutor) {
        setDraftId(response.tutor.assessmentId);
        setRubric(hydrateFromTutorAssessment(response.tutor));
      } else {
        setDraftId(null);
        setRubric(EMPTY_RUBRIC_VALUE);
      }

      let protectedRecordingUrl: string | null = null;
      if (sessionContext.recordingUrl) {
        try {
          protectedRecordingUrl = await tutorGetSessionRecordingObjectUrl(sessionId);
        } catch {
          protectedRecordingUrl = null;
        }
      }
      setRecordingObjectUrl((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return protectedRecordingUrl;
      });
      setContext({
        recordingUrl: protectedRecordingUrl,
        transcript: sessionContext.transcript ?? { segments: [] },
        comments: sessionContext.comments ?? [],
      });
      setComments(sessionContext.comments ?? []);
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to load session.';
      setErrorMsg(msg);
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    return () => {
      if (recordingObjectUrl) URL.revokeObjectURL(recordingObjectUrl);
    };
  }, [recordingObjectUrl]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleAddComment = useCallback(
    async (input: Parameters<typeof tutorAddTimestampedComment>[1]) => {
      const created = await tutorAddTimestampedComment(sessionId, input);
      setComments((prev) => [...prev, created]);
    },
    [sessionId],
  );

  const buildDraftPayload = () => ({
    intelligibility: rubric.intelligibility,
    fluency: rubric.fluency,
    appropriateness: rubric.appropriateness,
    grammarExpression: rubric.grammarExpression,
    relationshipBuilding: rubric.relationshipBuilding,
    patientPerspective: rubric.patientPerspective,
    structure: rubric.structure,
    informationGathering: rubric.informationGathering,
    informationGiving: rubric.informationGiving,
    overallFeedbackMarkdown: rubric.overallFeedbackMarkdown || null,
    strengths: rubric.strengths,
    improvements: rubric.improvements,
    recommendedDrills: rubric.recommendedDrills,
    recommendedRulebookEntries: rubric.recommendedRulebookEntries,
  });

  const handleSaveDraft = async () => {
    setSavingDraft(true);
    try {
      if (draftId) {
        await tutorUpdateDraft(sessionId, draftId, buildDraftPayload());
      } else {
        const created = await tutorCreateDraft(sessionId, buildDraftPayload());
        setDraftId(created.assessmentId);
      }
      setToast({ variant: 'success', message: 'Draft saved.' });
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to save draft.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setSavingDraft(false);
    }
  };

  const handleSubmit = async () => {
    setMode('submit');
    if (!isComplete(rubric)) {
      setToast({ variant: 'error', message: 'Please score all 9 criteria before submitting.' });
      return;
    }
    setSubmitting(true);
    try {
      // Ensure there's a draft to submit.
      let assessmentId = draftId;
      if (!assessmentId) {
        const created = await tutorCreateDraft(sessionId, buildDraftPayload());
        assessmentId = created.assessmentId;
        setDraftId(assessmentId);
      }
      await tutorSubmitAssessment(sessionId, assessmentId, {
        intelligibility: rubric.intelligibility as number,
        fluency: rubric.fluency as number,
        appropriateness: rubric.appropriateness as number,
        grammarExpression: rubric.grammarExpression as number,
        relationshipBuilding: rubric.relationshipBuilding as number,
        patientPerspective: rubric.patientPerspective as number,
        structure: rubric.structure as number,
        informationGathering: rubric.informationGathering as number,
        informationGiving: rubric.informationGiving as number,
        overallFeedbackMarkdown: rubric.overallFeedbackMarkdown || undefined,
        strengths: rubric.strengths,
        improvements: rubric.improvements,
        recommendedDrills: rubric.recommendedDrills,
        recommendedRulebookEntries: rubric.recommendedRulebookEntries,
      });
      // Stash a flash message for the queue page.
      if (typeof window !== 'undefined') {
        window.sessionStorage.setItem('speakingQueueFlash', 'assessment-submitted');
      }
      router.push('/expert/speaking/queue?flash=assessment-submitted');
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to submit assessment.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setSubmitting(false);
    }
  };

  const handleSendToModeration = async () => {
    setSendingToModeration(true);
    try {
      await moderationOpenCase(sessionId, 'tutor_request');
      router.push(`/expert/speaking/moderation/${encodeURIComponent(sessionId)}`);
    } catch (err) {
      const msg =
        err instanceof SpeakingAssessmentApiError
          ? err.message
          : 'Could not open a moderation case. Submit your assessment first.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setSendingToModeration(false);
    }
  };

  const aiAssessment = data?.ai ?? null;

  const allCommentsForPlayer = useMemo(() => comments, [comments]);

  if (loading) {
    return (
      <ExpertDashboardShell pageTitle="Loading assessment…">
        <div className="flex flex-col gap-4">
          <Skeleton className="h-32 w-full" />
          <div className="grid gap-4 md:grid-cols-2">
            <Skeleton className="h-96 w-full" />
            <Skeleton className="h-96 w-full" />
          </div>
        </div>
      </ExpertDashboardShell>
    );
  }

  if (errorMsg) {
    return (
      <ExpertDashboardShell pageTitle="Error">
        <InlineAlert variant="error" title="Failed to load session" action={
          <Button onClick={() => void load()} size="sm" variant="outline">Try again</Button>
        }>
          {errorMsg}
        </InlineAlert>
      </ExpertDashboardShell>
    );
  }

  return (
    <ExpertDashboardShell pageTitle="Assess speaking session" subtitle={`Session ${sessionId.slice(0, 8)}…`}>
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <Button variant="ghost" size="sm" onClick={() => router.push('/expert/speaking/queue')}>
            <ArrowLeft className="mr-1.5 h-3.5 w-3.5" aria-hidden />
            Back to queue
          </Button>
        </div>

        {/* Top: AI reference column (read-only). Pinned full-width on top. */}
        <div className="grid gap-4 md:grid-cols-2">
          <DualAssessmentColumn
            kind="ai"
            title="AI Reference"
            assessment={aiAssessment}
            attribution={
              aiAssessment
                ? {
                    provider: aiAssessment.provider,
                    modelId: aiAssessment.modelId,
                    submittedAt: aiAssessment.generatedAt,
                  }
                : undefined
            }
            placeholderCta={
              <p className="text-sm text-muted">AI assessment is still processing. You may proceed with tutor review.</p>
            }
          />
          <Card padding="md" className="flex flex-col gap-3" aria-label="Reviewer guidance">
            <div className="flex items-center gap-2 text-navy">
              <ListChecks className="h-5 w-5 text-primary" aria-hidden />
              <h3 className="text-base font-bold">Reviewer guidance</h3>
            </div>
            <ul className="ml-4 list-disc space-y-1 text-sm leading-relaxed text-navy">
              <li>Score the 9 criteria independently. Use the AI column for reference, not anchoring.</li>
              <li>Add timestamped comments on weak/strong moments. They appear in the learner&apos;s transcript.</li>
              <li>Save drafts often; submission is final and visible to the learner.</li>
              <li>Strengths and improvements are user-facing, so keep them specific and actionable.</li>
            </ul>
          </Card>
        </div>

        {/* Middle: transcript + rubric (side-by-side on desktop) */}
        <div className="grid gap-4 lg:grid-cols-[3fr_2fr]">
          <TranscriptPlayerWithComments
            recordingUrl={context?.recordingUrl ?? null}
            transcript={context?.transcript ?? { segments: [] }}
            comments={allCommentsForPlayer}
            onAddComment={handleAddComment}
          />
          <Card padding="md" aria-label="Tutor rubric form" className="flex flex-col gap-3">
            <h3 className="text-base font-bold text-navy">Tutor rubric</h3>
            <CriterionRubricForm value={rubric} onChange={setRubric} mode={mode} />
          </Card>
        </div>

        {/* Bottom: action bar */}
        <div className="sticky bottom-4 z-10 flex flex-wrap items-center justify-end gap-2 rounded-2xl border border-border bg-surface/95 p-3 shadow-clinical backdrop-blur">
          <span className="mr-auto text-xs text-muted">
            {draftId ? 'Draft auto-tracked. Save updates the draft.' : 'New tutor assessment.'}
          </span>
          <Button variant="outline" onClick={() => void handleSaveDraft()} loading={savingDraft} disabled={submitting}>
            <Save className="mr-1.5 h-3.5 w-3.5" aria-hidden />
            Save draft
          </Button>
          <Button
            variant="outline"
            onClick={() => void handleSendToModeration()}
            loading={sendingToModeration}
            disabled={submitting || savingDraft}
            title="Flag this submitted assessment for an independent second mark"
          >
            <Scale className="mr-1.5 h-3.5 w-3.5" aria-hidden />
            Send to moderation
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} loading={submitting} disabled={savingDraft}>
            {submitting ? <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" /> : <Send className="mr-1.5 h-3.5 w-3.5" aria-hidden />}
            Submit assessment
          </Button>
        </div>

        {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      </div>
    </ExpertDashboardShell>
  );
}
