'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Clock, FileText, PlayCircle, ShieldCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { completeMockSection, fetchMockSession, startMockSection, submitMockSession } from '@/lib/api';
import type { MockSession } from '@/lib/mock-data';
import {
  MOCK_REVIEW_RELEASE_STEPS,
  getMockModePolicy,
  getMockSectionPolicy,
  getMockSubmissionReadiness,
  isTeacherMarkedSubtest,
} from '@/lib/mocks/workflow';
import { useMockProctoring } from '@/lib/hooks/use-mock-proctoring';

export default function MockPlayerPage() {
  const params = useParams<{ id: string }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const sessionId = params?.id;
  const selectedSectionId = searchParams?.get('section') ?? null;

  const [session, setSession] = useState<MockSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [launchingSectionId, setLaunchingSectionId] = useState<string | null>(null);
  const [completingSectionId, setCompletingSectionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    analytics.track('content_view', { page: 'mock-player', sessionId });
    fetchMockSession(sessionId)
      .then(setSession)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this mock session.'))
      .finally(() => setLoading(false));
  }, [sessionId]);

  const selectedSection = useMemo(
    () => session?.sectionStates.find((section) => section.id === selectedSectionId) ?? session?.sectionStates[0] ?? null,
    [selectedSectionId, session],
  );
  const modePolicy = session ? getMockModePolicy(session.config.mode) : null;
  const selectedSectionPolicy = session && selectedSection ? getMockSectionPolicy(selectedSection.subtest, session.config.mode) : null;
  const submissionReadiness = session ? getMockSubmissionReadiness(session) : null;
  const proctoring = useMockProctoring({
    attemptId: session?.sessionId ?? null,
    sectionAttemptId: selectedSection?.sectionAttemptId ?? selectedSection?.id ?? null,
    enabled: Boolean(session && (session.config.mode === 'exam' || session.config.strictness === 'exam' || session.config.strictness === 'final_readiness')),
    blockPaste: Boolean(session && session.config.strictness !== 'learning'),
  });

  const handleSubmit = async () => {
    if (!session) return;
    const readiness = getMockSubmissionReadiness(session);
    if (!readiness.canSubmit) {
      setError(readiness.message);
      return;
    }
    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      await submitMockSession(session.sessionId);
      setSuccess('Mock submitted. The report is being generated.');
      const refreshed = await fetchMockSession(session.sessionId);
      setSession(refreshed);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit this mock yet.');
    } finally {
      setSubmitting(false);
    }
  };

  const refreshSession = async () => {
    if (!session) return null;
    const refreshed = await fetchMockSession(session.sessionId);
    setSession(refreshed);
    return refreshed;
  };

  const handleLaunchSection = async () => {
    if (!session || !selectedSection) return;
    setLaunchingSectionId(selectedSection.id);
    setError(null);
    try {
      const started = await startMockSection(session.sessionId, selectedSection.id);
      await refreshSession();
      router.push(started.launchRoute);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start this section.');
    } finally {
      setLaunchingSectionId(null);
    }
  };

  const handleCompleteSection = async () => {
    if (!session || !selectedSection) return;
    setCompletingSectionId(selectedSection.id);
    setError(null);
    setSuccess(null);
    try {
      await completeMockSection(session.sessionId, selectedSection.id, {
        evidence: { source: 'mock_player_confirmation', completedAt: new Date().toISOString() },
      });
      await refreshSession();
      setSuccess(`${selectedSection.title} recorded as completed.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not complete this section.');
    } finally {
      setCompletingSectionId(null);
    }
  };

  const handleReportAudioIssue = async () => {
    proctoring.report('audio_issue_reported', {
      severity: 'warning',
      metadata: { sectionId: selectedSection?.id, subtest: selectedSection?.subtest, source: 'mock_player_button' },
    });
    await proctoring.flush();
    setSuccess('Audio issue recorded for tutor/admin review. You can continue; proctoring signals never block submission automatically.');
  };

  return (
    <LearnerDashboardShell pageTitle="Run Your Mock" subtitle="Start, resume, and submit your mock from one place." backHref="/mocks">
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/mocks')}>
          <ArrowLeft className="h-4 w-4" />
          Back to mock center
        </Button>

        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {!loading && success ? <InlineAlert variant="success">{success}</InlineAlert> : null}

        {!loading && session ? (
          <>
            <LearnerPageHero
              eyebrow="Mock Flow"
              icon={FileText}
              accent="navy"
              title={session.config.title}
              description="Your mock setup — exam mode, timer, and review selection — stays in effect throughout."
              highlights={[
                { icon: FileText, label: 'Profession', value: session.config.profession },
                { icon: Clock, label: 'Mode', value: session.config.mode === 'exam' ? 'Exam simulation' : 'Practice simulation' },
                { icon: CheckCircle2, label: 'Delivery', value: session.config.deliveryMode?.replace(/_/g, ' ') ?? 'computer' },
              ]}
            />

            {session.reviewReservation ? (
              <section className="rounded-2xl border border-warning/30 bg-warning/10 p-4 text-sm text-warning">
                <p className="text-[11px] font-black uppercase tracking-widest">Review reservation</p>
                <p className="mt-1">
                  {session.reviewReservation.pendingCredits} pending / {session.reviewReservation.consumedCredits} consumed / state {session.reviewReservation.state.replace(/_/g, ' ')}
                </p>
              </section>
            ) : null}

            <section className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
              <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Section States"
                  title="Every mock section now has a real launch path"
                  description="See the section order, whether tutor review is attached, and where each section starts."
                  className="mb-4"
                />

                <div className="space-y-3">
                  {session.sectionStates.map((section) => (
                    <button
                      key={section.id}
                      onClick={() => router.push(`/mocks/player/${session.sessionId}?section=${section.id}`)}
                      className={`flex w-full items-center justify-between rounded-2xl border p-4 text-left transition-colors ${
                        selectedSection?.id === section.id
                          ? 'border-primary bg-primary/5'
                          : 'border-border bg-surface hover:border-border-hover'
                      }`}
                    >
                      <div>
                        <p className="text-sm font-bold text-navy">{section.title}</p>
                        <p className="mt-1 text-xs text-muted">
                          State: {section.state.replace(/_/g, ' ')} / {section.timeLimitMinutes ?? 'Timed'} min / Review {section.reviewSelected ? 'attached' : 'not attached'}
                        </p>
                      </div>
                      <span className="rounded-full bg-background-light px-3 py-1 text-xs font-black uppercase tracking-widest text-muted">
                        {section.subtest ?? section.id}
                      </span>
                    </button>
                  ))}
                </div>
              </div>

              <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
                <LearnerSurfaceSectionHeader
                  eyebrow="Current Launch"
                  title={selectedSection?.title ?? 'Select a section'}
                  description="Track your mock progress before launching each section."
                  className="mb-4"
                />

                {selectedSection ? (
                  <div className="space-y-4">
                    <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                      <p>Strict timer: {session.config.strictTimer ? 'Enabled' : 'Flexible'}</p>
                      <p className="mt-1">Strictness: {session.config.strictness?.replace(/_/g, ' ') ?? session.config.mode}</p>
                      <p className="mt-1">Delivery: {session.config.deliveryMode?.replace(/_/g, ' ') ?? 'computer'}</p>
                      <p className="mt-1">Review attached: {selectedSection.reviewSelected ? 'Yes' : 'No'}</p>
                      <p className="mt-1">Content: {selectedSection.contentPaperTitle ?? selectedSection.contentPaperId ?? 'Published section'}</p>
                      <p className="mt-1 break-all">Launch path: <span className="font-mono text-[11px]">{selectedSection.launchRoute}</span></p>
                    </div>

                    {selectedSectionPolicy ? (
                      <div className="rounded-2xl border border-border bg-background-light p-4">
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge variant="info" size="sm">{selectedSectionPolicy.label}</Badge>
                          <Badge variant={session.config.mode === 'exam' ? 'warning' : 'success'} size="sm">
                            {session.config.mode === 'exam' ? 'Strict mock' : 'Practice mock'}
                          </Badge>
                          {isTeacherMarkedSubtest(selectedSection.subtest) ? (
                            <Badge variant={selectedSection.reviewSelected ? 'success' : 'warning'} size="sm">
                              {selectedSection.reviewSelected ? 'Teacher review queued' : 'Teacher review recommended'}
                            </Badge>
                          ) : null}
                        </div>
                        <p className="mt-3 text-sm font-bold text-navy">{selectedSectionPolicy.timing}</p>
                        <p className="mt-1 text-sm leading-6 text-muted">{selectedSectionPolicy.examRule}</p>
                        <p className="mt-1 text-sm leading-6 text-muted">{selectedSectionPolicy.reviewRule}</p>
                      </div>
                    ) : null}

                    {submissionReadiness ? (
                      <InlineAlert variant={submissionReadiness.canSubmit ? 'success' : 'warning'}>
                        {submissionReadiness.completedCount}/{submissionReadiness.totalCount} sections recorded. {submissionReadiness.message}
                      </InlineAlert>
                    ) : null}

                    <Button fullWidth onClick={handleLaunchSection} loading={launchingSectionId === selectedSection.id}>
                      <PlayCircle className="h-4 w-4" />
                      Launch section workspace
                    </Button>

                    {selectedSection.subtest === 'listening' ? (
                      <Button variant="secondary" fullWidth onClick={handleReportAudioIssue}>
                        Report audio issue
                      </Button>
                    ) : null}

                    {selectedSection.state !== 'completed' ? (
                      <Button variant="secondary" fullWidth onClick={handleCompleteSection} loading={completingSectionId === selectedSection.id}>
                        <CheckCircle2 className="h-4 w-4" />
                        Record section completion
                      </Button>
                    ) : null}

                    <Button variant="outline" fullWidth onClick={handleSubmit} loading={submitting}>
                      Submit mock for report generation
                    </Button>

                    {session.reportRoute ? (
                      <Button variant="ghost" fullWidth onClick={() => router.push(session.reportRoute!)}>
                        View mock report
                      </Button>
                    ) : (
                      <p className="text-sm text-muted">A report link appears here after submission completes.</p>
                    )}
                  </div>
                ) : (
                  <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                    Select a section to continue.
                  </div>
                )}
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Why this matters"
                title="The mock setup is now honored end to end"
                description="Your exam mode, timer, and review selection stay visible throughout the mock."
                className="mb-4"
              />
              <div className="grid gap-4 md:grid-cols-3">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Mode</p>
                  <p className="mt-2 text-sm font-bold text-navy">{session.config.mode === 'exam' ? 'Exam simulation' : 'Practice mode'}</p>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Timer</p>
                  <p className="mt-2 text-sm font-bold text-navy">{session.config.strictTimer ? 'Strict timer active' : 'Flexible timing'}</p>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Review selection</p>
                  <p className="mt-2 text-sm font-bold text-navy">{session.config.reviewSelection.replace(/_/g, ' ')}</p>
                </div>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Attempt → Review → Remediation"
                title="Final reports are released in the right order"
                description="Auto-scored sections can appear quickly, while Writing and Speaking wait for teacher review when selected."
                className="mb-4"
              />
              <div className="grid gap-4 lg:grid-cols-[0.85fr_1.15fr]">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <div className="flex items-start gap-3">
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                      <ShieldCheck className="h-5 w-5" />
                    </div>
                    <div>
                      <p className="text-sm font-black text-navy">{modePolicy?.label ?? 'Mock workflow policy'}</p>
                      <p className="mt-1 text-sm leading-6 text-muted">{modePolicy?.description}</p>
                    </div>
                  </div>
                </div>
                <ol className="grid gap-3 sm:grid-cols-2">
                  {MOCK_REVIEW_RELEASE_STEPS.map((step, index) => (
                    <li key={step} className="rounded-2xl border border-border bg-background-light p-4">
                      <span className="text-[10px] font-black uppercase tracking-widest text-primary">Step {index + 1}</span>
                      <p className="mt-1 text-sm leading-6 text-navy">{step}</p>
                    </li>
                  ))}
                </ol>
              </div>
            </section>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
