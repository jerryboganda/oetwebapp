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
import { getSpeakingSessionTranscript, type SpeakingTranscriptPayload } from '@/lib/api/speaking-sessions';
import {
  getSpeakingSimulationV11Assessment,
  getSpeakingSimulationV11CombinedAssessment,
  getSpeakingSimulationV11TutorOverride,
  runSpeakingSimulationV11Assessment,
  runSpeakingSimulationV11CombinedAssessment,
  type SpeakingSimulationV11AssessmentResponse,
  type SpeakingSimulationV11LearnerTutorOverride,
} from '@/lib/api/speaking-simulation-v11';
import { SpeakingSimulationV11ReportView } from '@/components/domain/speaking/SpeakingSimulationV11ReportView';

const POLL_INTERVAL_MS = 4_000;

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
          const existing = await getSpeakingSimulationV11Assessment(card.sessionId).catch(() => null);
          let assessment = existing;
          if (!assessment && canAssessV11 && !requestedCardAssessmentsRef.current.has(card.sessionId)) {
            assessment = await runSpeakingSimulationV11Assessment(card.sessionId).catch(() => null);
            if (assessment) requestedCardAssessmentsRef.current.add(card.sessionId);
          }
          const [transcriptResponse, tutorOverride] = await Promise.all([
            getSpeakingSessionTranscript(card.sessionId).catch(() => null),
            getSpeakingSimulationV11TutorOverride(card.sessionId).catch(() => null),
          ]);
          return {
            sessionId: card.sessionId,
            assessment,
            transcript: transcriptResponse?.transcript ?? null,
            tutorOverride,
          };
        }),
      );
      const nextCards: Record<string, SpeakingSimulationV11AssessmentResponse> = {};
      const nextTranscripts: Record<string, SpeakingTranscriptPayload | null> = {};
      const nextTutorOverrides: Record<string, SpeakingSimulationV11LearnerTutorOverride | null> = {};
      for (const item of cardDetails) {
        if (!item) continue;
        if (item.assessment) nextCards[item.sessionId] = item.assessment;
        nextTranscripts[item.sessionId] = item.transcript;
        nextTutorOverrides[item.sessionId] = item.tutorOverride;
      }
      setV11Cards(nextCards);
      setV11Transcripts(nextTranscripts);
      setV11TutorOverrides(nextTutorOverrides);

      let combined = await getSpeakingSimulationV11CombinedAssessment(examId).catch(() => null);
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
    const interval = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [refresh]);

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
        <p className="text-sm text-rose-700">{error}</p>
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
                    ? 'bg-emerald-100 text-emerald-700'
                    : 'bg-amber-100 text-amber-700',
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
