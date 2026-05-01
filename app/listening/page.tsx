'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { AlertTriangle, ArrowRight, CheckCircle2, Clock, FileText, Headphones, History, MonitorCheck, Printer, Sparkles, Target, TrendingUp, Volume2 } from 'lucide-react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchMockReports } from '@/lib/api';
import { getListeningHome, type ListeningHomeDto, type ListeningHomePaperDto, type ListeningHomeTaskDto } from '@/lib/listening-api';
import { getListeningPathway, type ListeningPathwaySnapshot } from '@/lib/listening-authoring-api';
import type { MockReport } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

function titleCase(value: string | null | undefined) {
  if (!value) return 'Standard';
  return value.replace(/[_-]/g, ' ').replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function paperMeta(paper: ListeningHomePaperDto) {
  const missing = [
    !paper.assetReadiness.audio && 'audio',
    !paper.assetReadiness.questionPaper && 'question paper',
    !paper.assetReadiness.answerKey && 'answer key',
    !paper.assetReadiness.audioScript && 'audio script',
  ].filter(Boolean);

  if (missing.length > 0) return `Missing ${missing.join(', ')}`;
  if (!paper.objectiveReady) return 'Question map pending';
  return `${paper.questionCount || 42} authored items`;
}

function taskToCard(task: ListeningHomeTaskDto, index: number) {
  const card: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'indigo',
    eyebrow: task.scenarioType ? titleCase(task.scenarioType) : 'Demo Listening Task',
    eyebrowIcon: Volume2,
    title: task.title,
    description: 'Use the compatibility task while published Listening papers are being prepared.',
    metaItems: [
      { icon: Clock, label: `${task.estimatedDurationMinutes} mins` },
      { icon: Target, label: titleCase(task.difficulty) },
      { icon: FileText, label: `${task.questionCount} items` },
    ],
    primaryAction: {
      label: 'Start Task',
      href: `/listening/player/${task.contentId}?mode=practice`,
    },
    secondaryAction: {
      label: 'Exam Mode',
      href: `/listening/player/${task.contentId}?mode=exam`,
      variant: 'outline',
    },
  };

  return (
    <MotionItem key={task.contentId} delayIndex={index}>
      <LearnerSurfaceCard card={card} />
    </MotionItem>
  );
}

function modeLabel(mode: string) {
  if (mode === 'exam') return 'Exam Mode';
  if (mode === 'home') return 'OET@Home';
  if (mode === 'paper') return 'Paper Mode';
  return 'Practice Mode';
}

export default function ListeningHome() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const [home, setHome] = useState<ListeningHomeDto | null>(null);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [pathway, setPathway] = useState<ListeningPathwaySnapshot | null>(null);
  const [pathwayBusy, setPathwayBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    analytics.track('module_entry', { module: 'listening' });

    (async () => {
      try {
        setLoading(true);
        setError(null);
        const [listeningHome, reports] = await Promise.all([getListeningHome(), fetchMockReports()]);
        if (cancelled) return;
        setHome(listeningHome);
        setMockReports(reports);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load listening practice.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
      // Best-effort pathway fetch — never blocks the hub.
      void getListeningPathway()
        .then((snap) => { if (!cancelled) setPathway(snap); })
        .catch(() => { if (!cancelled) setPathway(null); });
    })();

    return () => {
      cancelled = true;
    };
  }, [authLoading, isAuthenticated]);

  const handlePathwayAction = useMemo(
    () => async () => {
      if (!pathway) return;
      const { nextAction } = pathway;
      setPathwayBusy(true);
      try {
        if (nextAction.route) {
          router.push(nextAction.route);
        }
      } finally {
        setPathwayBusy(false);
      }
    },
    [pathway, router],
  );

  const papers = home?.papers ?? [];
  const tasks = home?.featuredTasks ?? [];
  const activeAttempts = home?.activeAttempts ?? [];
  const recentResults = home?.recentResults ?? [];
  const safeDrills = useMemo(() => {
    const groups = Array.isArray(home?.drillGroups) ? home!.drillGroups : [];
    const distractor = Array.isArray(home?.distractorDrills) ? home!.distractorDrills : [];
    return groups.length ? groups : distractor;
  }, [home]);
  const firstDrill = safeDrills[0];
  const partCollections = Array.isArray(home?.partCollections) ? home!.partCollections : [];
  const mockSets = Array.isArray(home?.mockSets) ? home!.mockSets : [];
  const transcriptRoute = home?.transcriptBackedReview?.route ?? null;
  const hasPractice = papers.length + tasks.length > 0;

  const heroHighlights = useMemo(() => [
    {
      icon: Volume2,
      label: 'Published papers',
      value: loading ? 'Loading...' : papers.length ? `${papers.length} ready` : tasks.length ? 'Demo fallback' : 'None yet',
    },
    {
      icon: History,
      label: 'Resume',
      value: loading ? 'Loading...' : activeAttempts.length ? `${activeAttempts.length} active` : 'No active attempt',
    },
    {
      icon: FileText,
      label: 'Transcript review',
      value: loading ? 'Loading...' : transcriptRoute ? 'Latest result ready' : 'Available after submit',
    },
  ], [activeAttempts.length, loading, papers.length, tasks.length, transcriptRoute]);

  const transcriptCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: 'backend_summary',
    accent: 'amber',
    eyebrow: 'Transcript Policy',
    eyebrowIcon: FileText,
    title: transcriptRoute ? 'Open transcript-backed review' : 'Transcript review unlocks after an attempt',
    description: home?.accessPolicyHints?.rationale ?? 'Review transcript evidence only after you have committed to an answer.',
    metaItems: [
      { icon: CheckCircle2, label: home?.accessPolicyHints?.state === 'available' ? 'Available' : 'Post-attempt' },
      { icon: Target, label: home?.accessPolicyHints?.policy?.replace(/_/g, ' ') ?? 'per item policy' },
    ],
    primaryAction: transcriptRoute
      ? { label: 'Open Review', href: transcriptRoute }
      : { label: 'Start Listening', href: hasPractice ? (papers[0]?.route ?? `/listening/player/${tasks[0]?.contentId}`) : '/listening' },
    secondaryAction: {
      label: 'Open Mock Center',
      href: '/mocks',
      variant: 'outline',
    },
  };

  const drillCard: LearnerSurfaceCardModel = {
    kind: 'insight',
    sourceType: 'backend_summary',
    accent: 'rose',
    eyebrow: 'Focused Drill',
    eyebrowIcon: Target,
    title: firstDrill?.title ?? 'Build more reliable listening discrimination',
    description: firstDrill?.description ?? 'Train exact frequencies, plan changes, and clinical details before using another full mock.',
    metaItems: [
      { icon: Headphones, label: firstDrill ? firstDrill.focusLabel : 'Consultation audio' },
      { icon: Clock, label: firstDrill ? `${firstDrill.estimatedMinutes} mins` : 'Post-attempt review' },
    ],
  };

  return (
    <LearnerDashboardShell pageTitle="Listening" subtitle="Strengthen audio comprehension, exact detail capture, and distractor control.">
      <div className="space-y-10">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={Headphones}
          accent="indigo"
          title="Train listening accuracy before you test it under pressure"
          description={home?.intro ?? 'Use this workspace to tighten detail capture, review distractors with evidence, and move into transcript-backed follow-up when it helps.'}
          highlights={heroHighlights}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* ── Course pathway recommendation ─────────────────────────
            Joins completed-attempt, best-scaled, and mock signals into a
            single readiness stage with one concrete next step. */}
        {pathway ? (
          <section aria-label="Listening pathway">
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: pathway.stage === 'exam_ready' ? 'emerald' : 'amber',
                eyebrow: 'YOUR PATHWAY',
                eyebrowIcon: TrendingUp,
                title: pathway.headline,
                description:
                  pathway.bestScaledScore != null
                    ? `Best scaled score so far: ${pathway.bestScaledScore}/500.`
                    : 'A single recommended next step based on your recent Listening attempts.',
                metaItems: [
                  {
                    icon: CheckCircle2,
                    label: `${pathway.submittedAttempts} attempts · ${pathway.submittedListeningMockAttempts} mocks`,
                  },
                  pathway.bestScaledScore != null
                    ? { icon: Sparkles, label: `Best ${pathway.bestScaledScore}/500` }
                    : { icon: AlertTriangle, label: 'No graded score yet' },
                ],
              }}
            >
              <div className="mt-5 space-y-4">
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                  {pathway.milestones.map((m, milestoneIndex) => (
                    <div
                      key={m.code}
                      title={m.target != null && m.progress != null ? `${m.label} (${m.progress}/${m.target})` : m.label}
                      className={`flex min-h-14 items-center gap-2 rounded-2xl border px-3 py-2.5 shadow-sm ${
                        m.achieved
                          ? '!border-emerald-300 !bg-white !text-emerald-900 dark:!border-emerald-300/30 dark:!bg-slate-950 dark:!text-emerald-100'
                          : '!border-primary/25 !bg-white !text-slate-950 dark:!border-primary/30 dark:!bg-slate-950 dark:!text-white'
                      }`}
                    >
                      <span
                        className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-black ${
                          m.achieved ? '!bg-emerald-600 !text-white' : '!bg-primary/10 !text-primary ring-1 ring-primary/20 dark:!bg-white/10 dark:!text-primary'
                        }`}
                      >
                        {m.achieved ? <CheckCircle2 className="h-4 w-4" aria-hidden /> : milestoneIndex + 1}
                      </span>
                      <span className="min-w-0 text-xs font-black leading-snug">{m.label}</span>
                    </div>
                  ))}
                </div>
                <div className="flex justify-start sm:justify-end">
                  <Button
                    variant="primary"
                    size="md"
                    className="w-full justify-center sm:w-auto sm:min-w-72"
                    disabled={pathwayBusy}
                    onClick={() => void handlePathwayAction()}
                  >
                    {pathwayBusy ? 'Starting…' : pathway.nextAction.label}{' '}
                    <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
                  </Button>
                </div>
              </div>
            </LearnerSurfaceCard>
          </section>
        ) : null}

        {loading ? (
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            {[1, 2, 3, 4].map((item) => <Skeleton key={item} className="h-56 rounded-[24px]" />)}
          </div>
        ) : (
          <>
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Resume"
                title="Continue without losing your saved answers"
                description="Your progress is saved automatically — refresh or come back later, your work stays intact."
                className="mb-5"
              />
              {activeAttempts.length > 0 ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {activeAttempts.map((attempt, index) => {
                    const card: LearnerSurfaceCardModel = {
                      kind: 'task',
                      sourceType: 'backend_task',
                      accent: 'indigo',
                      eyebrow: modeLabel(attempt.mode),
                      eyebrowIcon: History,
                      title: attempt.paperTitle,
                      description: 'Resume from the latest server-saved answer state.',
                      metaItems: [
                        { icon: CheckCircle2, label: `${attempt.answeredCount} saved answers` },
                        { icon: Clock, label: attempt.lastClientSyncAt ? 'Recently saved' : 'Started' },
                      ],
                      primaryAction: { label: 'Resume Attempt', href: attempt.route },
                    };
                    return (
                      <MotionItem key={attempt.attemptId} delayIndex={index}>
                        <LearnerSurfaceCard card={card} />
                      </MotionItem>
                    );
                  })}
                </div>
              ) : (
                <div className="rounded-[24px] border border-dashed border-border bg-surface/80 p-6 text-sm text-muted">
                  <p>{home?.emptyStates.activeAttempts ?? 'No in-progress Listening attempt.'}</p>
                  <div className="mt-3 flex flex-wrap gap-4">
                    <Link href="/mocks" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Open Mock Center <ArrowRight className="h-4 w-4" /></Link>
                    <Link href="/study-plan" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">View Study Plan <ArrowRight className="h-4 w-4" /></Link>
                  </div>
                </div>
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Practice Papers"
                title="Open a listening task with a clear outcome"
                description="Full-length papers with timed playback, transcript reveal, and official OET scoring."
                className="mb-5"
              />

              {papers.length > 0 ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {papers.map((paper, index) => {
                    const card: LearnerSurfaceCardModel = {
                      kind: 'task',
                      sourceType: 'backend_task',
                      accent: paper.objectiveReady ? 'indigo' : 'amber',
                      eyebrow: paper.objectiveReady ? 'Published Paper' : 'Assets Ready',
                      eyebrowIcon: Volume2,
                      title: paper.title,
                      description: paper.objectiveReady
                        ? 'Start a server-authoritative Listening attempt with autosave and post-submit review.'
                        : 'Media is published, but graded practice waits for the structured question map.',
                      metaItems: [
                        { icon: Clock, label: `${paper.estimatedDurationMinutes} mins` },
                        { icon: Target, label: titleCase(paper.difficulty) },
                        { icon: FileText, label: paperMeta(paper) },
                      ],
                      primaryAction: {
                        label: paper.objectiveReady ? 'Start Practice' : 'Open Paper',
                        href: `${paper.route}?mode=practice`,
                      },
                      secondaryAction: paper.objectiveReady
                        ? { label: 'Exam Mode', href: `${paper.route}?mode=exam`, variant: 'outline' }
                        : undefined,
                    };
                    return (
                      <MotionItem key={paper.id} delayIndex={index}>
                        <LearnerSurfaceCard
                          card={card}
                          footer={paper.objectiveReady ? (
                            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                              <Link
                                href={`${paper.route}?mode=home`}
                                className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-medium text-navy transition hover:border-border-hover hover:bg-surface"
                              >
                                <MonitorCheck className="h-4 w-4" aria-hidden /> OET@Home
                              </Link>
                              <Link
                                href={`${paper.route}?mode=paper`}
                                className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border px-3 py-2 text-xs font-medium text-navy transition hover:border-border-hover hover:bg-surface"
                              >
                                <Printer className="h-4 w-4" aria-hidden /> Paper Mode
                              </Link>
                            </div>
                          ) : undefined}
                        />
                      </MotionItem>
                    );
                  })}
                </div>
              ) : tasks.length > 0 ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {tasks.map(taskToCard)}
                </div>
              ) : (
                <div className="rounded-[24px] border border-dashed border-border bg-surface/80 p-6 text-sm text-muted">
                  <p>{home?.emptyStates.papers ?? 'No published Listening papers are ready yet.'}</p>
                  <div className="mt-3 flex flex-wrap gap-4">
                    <Link href="/mocks" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Run a Full Mock <ArrowRight className="h-4 w-4" /></Link>
                    <Link href="/study-plan" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Review Study Plan <ArrowRight className="h-4 w-4" /></Link>
                  </div>
                </div>
              )}
            </section>

            {partCollections.length > 0 ? (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Part Focus"
                  title="Work on the parts that decide your grade"
                  description="Part A trains consultation-note detail capture. Parts B and C train purpose, attitude, and distractor control in workplace and presentation audio."
                  className="mb-5"
                />
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {partCollections.map((part, index) => {
                    const partCard: LearnerSurfaceCardModel = {
                      kind: 'navigation',
                      sourceType: 'backend_summary',
                      accent: index === 0 ? 'indigo' : 'amber',
                      eyebrow: part.available ? 'Ready to practise' : 'Awaiting published papers',
                      eyebrowIcon: Headphones,
                      title: part.title,
                      description: part.description,
                      metaItems: [
                        { icon: CheckCircle2, label: part.available ? 'Available' : 'Locked' },
                      ],
                      primaryAction: part.available && part.route
                        ? { label: 'Open Part Practice', href: part.route }
                        : { label: 'See Study Plan', href: '/study-plan', variant: 'outline' },
                    };
                    return (
                      <MotionItem key={part.id} delayIndex={index}>
                        <LearnerSurfaceCard card={partCard} />
                      </MotionItem>
                    );
                  })}
                </div>
              </section>
            ) : null}

            <section className="grid grid-cols-1 gap-8 lg:grid-cols-2">
              <LearnerSurfaceCard card={drillCard}>
                <ul className="space-y-3 text-sm text-navy/80">
                  {(firstDrill?.highlights ?? [
                    'Listen for exact frequencies, not approximate impressions.',
                    "Separate the patient's concern from the clinician's recommendation.",
                    'Use transcript-backed review after errors instead of replaying blindly.',
                  ]).map((highlight) => (
                    <li key={highlight}>{highlight}</li>
                  ))}
                </ul>
                <div className="mt-6">
                  <Link href={firstDrill?.launchRoute ?? '/listening'} className="inline-flex items-center gap-2 text-sm font-bold text-primary hover:underline">
                    Open focused drill <ArrowRight className="h-4 w-4" />
                  </Link>
                </div>
              </LearnerSurfaceCard>

              <LearnerSurfaceCard card={transcriptCard} />
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Recent Results"
                title="Track Listening against OET score rules"
                description="Results use raw /42, scaled /500, and Grade from the canonical scoring helper."
                className="mb-5"
              />
              {recentResults.length > 0 ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {recentResults.map((result, index) => {
                    const card: LearnerSurfaceCardModel = {
                      kind: 'evidence',
                      sourceType: 'backend_summary',
                      accent: result.passed ? 'emerald' : 'rose',
                      eyebrow: result.passed ? 'Grade B Threshold Met' : 'Below Grade B Threshold',
                      eyebrowIcon: FileText,
                      title: result.paperTitle,
                      description: result.scoreDisplay,
                      metaItems: [
                        { icon: FileText, label: `${result.rawScore}/${result.maxRawScore} raw` },
                        { icon: Target, label: `${result.scaledScore}/500` },
                        { icon: Sparkles, label: `Grade ${result.grade}` },
                      ],
                      primaryAction: { label: 'View Result', href: result.route, variant: 'outline' },
                    };
                    return (
                      <MotionItem key={result.attemptId} delayIndex={index}>
                        <LearnerSurfaceCard card={card} />
                      </MotionItem>
                    );
                  })}
                </div>
              ) : (
                <div className="rounded-[24px] border border-dashed border-border bg-surface/80 p-6 text-sm text-muted">
                  <p>{home?.emptyStates.recentResults ?? 'Complete a Listening task to unlock transcript-backed review and canonical OET score display.'}</p>
                  <div className="mt-3 flex flex-wrap gap-4">
                    <Link href="/mocks" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Open Mock Center <ArrowRight className="h-4 w-4" /></Link>
                    <Link href="/progress" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Track Progress <ArrowRight className="h-4 w-4" /></Link>
                  </div>
                </div>
              )}
            </section>

            {mockSets.length > 0 ? (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Mock Launchers"
                  title="Take Listening under full-mock conditions"
                  description="Launch a full OET practice or exam-mode mock, or run a sub-test mock to rehearse Listening with the same timer, controls, and policy."
                  className="mb-5"
                />
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {mockSets.map((mock, index) => {
                    const mockCard: LearnerSurfaceCardModel = {
                      kind: 'navigation',
                      sourceType: 'backend_summary',
                      accent: mock.mode === 'exam' ? 'rose' : 'indigo',
                      eyebrow: mock.mode === 'exam' ? 'Exam Mode' : 'Practice Mode',
                      eyebrowIcon: Sparkles,
                      title: mock.title,
                      description: mock.mode === 'exam'
                        ? 'Strict timer, one play, no scrub — closest to real OET conditions.'
                        : 'Timer relaxed, pause and scrub allowed — drill targeted Listening items.',
                      metaItems: [
                        { icon: Clock, label: mock.strictTimer ? 'Strict timer' : 'Relaxed timer' },
                      ],
                      primaryAction: { label: mock.mode === 'exam' ? 'Start Exam Mock' : 'Start Practice Mock', href: mock.route ?? '/mocks' },
                    };
                    return (
                      <MotionItem key={mock.id} delayIndex={index}>
                        <LearnerSurfaceCard card={mockCard} />
                      </MotionItem>
                    );
                  })}
                </div>
              </section>
            ) : null}

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Recent Mock Reports"
                title="Track listening impact inside full mocks"
                description="These reports help you see whether distractor control is improving when listening sits inside the full exam load."
                action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
                className="mb-5"
              />

              {mockReports.length > 0 ? (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
                  {mockReports.slice(0, 2).map((report, index) => {
                    const reportCard: LearnerSurfaceCardModel = {
                      kind: 'evidence',
                      sourceType: 'backend_summary',
                      accent: 'slate',
                      eyebrow: 'Mock Evidence',
                      eyebrowIcon: FileText,
                      title: report.title,
                      description: report.summary,
                      metaItems: [
                        { icon: Clock, label: report.date },
                        { icon: Target, label: report.overallScore },
                      ],
                      primaryAction: {
                        label: 'View Report',
                        href: `/mocks/report/${report.id}`,
                        variant: 'outline',
                      },
                    };

                    return (
                      <MotionItem key={report.id} delayIndex={index}>
                        <LearnerSurfaceCard card={reportCard} />
                      </MotionItem>
                    );
                  })}
                </div>
              ) : (
                <div className="rounded-[24px] border border-dashed border-border bg-surface/80 p-6 text-sm text-muted">
                  Complete a mock to see Listening transfer evidence here.
                </div>
              )}
            </section>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
