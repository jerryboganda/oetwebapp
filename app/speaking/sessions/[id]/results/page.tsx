'use client';

/**
 * Learner: dual-scoring results view for a speaking session.
 *
 * Three tabs:
 * - Overview: DualAssessmentLayout (AI + Tutor side-by-side + divergence banner)
 * - Transcript: TranscriptPlayerWithComments (readOnly — comments are tutor-only)
 * - Recommended drills: list of slugs/titles from AI.recommendedDrills + tutor.recommendedDrills
 *
 * If tutor is null, the layout's tutor column shows a CTA "Request tutor review
 * (uses 1 credit)" — visually disabled for now (credit gating wires in later).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { BookOpen, Loader2, Mic, UserPlus } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { DualAssessmentLayout } from '@/components/domain/speaking/DualAssessmentLayout';
import { TranscriptPlayerWithComments, type TranscriptPayload } from '@/components/domain/speaking/TranscriptPlayerWithComments';
import {
  SpeakingAssessmentApiError,
  learnerGetDualAssessment,
  type DualAssessmentResponse,
} from '@/lib/api/speaking-assessments';
import {
  getSpeakingSession,
  getSpeakingSessionTranscript,
  type SpeakingSessionDetail,
  type SpeakingTranscriptPayload,
} from '@/lib/api/speaking-sessions';
import {
  getSpeakingResultVisibility,
  type SpeakingResultVisibilityDto,
} from '@/lib/api/speaking-result-visibility';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

function TutorReviewCta() {
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="text-sm font-semibold text-navy">No tutor review yet</p>
      <p className="text-xs leading-relaxed text-muted">
        A calibrated OET tutor can review this session and provide nuanced feedback that AI may miss.
      </p>
      <Button
        size="sm"
        variant="outline"
        disabled
        aria-disabled
        title="Coming soon. Credit gating not yet enabled."
      >
        <UserPlus className="mr-1.5 h-3.5 w-3.5" aria-hidden />
        Request tutor review (uses 1 credit)
      </Button>
    </div>
  );
}

function AiProcessingCta() {
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="text-sm font-semibold text-navy">Assessment processing…</p>
      <p className="text-xs leading-relaxed text-muted">
        Your AI assessment will appear here in a few minutes. Refresh once it&apos;s ready.
      </p>
    </div>
  );
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
  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [visibility, setVisibility] = useState<SpeakingResultVisibilityDto | null>(null);
  const [transcript, setTranscript] = useState<SpeakingTranscriptPayload | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('overview');
  const trackedAiAssessmentRef = useRef(false);
  const trackedTutorAssessmentRef = useRef(false);

  const load = useCallback(async (showSpinner = true) => {
    if (!sessionId) return;
    if (showSpinner) setLoading(true);
    setErrorMsg(null);
    try {
      const sessionDetail = await getSpeakingSession(sessionId);
      const visibilityDto = await getSpeakingResultVisibility(sessionDetail.card.cardId).catch(() => null);

      const assessmentPromise = learnerGetDualAssessment(sessionId);
      const transcriptPromise = visibilityDto?.showTranscript !== false
        ? getSpeakingSessionTranscript(sessionId).catch(() => null)
        : Promise.resolve(null);

      const [assessmentResponse, transcriptResponse] = await Promise.all([assessmentPromise, transcriptPromise]);
      setSession(sessionDetail);
      setVisibility(visibilityDto);
      setData(assessmentResponse);
      setTranscript(transcriptResponse?.transcript ?? null);
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to load assessment.';
      setErrorMsg(msg);
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    if (visibleData?.ai && !trackedAiAssessmentRef.current) {
      trackedAiAssessmentRef.current = true;
      trackSpeaking('ai_assessment_viewed', {
        sessionId,
        estimatedBand: visibleData.ai.readinessBand,
      });
    }
    if (visibleData?.tutor && !trackedTutorAssessmentRef.current) {
      trackedTutorAssessmentRef.current = true;
      trackSpeaking('tutor_assessment_viewed', {
        sessionId,
        estimatedBand: visibleData.tutor.readinessBand,
      });
    }
  }, [sessionId, visibleData]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    // Poll once a minute while AI is still processing or the transcript is pending.
    const interval = window.setInterval(() => {
      if (!data?.ai || (visibility?.showTranscript && !transcript)) {
        void load(false);
      }
    }, 60_000);
    return () => window.clearInterval(interval);
  }, [data?.ai, load, transcript, visibility?.showTranscript]);

  const showSubmissionReceived = visibility?.showSubmissionReceived ?? true;
  const showAiEstimate = visibility?.showAiEstimate ?? true;
  const showReadinessBand = visibility?.showReadinessBand ?? true;
  const showTutorScore = visibility?.showTutorScore ?? true;
  const showFullCriteria = visibility?.showFullCriteria ?? true;
  const showTranscript = visibility?.showTranscript ?? true;
  const showTutorComments = visibility?.showTutorComments ?? true;
  const showRecommendedDrills = visibility?.showRecommendedDrills ?? true;
  const allowReattempt = visibility?.allowReattempt ?? true;

  const visibleData = useMemo<DualAssessmentResponse | null>(() => {
    if (!data) return null;
    return {
      sessionId: data.sessionId,
      ai: showAiEstimate ? data.ai : null,
      tutor: showTutorScore ? data.tutor : null,
      tutorHistory: showTutorScore ? data.tutorHistory : [],
      divergence: showAiEstimate && showTutorScore ? data.divergence : null,
    };
  }, [data, showAiEstimate, showTutorScore]);

  const transcriptPayload = useMemo<TranscriptPayload>(() => ({
    segments: transcript?.segments ?? [],
  }), [transcript]);

  const drills = useMemo(() => {
    if (!showRecommendedDrills) return [] as string[];
    const all = new Set<string>();
    visibleData?.ai?.recommendedDrills?.forEach((d) => all.add(d));
    visibleData?.tutor?.recommendedDrills?.forEach((d) => all.add(d));
    return Array.from(all);
  }, [showRecommendedDrills, visibleData]);

  const tabs = useMemo(
    () => {
      const baseTabs = [
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

  const hiddenTutorPlaceholder = (
    <VisibilityLockedCta
      title="Tutor review hidden"
      body="This result visibility profile hides the tutor score and review details for learners on this card."
    />
  );

  const reattemptHref = session ? `/speaking/check?taskId=${encodeURIComponent(session.card.cardId)}` : '/speaking/check';
  const submissionAtLabel = session?.submittedAt
    ? new Date(session.submittedAt).toLocaleString()
    : null;

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking results">
        <div className="flex flex-col gap-4">
          <Skeleton className="h-12 w-64" />
          <div className="grid gap-4 md:grid-cols-2">
            <Skeleton className="h-96 w-full" />
            <Skeleton className="h-96 w-full" />
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (errorMsg || !data) {
    return (
      <LearnerDashboardShell pageTitle="Speaking results">
        <InlineAlert
          variant="error"
          title="Failed to load assessment"
          action={
            <Button onClick={() => void load()} size="sm" variant="outline">
              Try again
            </Button>
          }
        >
          {errorMsg ?? 'No assessment data available.'}
        </InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const subtitle = session
    ? `${session.card.scenarioTitle} · Session ${sessionId.slice(0, 8)}…`
    : `Session ${sessionId.slice(0, 8)}…`;

  return (
    <LearnerDashboardShell
      pageTitle="Speaking results"
      subtitle={subtitle}
    >
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
            We received your recording{submissionAtLabel ? ` on ${submissionAtLabel}` : ''} and queued it for marking.
          </InlineAlert>
        ) : null}

        {allowReattempt ? (
          <Card padding="md" className="flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-navy">Reattempt this speaking card</p>
              <p className="text-xs leading-relaxed text-muted">
                Start another practice attempt with the same scenario from the learner speaking check flow.
              </p>
            </div>
            <Button variant="outline" size="sm" asChild>
              <Link href={reattemptHref}>Open speaking check</Link>
            </Button>
          </Card>
        ) : null}

        <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} />

        <TabPanel id="overview" activeTab={activeTab}>
          <DualAssessmentLayout
            data={visibleData ?? data}
            tutorPlaceholderCta={showTutorScore ? <TutorReviewCta /> : hiddenTutorPlaceholder}
            aiPlaceholderCta={showAiEstimate ? <AiProcessingCta /> : hiddenAiPlaceholder}
            showFullCriteria={showFullCriteria}
            showReadinessBand={showReadinessBand}
          />
        </TabPanel>

        {showTranscript ? (
          <TabPanel id="transcript" activeTab={activeTab}>
            <div className="space-y-3">
              {!showTutorComments ? (
                <InlineAlert variant="info" title="Tutor comments hidden">
                  This result visibility profile hides tutor comments. The transcript remains available for review.
                </InlineAlert>
              ) : null}
              <TranscriptPlayerWithComments
                recordingUrl={null}
                transcript={transcriptPayload}
                comments={[]}
                readOnly
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
    </LearnerDashboardShell>
  );
}
