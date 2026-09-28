'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Results page for the two-card Speaking exam. v1.1 AI-mode exams show the
 * calibrated practice report; live-tutor exams remain pending until a tutor
 * submits. Legacy sessions retain their existing result fallback.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Loader2, Mic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { CriterionScoreRow } from '@/components/domain/results/criterion-score-row';
import { cn } from '@/lib/utils';
import {
  getSpeakingExamResults,
  type SpeakingExamResults,
} from '@/lib/api/speaking-exams';
import { ApiError } from '@/lib/api';
import {
  getSpeakingSessionResults,
  getSpeakingSessionTranscript,
  runAiAssessment,
  type SpeakingSessionResultsStatus,
  type SpeakingTranscriptPayload,
} from '@/lib/api/speaking-sessions';
import {
  getSpeakingSimulationV11Assessment,
  getSpeakingSimulationV11CombinedAssessment,
  getSpeakingSimulationV11TutorOverride,
  runSpeakingSimulationV11CombinedAssessment,
  type SpeakingSimulationV11AssessmentResponse,
  type SpeakingSimulationV11LearnerTutorOverride,
} from '@/lib/api/speaking-simulation-v11';
import { SpeakingSimulationV11ReportView } from '@/components/domain/speaking/SpeakingSimulationV11ReportView';

const POLL_INTERVAL_MS = 4_000;
/** ~10 minutes of polling, then "Check again" (the result persists server-side). */
const MAX_POLLS = 150;

export default function SpeakingExamResultsPage() {
  const params = useParams<{ id: string }>();
  const examId = params?.id ?? '';
  const [results, setResults] = useState<SpeakingExamResults | null>(null);
  const [v11Cards, setV11Cards] = useState<Record<string, SpeakingSimulationV11AssessmentResponse>>({});
  const [v11Transcripts, setV11Transcripts] = useState<Record<string, SpeakingTranscriptPayload | null>>({});
  const [v11TutorOverrides, setV11TutorOverrides] = useState<Record<string, SpeakingSimulationV11LearnerTutorOverride | null>>({});
  const [v11Combined, setV11Combined] = useState<SpeakingSimulationV11AssessmentResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [cardStatus, setCardStatus] = useState<Record<string, SpeakingSessionResultsStatus | null>>({});
  const [pollCount, setPollCount] = useState(0);
  const [retryingSessionId, setRetryingSessionId] = useState<string | null>(null);
  const requestedCardAssessmentsRef = useRef(new Set<string>());
  const requestedCombinedRef = useRef(false);

  const refresh = useCallback(async () => {
    if (!examId) return;
    try {
      const r = await getSpeakingExamResults(examId);
      setResults(r);
      const isAiExam = r.mode === 'ai';
      const canAssessV11 = isAiExam && r.state === 'completed';
      const cardDetails = await Promise.all(
        r.cards.map(async (card) => {
          if (!card.sessionId) return null;
          const gradingStatus = await getSpeakingSessionResults(card.sessionId).catch(() => null);
          // v1.1 report endpoints exist only for v1.1-scored cards.
          const usesV11 = gradingStatus?.usesV11 === true;
          if (canAssessV11 && gradingStatus?.assessmentState === 'processing'
            && !requestedCardAssessmentsRef.current.has(card.sessionId)) {
            // Make sure grading is running (idempotent; the worker normally
            // already has it). Not awaited: grading takes minutes, and the
            // server keeps going after the browser stops waiting.
            requestedCardAssessmentsRef.current.add(card.sessionId);
            void runAiAssessment(card.sessionId).catch(() => undefined);
          }
          const [assessment, transcriptResponse, tutorOverride] = await Promise.all([
            usesV11 ? getSpeakingSimulationV11Assessment(card.sessionId).catch(() => null) : null,
            getSpeakingSessionTranscript(card.sessionId).catch(() => null),
            usesV11 ? getSpeakingSimulationV11TutorOverride(card.sessionId).catch(() => null) : null,
          ]);
          return {
            sessionId: card.sessionId,
            assessment,
            transcript: transcriptResponse?.transcript ?? null,
            tutorOverride,
            gradingStatus,
            usesV11,
          };
        }),
      );
      const nextCards: Record<string, SpeakingSimulationV11AssessmentResponse> = {};
      const nextTranscripts: Record<string, SpeakingTranscriptPayload | null> = {};
      const nextTutorOverrides: Record<string, SpeakingSimulationV11LearnerTutorOverride | null> = {};
      const nextStatus: Record<string, SpeakingSessionResultsStatus | null> = {};
      for (const item of cardDetails) {
        if (!item) continue;
        if (item.assessment) nextCards[item.sessionId] = item.assessment;
        nextTranscripts[item.sessionId] = item.transcript;
        nextTutorOverrides[item.sessionId] = item.tutorOverride;
        nextStatus[item.sessionId] = item.gradingStatus;
      }
      setCardStatus(nextStatus);
      setV11Cards(nextCards);
      setV11Transcripts(nextTranscripts);
      setV11TutorOverrides(nextTutorOverrides);

      const anyV11 = cardDetails.some((item) => item?.usesV11);
      let combined = anyV11 ? await getSpeakingSimulationV11CombinedAssessment(examId).catch(() => null) : null;
      const completeCards = Object.values(nextCards).filter((item) => item.status === 'Complete');
      if (!combined && canAssessV11 && completeCards.length === 2 && !requestedCombinedRef.current) {
        const assessedCombined = await runSpeakingSimulationV11CombinedAssessment(examId).catch(() => null);
        if (assessedCombined) {
          requestedCombinedRef.current = true;
          combined = assessedCombined;
        }
      }
      setV11Combined(combined);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : 'Could not load results.');
    } finally {
      setLoading(false);
    }
  }, [examId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const done = Boolean(v11Combined) || results?.overallStatus === 'scored';
  const pollCapped = pollCount >= MAX_POLLS;

  // Poll until the combined result is in, then stop; capped so a stuck grade
  // becomes "Check again" rather than an endless spinner.
  useEffect(() => {
    if (done || pollCapped) return;
    const timer = window.setTimeout(() => {
      setPollCount((count) => count + 1);
      void refresh();
    }, POLL_INTERVAL_MS);
    return () => window.clearTimeout(timer);
  }, [done, pollCapped, pollCount, refresh]);

  const retryCard = async (sessionId: string) => {
    setRetryingSessionId(sessionId);
    try {
      await runAiAssessment(sessionId);
      setCardStatus((current) => ({ ...current, [sessionId]: { assessmentState: 'processing', retryable: false, failureReason: null } }));
      setPollCount(0);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : 'Could not restart grading. Please try again.');
    } finally {
      setRetryingSessionId(null);
    }
  };

  const failedCards = (results?.cards ?? []).filter((card) => {
    const status = card.sessionId ? cardStatus[card.sessionId] : null;
    return status?.assessmentState === 'failed';
  });
  const gradingNotices = failedCards.length > 0 || (pollCapped && !done) ? (
    <div className="mb-4 space-y-2" data-testid="speaking-exam-grading-notices">
      {failedCards.map((card) => {
        const status = cardStatus[card.sessionId];
        return (
          <div key={card.sessionId} className="rounded-xl border border-danger/30 bg-danger/10 p-4 text-sm text-navy" role="alert">
            <p className="font-semibold">Card {card.cardNumber === 1 ? 'A' : 'B'}: grading could not be completed</p>
            <p className="mt-1">{status?.failureReason ?? 'Your recording is saved. No credits were used for this failed grade.'}</p>
            {status?.retryable ? (
              <Button className="mt-3" size="sm" onClick={() => void retryCard(card.sessionId)} disabled={retryingSessionId === card.sessionId}>
                {retryingSessionId === card.sessionId ? <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden /> : null}
                Try grading again
              </Button>
            ) : null}
          </div>
        );
      })}
      {pollCapped && !done ? (
        <div className="rounded-xl border border-border bg-surface p-4 text-sm text-muted">
          <p>Grading is taking longer than usual. Your recordings are saved and the result will appear here.</p>
          <Button className="mt-3" size="sm" variant="outline" onClick={() => { setPollCount(0); void refresh(); }}>
            Check again
          </Button>
        </div>
      ) : null}
    </div>
  ) : null;

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-muted" />
      </div>
    );
  }

  if (error && !results) {
    return (
      <div className="mx-auto max-w-lg px-4 py-12 text-center">
        <p className="text-sm text-danger">{error}</p>
        <Button className="mt-4" variant="outline" onClick={() => void refresh()}>
          Retry
        </Button>
      </div>
    );
  }

  if (!results) return null;

  const firstCardSessionId = results.cards.find((card) => card.sessionId)?.sessionId ?? examId;
  if (v11Combined) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-8">
        <SpeakingSimulationV11ReportView
          sessionId={firstCardSessionId}
          response={v11Combined}
          transcriptsBySessionId={v11Transcripts}
          tutorOverridesBySessionId={v11TutorOverrides}
          title="Full Speaking mock report"
        />
        <div className="mt-6 flex justify-center">
          <Button asChild variant="outline">
            <Link href="/speaking">Back to Speaking</Link>
          </Button>
        </div>
      </div>
    );
  }

  const firstV11Entry = results.cards
    .map((card) => card.sessionId ? [card.sessionId, v11Cards[card.sessionId]] as const : null)
    .find((entry): entry is readonly [string, SpeakingSimulationV11AssessmentResponse] => Boolean(entry?.[1]));
  if (firstV11Entry) {
    const [firstV11SessionId, firstV11Card] = firstV11Entry;
    return (
      <div className="mx-auto max-w-5xl px-4 py-8">
        {gradingNotices}
        <SpeakingSimulationV11ReportView
          sessionId={firstV11SessionId}
          response={firstV11Card}
          transcript={v11Transcripts[firstV11SessionId]}
          tutorOverride={v11TutorOverrides[firstV11SessionId]}
          title="Speaking card report"
        />
        <div className="mt-6 flex justify-center">
          <Button asChild variant="outline">
            <Link href="/speaking">Back to Speaking</Link>
          </Button>
        </div>
      </div>
    );
  }

  const pending = results.overallStatus !== 'scored';
  const awaitingTutor = results.overallStatus === 'awaiting_tutor';

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <h1 className="text-xl font-semibold text-foreground">Speaking exam results</h1>
      <div className="mt-4">{gradingNotices}</div>

      {pending ? (
        <div className="mt-4 rounded-xl border border-border bg-surface p-5">
          <div className="flex items-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" />
            {awaitingTutor
              ? 'Your tutor is marking this exam. Your result will appear here once marking is complete.'
              : 'Scoring your exam… this usually takes a moment. This page refreshes automatically.'}
          </div>
        </div>
      ) : (
        <div className="mt-4">
          <ResultsScorePanel
            eyebrow="Speaking exam"
            icon={Mic}
            title="Combined result"
            subtitle={results.readinessBand ? `Readiness band ${results.readinessBand}` : undefined}
            gaugeValue={typeof results.combinedScaledScore === 'number' ? (results.combinedScaledScore / 500) * 100 : 0}
            gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{results.combinedScaledScore ?? '—'}</span>}
            gaugeLabel="/ 500"
            gaugeColor="var(--color-success)"
            grade={results.readinessBand ? { label: `Band ${results.readinessBand}`, tone: 'success' } : null}
            stats={results.cards.map((card) => ({
              label: `Card ${card.cardNumber === 1 ? 'A' : 'B'}`,
              value: card.assessment ? `${card.assessment.estimatedScaledScore}/500` : '—',
              tone: 'info' as const,
            }))}
          />
        </div>
      )}

      <div className="mt-6 space-y-4">
        {results.cards.map((card) => (
          <section key={card.cardNumber} className="rounded-xl border border-border bg-surface p-5">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-semibold text-foreground">
                Card {card.cardNumber === 1 ? 'A' : 'B'}
              </h2>
              <span
                className={cn(
                  'rounded-full px-2.5 py-0.5 text-xs font-medium',
                  card.status === 'scored'
                    ? 'bg-success/10 text-success'
                    : 'bg-warning/10 text-warning',
                )}
              >
                {card.status === 'scored'
                  ? 'Scored'
                  : card.status === 'awaiting_tutor'
                    ? 'Awaiting tutor'
                    : 'Processing'}
              </span>
            </div>

            {card.assessment ? (
              <div className="mt-3 space-y-3">
                <div className="flex items-baseline gap-2">
                  <span className="text-2xl font-bold tabular-nums text-foreground">
                    {card.assessment.estimatedScaledScore}
                  </span>
                  <span className="text-sm text-muted">/ 500</span>
                  <span className="ml-auto text-xs uppercase tracking-wide text-muted">
                    Band {card.assessment.readinessBand}
                  </span>
                </div>
                {card.assessment.overallSummary ? (
                  <p className="text-sm leading-relaxed text-muted">
                    {card.assessment.overallSummary}
                  </p>
                ) : null}
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  {Object.entries(card.assessment.criterionScores).map(([code, c]) => (
                    <CriterionScoreRow
                      key={code}
                      label={code.replace(/([A-Z])/g, ' $1').replace(/^./, (m) => m.toUpperCase()).trim()}
                      score={c.score}
                      max={c.maxScore}
                    />
                  ))}
                </div>
              </div>
            ) : (
              <p className="mt-3 text-sm text-muted">Not yet available.</p>
            )}
          </section>
        ))}
      </div>

      <div className="mt-6 flex justify-center">
        <Button asChild variant="outline">
          <Link href="/speaking">Back to Speaking</Link>
        </Button>
      </div>
    </div>
  );
}
