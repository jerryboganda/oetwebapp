'use client';

/**
 * Learner-facing AI result and recovery view for a speaking session.
 *
 * Part of the interlocutor-trainee practice route tree
 * (`app/speaking/sessions/[id]/*`) — not the candidate exam results page,
 * which lives under `app/speaking/exam/[id]`.
 *
 * Three tabs:
 * - Overview: AI assessment only; tutor services remain a separate product flow.
 * - Transcript: TranscriptPlayerWithComments (readOnly — comments are tutor-only)
 * - Recommended drills: AI recommendations only.
 *
 * 23 Sep 2026: never a dead end. While grading runs the page polls every
 * 3 s with backoff (capped, then "Check again"); a failed + retryable grade
 * offers "Try grading again" (POST /ai-assess, no extra charge). For the free
 * sample card it shows the retry CTA or "Free sample completed".
 *
 * 1 Oct 2026: wording follows what the learner handed in (`inputKind` from the
 * results endpoint, see lib/speaking/input-kind.ts). A live conversation keeps
 * only a transcript, so it never says "recording" and has no audio player.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { BookOpen, Loader2, Mic } from 'lucide-react';

import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { DualAssessmentLayout } from '@/components/domain/speaking/DualAssessmentLayout';
import { SpeakingSimulationV11ReportView } from '@/components/domain/speaking/SpeakingSimulationV11ReportView';
import { TranscriptPlayerWithComments, type TranscriptPayload } from '@/components/domain/speaking/TranscriptPlayerWithComments';
import { ApiError } from '@/lib/api';
import {
  learnerGetDualAssessment,
  type DualAssessmentResponse,
} from '@/lib/api/speaking-assessments';
import {
  getSpeakingSession,
  getSpeakingSessionResults,
  getSpeakingSessionTranscript,
  runAiAssessment,
  type SpeakingSessionDetail,
  type SpeakingSessionResultsStatus,
  type SpeakingTranscriptPayload,
} from '@/lib/api/speaking-sessions';
import { listFreeSamples, type FreeSampleOption } from '@/lib/api/free-samples';
import {
  getSpeakingResultVisibility,
  type SpeakingResultVisibilityDto,
} from '@/lib/api/speaking-result-visibility';
import { trackSpeaking } from '@/lib/analytics/speaking-events';
import {
  getSpeakingSimulationV11Assessment,
  type SpeakingSimulationV11AssessmentResponse,
} from '@/lib/api/speaking-simulation-v11';
import {
  LIVE_TRANSCRIPT_NOTE,
  gradeFailedSavedCopy,
  gradingInProgressCopy,
  speakingInputKind,
  submissionReceivedCopy,
  type SpeakingInputKind,
} from '@/lib/speaking/input-kind';

function AiProcessingCta() {
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="text-sm font-semibold text-navy">Assessment processing…</p>
      <p className="text-xs leading-relaxed text-muted">
        Your AI assessment will appear here automatically in a few minutes.
      </p>
    </div>
  );
}

const POLL_START_MS = 3_000;
const POLL_MAX_MS = 15_000;
const POLL_MAX_ATTEMPTS = 40;

function GradingStatus({
  status,
  inputKind,
  pending,
  pollCapped,
  retrying,
  onRetry,
  onCheckAgain,
}: {
  status: SpeakingSessionResultsStatus | null;
  inputKind: SpeakingInputKind | null;
  pending: boolean;
  pollCapped: boolean;
  retrying: boolean;
  onRetry: () => void;
  onCheckAgain: () => void;
}) {
  if (status?.assessmentState === 'failed') {
    return (
      <InlineAlert
        variant="error"
        title="Grading could not be completed"
        action={status.retryable ? (
          <Button size="sm" onClick={onRetry} disabled={retrying} data-testid="speaking-retry-grading">
            {retrying ? <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden /> : null}
            Try grading again
          </Button>
        ) : (
          <Button variant="outline" size="sm" asChild>
            <Link href="/speaking">Back to Speaking</Link>
          </Button>
        )}
      >
        {status.failureReason ?? gradeFailedSavedCopy(inputKind)}
      </InlineAlert>
    );
  }
  if (!pending) return null;
  return (
    <InlineAlert
      variant="info"
      title="Grading your role-play…"
      action={pollCapped ? (
        <Button variant="outline" size="sm" onClick={onCheckAgain}>
          Check again
        </Button>
      ) : undefined}
    >
      <span className="inline-flex items-center gap-2" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        {pollCapped
          ? 'This is taking longer than usual. Your result is saved and will appear here — you can leave and come back.'
          : gradingInProgressCopy(inputKind)}
      </span>
    </InlineAlert>
  );
}

function FreeSampleNext({ row }: { row: FreeSampleOption | null }) {
  if (row?.state === 'completed') {
    return (
      <p className="rounded-lg border border-border bg-background-light px-3 py-2 text-center text-sm font-semibold text-muted" data-testid="speaking-free-completed">
        Free sample completed
      </p>
    );
  }
  return null;
}

function VisibilityLockedCta({ title, body }: { title: string; body: string }) {
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="text-sm font-semibold text-navy">{title}</p>
      <p className="text-xs leading-relaxed text-muted">{body}</p>
    </div>
  );
}

export default function SpeakingSessionResultsPage() {
  const params = useParams();
  const rawId = params?.id;
  const sessionId = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  const [data, setData] = useState<DualAssessmentResponse | null>(null);
  const [v11, setV11] = useState<SpeakingSimulationV11AssessmentResponse | null>(null);
  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [visibility, setVisibility] = useState<SpeakingResultVisibilityDto | null>(null);
  const [transcript, setTranscript] = useState<SpeakingTranscriptPayload | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('overview');
  const [status, setStatus] = useState<SpeakingSessionResultsStatus | null>(null);
  const [freeRow, setFreeRow] = useState<FreeSampleOption | null>(null);
  const [pollAttempt, setPollAttempt] = useState(0);
  const [retrying, setRetrying] = useState(false);
  const trackedAiAssessmentRef = useRef(false);

  const load = useCallback(async (showSpinner = true) => {
    if (!sessionId) return;
    if (showSpinner) setLoading(true);
    setErrorMsg(null);
    try {
      const sessionDetail = await getSpeakingSession(sessionId);
      const visibilityDto = await getSpeakingResultVisibility(sessionDetail.card.cardId).catch(() => null);

      const statusResponse = await getSpeakingSessionResults(sessionId).catch(() => null);
      // v1.1 report endpoints exist only for v1.1-scored sessions.
      const usesV11 = statusResponse?.usesV11 === true;
      const assessmentPromise = learnerGetDualAssessment(sessionId).catch(() => null);
      const v11Promise = usesV11 ? getSpeakingSimulationV11Assessment(sessionId).catch(() => null) : null;
      const transcriptPromise = visibilityDto?.showTranscript !== false
        ? getSpeakingSessionTranscript(sessionId).catch(() => null)
        : Promise.resolve(null);

      const freePromise = listFreeSamples('speaking').catch(() => []);

      const [v11Response, assessmentResponse, transcriptResponse, freeRows] = await Promise.all([
        v11Promise,
        assessmentPromise,
        transcriptPromise,
        freePromise,
      ]);
      setStatus(statusResponse);
      setFreeRow((Array.isArray(freeRows) ? freeRows : []).find((row) => row.contentId === sessionDetail.card.cardId) ?? null);
      setSession(sessionDetail);
      setVisibility(visibilityDto);
      setData(assessmentResponse);
      setV11(v11Response);
      setTranscript(transcriptResponse?.transcript ?? null);
    } catch (err) {
      const msg = err instanceof ApiError ? err.userMessage : 'Could not load this assessment. Please try again.';
      setErrorMsg(msg);
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Grading is pending until an AI result (v1.1 or dual) exists, unless the
  // server says it failed. A completed state with no payload yet also polls.
  const gradingPending = !v11 && !data?.ai && status?.assessmentState !== 'failed';
  const pollCapped = pollAttempt >= POLL_MAX_ATTEMPTS;

  useEffect(() => {
    // 3 s, backing off to 15 s, capped — then the learner gets "Check again".
    if (loading || errorMsg || pollCapped) return;
    if (!gradingPending && !(visibility?.showTranscript && !transcript)) return;
    const delay = Math.min(POLL_MAX_MS, Math.round(POLL_START_MS * 1.25 ** pollAttempt));
    const timer = window.setTimeout(() => {
      setPollAttempt((attempt) => attempt + 1);
      void load(false);
    }, delay);
    return () => window.clearTimeout(timer);
  }, [errorMsg, gradingPending, load, loading, pollAttempt, pollCapped, transcript, visibility?.showTranscript]);

  const retryGrading = useCallback(async () => {
    setRetrying(true);
    try {
      await runAiAssessment(sessionId);
      // Keep what was handed in, or the wording flips to the neutral variant until the next poll.
      setStatus((current) => ({ inputKind: current?.inputKind, assessmentState: 'processing', retryable: false, failureReason: null }));
      setPollAttempt(0);
    } catch (err) {
      setStatus((current) => current && {
        ...current,
        failureReason: err instanceof ApiError ? err.userMessage : 'Could not restart grading. Please try again.',
      });
    } finally {
      setRetrying(false);
    }
  }, [sessionId]);

  // What the learner handed in decides the wording below; a tutor room is always recorded.
  const inputKind = speakingInputKind(session?.mode === 'live_tutor', status?.inputKind);

  const gradingStatus = (
    <GradingStatus
      status={status}
      inputKind={inputKind}
      pending={gradingPending}
      pollCapped={pollCapped}
      retrying={retrying}
      onRetry={() => void retryGrading()}
      onCheckAgain={() => {
        setPollAttempt(0);
        void load(false);
      }}
    />
  );

  const showSubmissionReceived = visibility?.showSubmissionReceived ?? true;
  const showAiEstimate = visibility?.showAiEstimate ?? true;
  const showReadinessBand = visibility?.showReadinessBand ?? true;
  const showFullCriteria = visibility?.showFullCriteria ?? true;
  const showTranscript = visibility?.showTranscript ?? true;
  const showRecommendedDrills = visibility?.showRecommendedDrills ?? true;
  const allowReattempt = visibility?.allowReattempt ?? true;

  const visibleData = useMemo<DualAssessmentResponse | null>(() => {
    if (!data) return null;
    return {
      sessionId: data.sessionId,
      ai: showAiEstimate ? data.ai : null,
      tutor: null,
      tutorHistory: [],
      divergence: null,
    };
  }, [data, showAiEstimate]);

  useEffect(() => {
    if (visibleData?.ai && !trackedAiAssessmentRef.current) {
      trackedAiAssessmentRef.current = true;
      trackSpeaking('ai_assessment_viewed', {
        sessionId,
        estimatedBand: visibleData.ai.readinessBand,
      });
    }
  }, [sessionId, visibleData]);

  const transcriptPayload = useMemo<TranscriptPayload>(() => ({
    segments: transcript?.segments ?? [],
  }), [transcript]);

  const drills = useMemo(() => {
    if (!showRecommendedDrills) return [] as string[];
    const all = new Set<string>();
    visibleData?.ai?.recommendedDrills?.forEach((d) => all.add(d));
    return Array.from(all);
  }, [showRecommendedDrills, visibleData]);

  const tabs = useMemo(
    () => {
      const baseTabs: Array<{ id: string; label: string; icon: ReactNode; count?: number }> = [
      { id: 'overview', label: 'Overview', icon: <Mic className="h-4 w-4" aria-hidden /> },
      ];
      if (showTranscript) {
        baseTabs.push({ id: 'transcript', label: 'Transcript', icon: <Loader2 className="hidden" aria-hidden /> });
      }
      if (showRecommendedDrills) {
        baseTabs.push({
          id: 'drills',
          label: 'Recommended drills',
          icon: <BookOpen className="h-4 w-4" aria-hidden />,
          count: drills.length || undefined,
        });
      }
      return baseTabs;
    },
    [drills.length, showRecommendedDrills, showTranscript],
  );

  useEffect(() => {
    if (!tabs.some((tab) => tab.id === activeTab)) {
      setActiveTab(tabs[0]?.id ?? 'overview');
    }
  }, [activeTab, tabs]);

  const hiddenAiPlaceholder = (
    <VisibilityLockedCta
      title="AI estimate hidden"
      body="This result visibility profile hides the AI estimate for learners on this card."
    />
  );

  const reattemptHref = session
    ? `/speaking/roleplay/${encodeURIComponent(session.card.cardId)}`
    : '/speaking/selection';
  const submissionAtLabel = session?.submittedAt
    ? new Date(session.submittedAt).toLocaleString()
    : null;

  if (loading) {
    return (
      <>
        <div className="flex flex-col gap-4">
          <Skeleton className="h-12 w-64" />
          <div className="grid gap-4 md:grid-cols-2">
            <Skeleton className="h-96 w-full" />
            <Skeleton className="h-96 w-full" />
          </div>
        </div>
      </>
    );
  }

  if (v11 && session) {
    return (
      <>
        <div className="flex flex-col gap-4">
          {showSubmissionReceived && session.submittedAt ? (
            <InlineAlert
              variant="success"
              title="Submission received"
              action={allowReattempt ? (
                <Button variant="outline" size="sm" asChild>
                  <Link href={reattemptHref}>Try another role play</Link>
                </Button>
              ) : undefined}
            >
              {submissionReceivedCopy(inputKind, submissionAtLabel)}
            </InlineAlert>
          ) : null}
          {gradingStatus}
          <FreeSampleNext row={freeRow} />
          <SpeakingSimulationV11ReportView
            sessionId={sessionId}
            response={v11}
            transcript={showTranscript ? transcript : null}
            inputKind={inputKind}
          />
        </div>
      </>
    );
  }

  if (errorMsg) {
    return (
      <>
        <InlineAlert
          variant="error"
          title="Failed to load assessment"
          action={
            <Button onClick={() => void load()} size="sm" variant="outline">
              Try again
            </Button>
          }
        >
          {errorMsg}
        </InlineAlert>
      </>
    );
  }

  // No dual assessment yet (still grading): render the processing layout, never a dead end.
  const layoutData: DualAssessmentResponse = visibleData ?? data ?? {
    sessionId,
    ai: null,
    tutor: null,
    tutorHistory: [],
    divergence: null,
  };
  const isFreeSession = Boolean(session?.isFreeSample || freeRow);

  return (
    <>
      <div className="flex flex-col gap-4">
        {showSubmissionReceived && session?.submittedAt ? (
          <InlineAlert
            variant="success"
            title="Submission received"
            action={allowReattempt ? (
              <Button variant="outline" size="sm" asChild>
                <Link href={reattemptHref}>Try another role play</Link>
              </Button>
            ) : undefined}
          >
            {submissionReceivedCopy(inputKind, submissionAtLabel)}
          </InlineAlert>
        ) : null}

        {gradingStatus}
        <FreeSampleNext row={freeRow} />

        {allowReattempt && session ? (
          <Card padding="md" className="flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-navy">Start a new Speaking attempt</p>
              <p className="text-xs leading-relaxed text-muted">
                {isFreeSession
                  ? 'This free sample is complete. Repeating the card starts a new attempt and uses Speaking credits.'
                  : 'Completed attempts cannot be replayed for free. Starting again uses Speaking credits.'}
              </p>
            </div>
            <Button variant="outline" size="sm" asChild>
              <Link href={reattemptHref}>Start new attempt</Link>
            </Button>
          </Card>
        ) : null}

        <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} />

        <TabPanel id="overview" activeTab={activeTab}>
          <DualAssessmentLayout
            data={layoutData}
            aiPlaceholderCta={showAiEstimate ? <AiProcessingCta /> : hiddenAiPlaceholder}
            showFullCriteria={showFullCriteria}
            showReadinessBand={showReadinessBand}
            showTutorAssessment={false}
          />
        </TabPanel>

        {showTranscript ? (
          <TabPanel id="transcript" activeTab={activeTab}>
            <div className="space-y-3">
              {inputKind === 'live_voice' ? (
                <InlineAlert variant="info" live="polite">
                  {LIVE_TRANSCRIPT_NOTE}
                </InlineAlert>
              ) : null}
              <TranscriptPlayerWithComments
                recordingUrl={null}
                transcript={transcriptPayload}
                comments={[]}
                readOnly
                hideAudioPlayer={inputKind !== 'recording'}
              />
            </div>
          </TabPanel>
        ) : null}

        {showRecommendedDrills ? (
          <TabPanel id="drills" activeTab={activeTab}>
            {drills.length === 0 ? (
              <Card padding="lg" className="text-center text-sm text-muted">
                No drills recommended yet. Come back once your tutor review is in.
              </Card>
            ) : (
              <Card padding="md" className="flex flex-col gap-3">
                <h3 className="text-base font-bold text-navy">Recommended drills</h3>
                <p className="text-xs text-muted">
                  Targeted practice based on your AI and tutor feedback. Drill titles map to slugs in the practice library.
                </p>
                <ul className="flex flex-col gap-2">
                  {drills.map((slug) => (
                    <li key={slug}>
                      <Link
                        href={`/speaking/drills?slug=${encodeURIComponent(slug)}`}
                        className="flex items-center justify-between gap-3 rounded-xl border border-border bg-background-light p-3 hover:border-primary/40 hover:bg-primary/5"
                      >
                        <div className="flex items-center gap-2">
                          <BookOpen className="h-4 w-4 text-primary" aria-hidden />
                          <span className="font-semibold text-navy">{slug.replace(/-/g, ' ')}</span>
                        </div>
                        <span className="text-xs font-bold text-primary">Open drill →</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </TabPanel>
        ) : null}
      </div>
    </>
  );
}
