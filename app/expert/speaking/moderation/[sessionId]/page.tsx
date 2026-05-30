'use client';

/**
 * Expert: speaking moderation case detail (§15.4 / §15.5).
 *
 * Depending on case status this surface offers the acting expert exactly one
 * action (all separation-of-duties rules are enforced server-side):
 *   - pending_second     → submit a second independent mark.
 *   - pending_moderation → reconcile a final score, or request a reattempt.
 *   - finalized / reattempt_requested → read-only summary.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, CheckCircle2, RotateCcw, Scale } from 'lucide-react';

import { ExpertDashboardShell } from '@/components/layout';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  CriterionRubricForm,
  EMPTY_RUBRIC_VALUE,
  type CriterionRubricFormValue,
} from '@/components/domain/speaking/CriterionRubricForm';
import {
  CLINICAL_CRITERIA,
  CRITERION_LABEL,
  LINGUISTIC_CRITERIA,
  moderationFinalize,
  moderationGetCase,
  moderationStatusLabel,
  moderationSubmitSecondMark,
  SpeakingAssessmentApiError,
  type SpeakingCriterionCode,
  type SpeakingCriterionScorePayload,
  type SpeakingMarkInput,
  type SpeakingModerationCase,
} from '@/lib/api/speaking-assessments';

const ALL_CRITERIA: SpeakingCriterionCode[] = [...LINGUISTIC_CRITERIA, ...CLINICAL_CRITERIA];

function toMarkInput(value: CriterionRubricFormValue): SpeakingMarkInput | { error: string } {
  const missing = ALL_CRITERIA.filter((c) => value[c] == null);
  if (missing.length > 0) {
    return { error: `Please score all 9 criteria before submitting (${missing.length} remaining).` };
  }
  return {
    intelligibility: value.intelligibility as number,
    fluency: value.fluency as number,
    appropriateness: value.appropriateness as number,
    grammarExpression: value.grammarExpression as number,
    relationshipBuilding: value.relationshipBuilding as number,
    patientPerspective: value.patientPerspective as number,
    structure: value.structure as number,
    informationGathering: value.informationGathering as number,
    informationGiving: value.informationGiving as number,
    overallFeedbackMarkdown: value.overallFeedbackMarkdown || undefined,
    strengths: value.strengths.length ? value.strengths : undefined,
    improvements: value.improvements.length ? value.improvements : undefined,
    recommendedDrills: value.recommendedDrills.length ? value.recommendedDrills : undefined,
  };
}

function ScoreSummary({ title, score }: { title: string; score?: SpeakingCriterionScorePayload | null }) {
  if (!score) {
    return (
      <Card className="p-4">
        <h3 className="text-sm font-semibold">{title}</h3>
        <p className="mt-2 text-sm text-muted-foreground">Not submitted yet.</p>
      </Card>
    );
  }
  return (
    <Card className="p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">{title}</h3>
        <Badge variant="info">{score.estimatedScaledScore}/500</Badge>
      </div>
      <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-2">
        {ALL_CRITERIA.map((c) => (
          <div key={c} className="flex items-center justify-between gap-2">
            <dt className="text-muted-foreground">{CRITERION_LABEL[c]}</dt>
            <dd className="font-medium">{score[c]}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

export default function SpeakingModerationCasePage() {
  const params = useParams();
  const router = useRouter();
  const sessionId = useMemo(() => {
    const raw = params?.sessionId;
    return Array.isArray(raw) ? raw[0] : (raw ?? '');
  }, [params]);

  const [moderation, setModeration] = useState<SpeakingModerationCase | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [formValue, setFormValue] = useState<CriterionRubricFormValue>(EMPTY_RUBRIC_VALUE);
  const [decisionNote, setDecisionNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error' | 'info'; message: string } | null>(null);

  const load = useCallback(async () => {
    if (!sessionId) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const next = await moderationGetCase(sessionId);
      setModeration(next);
    } catch (err) {
      setErrorMsg(
        err instanceof SpeakingAssessmentApiError
          ? err.message
          : 'Could not load the moderation case. Please try again.',
      );
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleSecondMark = useCallback(async () => {
    const input = toMarkInput(formValue);
    if ('error' in input) {
      setToast({ variant: 'error', message: input.error });
      return;
    }
    setSubmitting(true);
    try {
      const next = await moderationSubmitSecondMark(sessionId, input);
      setModeration(next);
      setToast({
        variant: 'success',
        message:
          next.status === 'finalized'
            ? 'Marks agreed within threshold — final score reconciled automatically.'
            : 'Second mark submitted. Marks diverged — escalated to senior moderation.',
      });
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof SpeakingAssessmentApiError ? err.message : 'Could not submit the second mark.',
      });
    } finally {
      setSubmitting(false);
    }
  }, [formValue, sessionId]);

  const handleFinalize = useCallback(async () => {
    const input = toMarkInput(formValue);
    if ('error' in input) {
      setToast({ variant: 'error', message: input.error });
      return;
    }
    setSubmitting(true);
    try {
      const next = await moderationFinalize(sessionId, {
        ...input,
        decisionNote: decisionNote || undefined,
        requestReattempt: false,
      });
      setModeration(next);
      setToast({ variant: 'success', message: 'Final moderated score recorded.' });
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof SpeakingAssessmentApiError ? err.message : 'Could not finalise the moderation.',
      });
    } finally {
      setSubmitting(false);
    }
  }, [decisionNote, formValue, sessionId]);

  const handleRequestReattempt = useCallback(async () => {
    setSubmitting(true);
    try {
      const next = await moderationFinalize(sessionId, {
        decisionNote: decisionNote || undefined,
        requestReattempt: true,
      });
      setModeration(next);
      setToast({ variant: 'info', message: 'Reattempt requested. The learner will be asked to re-record.' });
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof SpeakingAssessmentApiError ? err.message : 'Could not request a reattempt.',
      });
    } finally {
      setSubmitting(false);
    }
  }, [decisionNote, sessionId]);

  const status = moderation?.status;
  const isPendingSecond = status === 'pending_second';
  const isPendingModeration = status === 'pending_moderation';
  const isClosed = status === 'finalized' || status === 'reattempt_requested';

  return (
    <ExpertDashboardShell>
      <div className="mx-auto flex w-full max-w-4xl flex-col gap-6 p-4 md:p-6">
        <div>
          <Button variant="ghost" size="sm" onClick={() => router.push('/expert/speaking/moderation')}>
            <ArrowLeft className="mr-2 h-4 w-4" aria-hidden />
            Back to moderation queue
          </Button>
        </div>

        <header className="flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <Scale className="h-6 w-6 text-primary" aria-hidden />
            <h1 className="text-2xl font-semibold tracking-tight">Moderation case</h1>
            {status && (
              <Badge variant={isPendingModeration ? 'danger' : isClosed ? 'success' : 'warning'}>
                {moderationStatusLabel(status)}
              </Badge>
            )}
          </div>
          <p className="font-mono text-xs text-muted-foreground">Session {sessionId}</p>
          {moderation?.variancePoints != null && (
            <p className="text-sm text-muted-foreground">
              Variance between markers: <span className="font-medium">{moderation.variancePoints} scaled points</span>
              {moderation.varianceReason ? ` — ${moderation.varianceReason}` : ''}
            </p>
          )}
        </header>

        {errorMsg && <InlineAlert variant="error">{errorMsg}</InlineAlert>}

        {loading ? (
          <div className="flex flex-col gap-3">
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-64 w-full" />
          </div>
        ) : !moderation ? (
          <InlineAlert variant="info">
            No moderation case exists for this session yet. Open one from the assessment page.
          </InlineAlert>
        ) : (
          <>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <ScoreSummary title="First marker" score={moderation.firstScore} />
              <ScoreSummary title="Second marker" score={moderation.secondScore} />
              <ScoreSummary title="Moderated final" score={moderation.finalScore} />
            </div>

            {moderation.finalDecisionNote && (
              <Card className="p-4">
                <h3 className="text-sm font-semibold">Moderator decision note</h3>
                <p className="mt-2 whitespace-pre-wrap text-sm text-muted-foreground">
                  {moderation.finalDecisionNote}
                </p>
              </Card>
            )}

            {(isPendingSecond || isPendingModeration) && (
              <Card className="flex flex-col gap-4 p-4">
                <div>
                  <h2 className="text-lg font-semibold">
                    {isPendingSecond ? 'Submit your independent second mark' : 'Reconcile the final moderated score'}
                  </h2>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {isPendingSecond
                      ? 'Score the session without reference to the first marker. If your marks agree within threshold the final score is reconciled automatically.'
                      : 'Enter the reconciled final score after reviewing both markers above, or request a reattempt if the recording cannot be fairly assessed.'}
                  </p>
                </div>

                <CriterionRubricForm value={formValue} onChange={setFormValue} mode="submit" />

                {isPendingModeration && (
                  <label className="flex flex-col gap-1 text-sm">
                    <span className="font-medium">Decision note (optional)</span>
                    <textarea
                      className="min-h-[80px] rounded-md border border-input bg-background p-2 text-sm"
                      value={decisionNote}
                      onChange={(e) => setDecisionNote(e.target.value)}
                      placeholder="Briefly record why this final score was chosen."
                    />
                  </label>
                )}

                <div className="flex flex-wrap gap-2">
                  {isPendingSecond ? (
                    <Button variant="primary" onClick={() => void handleSecondMark()} disabled={submitting}>
                      <CheckCircle2 className="mr-2 h-4 w-4" aria-hidden />
                      Submit second mark
                    </Button>
                  ) : (
                    <>
                      <Button variant="primary" onClick={() => void handleFinalize()} disabled={submitting}>
                        <CheckCircle2 className="mr-2 h-4 w-4" aria-hidden />
                        Record final score
                      </Button>
                      <Button variant="secondary" onClick={() => void handleRequestReattempt()} disabled={submitting}>
                        <RotateCcw className="mr-2 h-4 w-4" aria-hidden />
                        Request reattempt
                      </Button>
                    </>
                  )}
                </div>
              </Card>
            )}

            {isClosed && (
              <InlineAlert variant="success">
                This moderation case is closed ({moderationStatusLabel(moderation.status)}).
              </InlineAlert>
            )}
          </>
        )}
      </div>

      {toast && (
        <Toast variant={toast.variant} onClose={() => setToast(null)}>
          {toast.message}
        </Toast>
      )}
    </ExpertDashboardShell>
  );
}
