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
  type TimestampedComment,
} from '@/lib/api/speaking-assessments';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

interface SessionContext {
  recordingUrl?: string | null;
  transcript: TranscriptPayload;
  comments: TimestampedComment[];
}

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

export default function SpeakingSessionResultsPage() {
  const params = useParams();
  const rawId = params?.id;
  const sessionId = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  const [data, setData] = useState<DualAssessmentResponse | null>(null);
  const [context, setContext] = useState<SessionContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState('overview');
  const trackedAiAssessmentRef = useRef(false);
  const trackedTutorAssessmentRef = useRef(false);

  const load = useCallback(async () => {
    if (!sessionId) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const response = await learnerGetDualAssessment(sessionId);
      setData(response);
      const ctx = (response as unknown as { context?: SessionContext }).context;
      setContext(
        ctx ?? {
          recordingUrl: null,
          transcript: { segments: [] },
          comments: [],
        },
      );
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to load assessment.';
      setErrorMsg(msg);
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    if (data?.ai && !trackedAiAssessmentRef.current) {
      trackedAiAssessmentRef.current = true;
      trackSpeaking('ai_assessment_viewed', {
        sessionId,
        estimatedBand: data.ai.readinessBand,
      });
    }
    if (data?.tutor && !trackedTutorAssessmentRef.current) {
      trackedTutorAssessmentRef.current = true;
      trackSpeaking('tutor_assessment_viewed', {
        sessionId,
        estimatedBand: data.tutor.readinessBand,
      });
    }
  }, [data, sessionId]);

  useEffect(() => {
    void load();
    // Poll once a minute while AI is still processing (`ai === null`)
    const interval = window.setInterval(() => {
      if (!data?.ai) void load();
    }, 60_000);
    return () => window.clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  const drills = useMemo(() => {
    const all = new Set<string>();
    data?.ai?.recommendedDrills?.forEach((d) => all.add(d));
    data?.tutor?.recommendedDrills?.forEach((d) => all.add(d));
    return Array.from(all);
  }, [data?.ai?.recommendedDrills, data?.tutor?.recommendedDrills]);

  const tabs = useMemo(
    () => [
      { id: 'overview', label: 'Overview', icon: <Mic className="h-4 w-4" aria-hidden /> },
      { id: 'transcript', label: 'Transcript', icon: <Loader2 className="hidden" aria-hidden /> },
      { id: 'drills', label: 'Recommended drills', icon: <BookOpen className="h-4 w-4" aria-hidden />, count: drills.length || undefined },
    ],
    [drills.length],
  );

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

  return (
    <LearnerDashboardShell
      pageTitle="Speaking results"
      subtitle={`Session ${sessionId.slice(0, 8)}…`}
    >
      <div className="flex flex-col gap-4">
        <Tabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} />

        <TabPanel id="overview" activeTab={activeTab}>
          <DualAssessmentLayout
            data={data}
            tutorPlaceholderCta={<TutorReviewCta />}
            aiPlaceholderCta={<AiProcessingCta />}
          />
        </TabPanel>

        <TabPanel id="transcript" activeTab={activeTab}>
          <TranscriptPlayerWithComments
            recordingUrl={context?.recordingUrl ?? null}
            transcript={context?.transcript ?? { segments: [] }}
            comments={context?.comments ?? []}
            readOnly
          />
        </TabPanel>

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
      </div>
    </LearnerDashboardShell>
  );
}
