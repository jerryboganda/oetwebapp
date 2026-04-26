'use client';

import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Badge, StatusBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { MotionCollapse, MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchMockReports, fetchWritingHome, fetchWritingSubmissions, fetchWritingTasks } from '@/lib/api';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';
import type { MockReport, WritingSubmission, WritingTask } from '@/lib/mock-data';
import {
    ArrowRight, Award, BookOpen, Calendar, Clock, FileText, History, Layers, PenTool, RefreshCw, Star, Target
} from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useMemo, useState } from 'react';

type TabType = 'practice' | 'drills' | 'past';

interface WritingHomeActionDto { label: string; route: string }
interface WritingHomeLatestEvaluationDto {
  evaluationId?: string | null;
  scoreRange?: string | null;
  scaledScore?: number | null;
  grade?: string | null;
  passed?: boolean | null;
  criterionScores?: Array<{ criterionCode?: string; criterionLabel?: string; scoreRange?: string }>;
}
interface WritingHomeCriterionDrillDto {
  criterionCode?: string;
  criterionLabel?: string;
  rationale?: string;
  route?: string;
}
interface WritingHomeDto {
  recommendedTask?: Record<string, unknown> & { id?: string; contentId?: string; title?: string; criteriaFocus?: string | string[]; scenarioType?: string; profession?: string; time?: string; estimatedDurationMinutes?: number };
  reviewCredits?: { available?: number };
  fullMockEntry?: { title?: string; route?: string; rationale?: string };
  actions?: WritingHomeActionDto[];
  latestEvaluation?: WritingHomeLatestEvaluationDto | null;
  criterionDrillLibrary?: WritingHomeCriterionDrillDto[];
}

const filterGroups: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [
    { id: 'Nursing', label: 'Nursing' }, { id: 'Medicine', label: 'Medicine' },
    { id: 'Physiotherapy', label: 'Physiotherapy' }, { id: 'Dietetics', label: 'Dietetics' },
  ]},
  { id: 'difficulty', label: 'Difficulty', options: [
    { id: 'Easy', label: 'Easy' }, { id: 'Medium', label: 'Medium' }, { id: 'Hard', label: 'Hard' },
  ]},
];

export default function WritingHome() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<TabType>('practice');
  const [home, setHome] = useState<WritingHomeDto | null>(null);
  const [tasks, setTasks] = useState<WritingTask[]>([]);
  const [submissions, setSubmissions] = useState<WritingSubmission[]>([]);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFilters] = useState<Record<string, string[]>>({});

  useEffect(() => {
    analytics.track('module_entry', { module: 'writing' });
    Promise.all([fetchWritingHome(), fetchWritingTasks(), fetchWritingSubmissions(), fetchMockReports()])
      .then(([writingHome, writingTasks, writingSubmissions, reports]) => {
        setHome(writingHome as WritingHomeDto);
        setTasks(writingTasks);
        setSubmissions(writingSubmissions);
        setMockReports(Array.isArray(reports) ? reports : []);
      })
      .catch(() => setError('Failed to load writing tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  const handleFilterChange = useCallback((groupId: string, optionId: string) => {
    setFilters((prev) => {
      const current = prev[groupId] ?? [];
      return { ...prev, [groupId]: current.includes(optionId) ? current.filter((id) => id !== optionId) : [...current, optionId] };
    });
  }, []);

  const hasActiveFilters = Object.values(filters).some(arr => arr && arr.length > 0);
  const filteredTasks = tasks.filter((t) => {
    if (filters.profession?.length && !filters.profession.includes(t.profession)) return false;
    if (filters.difficulty?.length && !filters.difficulty.includes(t.difficulty)) return false;
    return true;
  });

  const recommended: (Record<string, unknown> & { id?: string; contentId?: string; title?: string; criteriaFocus?: string | string[]; scenarioType?: string; profession?: string; time?: string; estimatedDurationMinutes?: number; difficulty?: string }) | undefined =
    home?.recommendedTask ?? (tasks[0] as unknown as Record<string, unknown> & { id?: string; title?: string; criteriaFocus?: string | string[]; profession?: string; difficulty?: string });
  const credits = home?.reviewCredits?.available ?? 0;
  const fullMockEntry = home?.fullMockEntry;
  const latestEvaluation = home?.latestEvaluation ?? null;
  const criterionDrillLibrary = useMemo(
    () => (Array.isArray(home?.criterionDrillLibrary) ? home!.criterionDrillLibrary : []),
    [home],
  );
  const resumeAction = useMemo(() => {
    const action = home?.actions?.find((entry) => entry?.label?.toLowerCase().includes('resume'));
    if (!action?.route) return null;
    if (!action.route.includes('/writing/attempt/')) return null;
    return action;
  }, [home]);
  const recommendedCriteriaFocus = Array.isArray(recommended?.criteriaFocus)
    ? recommended.criteriaFocus.filter(Boolean).join(', ')
    : recommended?.criteriaFocus;
  const recommendedScenarioLabel = createLearnerMetaLabel(
    typeof recommended?.scenarioType === 'string'
      ? recommended.scenarioType
      : typeof recommended?.profession === 'string'
        ? recommended.profession
        : undefined,
    'Writing task',
  );
  const recommendedTimeLabel = createLearnerMetaLabel(
    typeof recommended?.time === 'string'
      ? recommended.time
      : typeof recommended?.estimatedDurationMinutes === 'number'
        ? `${recommended.estimatedDurationMinutes} mins`
        : undefined,
    'Practice session',
  );
  const recommendedTaskId = recommended?.id ?? recommended?.contentId;

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Writing">
        <div className="space-y-6">
          <Skeleton className="h-36 rounded-2xl" />
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <Skeleton className="h-72 rounded-2xl" />
            <Skeleton className="h-72 rounded-2xl" />
          </div>
          <Skeleton className="h-10 w-80" />
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  const recommendedCard = recommended ? ({
    kind: 'task',
    sourceType: home?.recommendedTask ? 'backend_summary' : 'backend_task',
    accent: 'amber',
    eyebrow: 'Recommended Next',
    eyebrowIcon: Star,
    title: recommended.title ?? 'Recommended writing task',
    description: `Focuses on ${recommendedCriteriaFocus ?? 'your current priority criteria'}, which is currently one of your most important writing areas.`,
    metaItems: [
      { icon: FileText, label: recommendedScenarioLabel },
      { icon: Clock, label: recommendedTimeLabel },
    ],
    primaryAction: {
      label: 'Start Recommended Task',
      href: recommendedTaskId ? `/writing/player?taskId=${recommendedTaskId}` : '/writing/library',
    },
  } satisfies LearnerSurfaceCardModel) : null;

  const mockCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: fullMockEntry ? 'backend_summary' : 'frontend_navigation',
    accent: 'primary',
    eyebrow: 'Exam Simulation',
    eyebrowIcon: Award,
    title: fullMockEntry?.title ?? 'Full Writing Mock Test',
    description: fullMockEntry?.rationale ?? 'Enter a timed writing flow to confirm whether practice gains hold up under realistic exam pressure.',
    metaItems: [
      { icon: Layers, label: 'Guided setup' },
      { icon: Clock, label: 'Timed flow' },
    ],
    primaryAction: {
      label: 'Enter Mock Flow',
      href: fullMockEntry?.route ?? '/mocks/setup',
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Writing">
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={PenTool}
          accent="amber"
          title="Choose the next writing task that moves your score"
          description="Use this workspace to pick the right letter, stay on criterion focus, and decide when expert review is worth spending."
          highlights={[
            { icon: Star, label: 'Recommended next', value: recommended ? 'Task ready' : 'Choose a task' },
            { icon: Award, label: 'Review credits', value: `${credits} available` },
            { icon: Clock, label: 'Mock flow', value: fullMockEntry ? 'Timed mock ready' : 'Browse mock setup' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* Resume Draft — backend tracks an in-progress Writing attempt for this learner */}
        {resumeAction ? (
          <MotionSection>
            <LearnerSurfaceCard card={{
              kind: 'task',
              sourceType: 'backend_task',
              accent: 'indigo',
              eyebrow: 'Resume Draft',
              eyebrowIcon: RefreshCw,
              title: 'Continue your in-progress letter',
              description: 'Your draft is autosaved to the server — pick up exactly where you stopped. The 45-minute timer resumes from its last synced value.',
              metaItems: [
                { icon: Clock, label: 'Autosaved' },
                { label: 'In progress' },
              ],
              primaryAction: { label: 'Resume Draft', href: resumeAction.route },
              secondaryAction: { label: 'Start a Fresh Task', href: '/writing/library', variant: 'outline' },
            }} />
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={1} className="rounded-[24px] border border-border bg-surface p-5 shadow-sm">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Rulebook Source of Truth</p>
              <h2 className="mt-2 text-lg font-black text-navy">Study the exact rules your writing is judged against</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
                The live checker in the writing player uses Dr. Hesham&apos;s rulebook rules directly. Open the rulebook to understand the real standard behind the feedback.
              </p>
            </div>

            <div className="flex flex-wrap gap-3">
              <Link href="/writing/rulebook">
                <Button variant="outline" className="whitespace-nowrap">
                  <BookOpen className="h-4 w-4" /> Open Rulebook
                </Button>
              </Link>
              <Link href="/writing/model">
                <Button variant="ghost" className="whitespace-nowrap">
                  Model Answers
                </Button>
              </Link>
            </div>
          </div>
        </MotionSection>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          {recommendedCard ? (
            <MotionSection>
              <LearnerSurfaceCard card={recommendedCard} />
            </MotionSection>
          ) : null}

          <MotionSection delayIndex={1}>
            <LearnerSurfaceCard card={mockCard} />
          </MotionSection>
        </div>

        {/* Latest Result — surfaces last evaluation with rubric breakdown */}
        {latestEvaluation && (latestEvaluation.scoreRange || latestEvaluation.scaledScore) ? (
          <MotionSection delayIndex={2}>
            <LearnerSurfaceSectionHeader
              eyebrow="Latest Result"
              title="Read your most recent rubric-graded evaluation"
              description="Open the full card to see per-criterion bands, rule-cited feedback, and the revision route."
              className="mb-4"
            />
            <LearnerSurfaceCard card={{
              kind: 'evidence',
              sourceType: 'backend_summary',
              accent: latestEvaluation.passed ? 'emerald' : 'rose',
              eyebrow: latestEvaluation.passed ? 'Pass Threshold Met' : 'Below Pass Threshold',
              eyebrowIcon: Award,
              title: latestEvaluation.scoreRange ?? (latestEvaluation.scaledScore ? `${latestEvaluation.scaledScore}/500` : 'Rubric-graded'),
              description: 'Your writing was graded on the 6 OET criteria: Purpose, Content, Conciseness, Genre, Organization, Language.',
              metaItems: [
                ...(latestEvaluation.grade ? [{ label: `Grade ${latestEvaluation.grade}` }] : []),
                ...(latestEvaluation.criterionScores?.slice(0, 3) ?? []).map((c) => ({
                  label: `${c.criterionLabel ?? c.criterionCode ?? 'Criterion'}: ${c.scoreRange ?? '—'}`,
                })),
              ],
              primaryAction: latestEvaluation.evaluationId
                ? { label: 'Open Result', href: `/writing/result/${latestEvaluation.evaluationId}` }
                : { label: 'View Submissions', href: '/writing', variant: 'outline' },
            }} />
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={2}>
          <LearnerSurfaceSectionHeader
            eyebrow="Writing Workspace"
            title="Choose practice, drills, or past evidence"
            description="The top cards show what to do next. The sections below let you expand into library work, criterion drills, or submission history."
            className="mb-4"
          />

          <div className="flex overflow-x-auto border-b border-gray-200 mb-6 scrollbar-hide -mx-1">
            {([
              { key: 'practice' as TabType, label: 'Practice Library', icon: FileText },
              { key: 'drills' as TabType, label: 'Criterion Drills', icon: Target },
              { key: 'past' as TabType, label: 'Past Submissions', icon: History },
            ]).map((tab) => (
              <button key={tab.key} onClick={() => setActiveTab(tab.key)}
                className={`whitespace-nowrap py-3 px-4 sm:py-4 sm:px-6 font-bold text-sm border-b-2 transition-colors flex items-center gap-2 ${activeTab === tab.key ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-navy'}`}>
                <tab.icon className="w-4 h-4" /> {tab.label}
              </button>
            ))}
          </div>

          <MotionCollapse open={activeTab !== 'past'}>
                <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange}
                  onClear={() => setFilters({})} className="mb-6 bg-surface p-4 rounded-xl border border-border shadow-sm" />
          </MotionCollapse>

          <div className="bg-surface rounded-2xl shadow-sm border border-border overflow-hidden">
            <AnimatePresence mode="wait">
              {activeTab === 'practice' && (
                <motion.div key="practice" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-border">
                  {filteredTasks.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title={hasActiveFilters ? 'No tasks match your filters' : 'No practice tasks available'} description={hasActiveFilters ? 'Try removing some filters to see more tasks.' : 'Check back later for new writing tasks.'} />
                    </div>
                  ) : filteredTasks.map((task) => (
                    <div key={task.id} className="p-5 hover:bg-background-light transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => { analytics.track('task_started', { taskId: task.id, subtest: 'writing' }); router.push(`/writing/player?taskId=${task.id}`); }}>
                      <div>
                        <h3 className="font-bold text-navy mb-1 group-hover:text-primary transition-colors">{task.title}</h3>
                        <div className="flex flex-wrap items-center gap-2 mt-1">
                          <Badge variant="muted" size="sm">{task.profession}</Badge>
                          <Badge variant={task.difficulty === 'Hard' ? 'danger' : task.difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{task.difficulty}</Badge>
                          <Badge variant="info" size="sm">{task.scenarioType}</Badge>
                        </div>
                      </div>
                      <Button size="sm" variant="outline">Start Practice</Button>
                    </div>
                  ))}
                </motion.div>
              )}

              {activeTab === 'drills' && (
                <motion.div key="drills" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-border">
                  {criterionDrillLibrary.length > 0 ? (
                    criterionDrillLibrary.map((drill, index) => (
                      <div
                        key={drill.criterionCode ?? `drill-${index}`}
                        className="p-5 hover:bg-background-light transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                        onClick={() => router.push(drill.route ?? '/writing/library')}
                      >
                        <div>
                          <div className="flex items-center gap-2 mb-1">
                            <Target className="w-4 h-4 text-amber-500" />
                            <h3 className="font-bold text-navy group-hover:text-primary transition-colors">
                              {drill.criterionLabel ?? drill.criterionCode ?? 'Criterion drill'}
                            </h3>
                            {index === 0 ? <Badge variant="warning" size="sm">Weakest</Badge> : null}
                          </div>
                          <p className="text-sm text-muted max-w-2xl">{drill.rationale ?? 'Target this criterion with a focused drill based on your most recent evaluation.'}</p>
                        </div>
                        <Button size="sm" variant="outline">Start Drill</Button>
                      </div>
                    ))
                  ) : filteredTasks.length === 0 ? (
                    <div className="p-10">
                      <EmptyState
                        title={hasActiveFilters ? 'No drills match your filters' : 'No criterion drills available'}
                        description={hasActiveFilters ? 'Try removing some filters to see more drills.' : 'Complete a writing task to unlock AI-driven criterion drills targeting your weakest areas.'}
                      />
                    </div>
                  ) : (
                    filteredTasks.map((task) => (
                      <div key={task.id} className="p-5 hover:bg-background-light transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer">
                        <div>
                          <div className="flex items-center gap-2 mb-1">
                            <Target className="w-4 h-4 text-amber-500" />
                            <h3 className="font-bold text-navy group-hover:text-primary transition-colors">{task.title}</h3>
                          </div>
                          <div className="flex flex-wrap items-center gap-2 mt-1">
                            <Badge variant="warning" size="sm">Focus: {task.criteriaFocus}</Badge>
                            <Badge variant={task.difficulty === 'Hard' ? 'danger' : task.difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{task.difficulty}</Badge>
                          </div>
                        </div>
                        <Button size="sm" variant="outline">Start Drill</Button>
                      </div>
                    ))
                  )}
                </motion.div>
              )}

              {activeTab === 'past' && (
                <motion.div key="past" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-border">
                  {submissions.length === 0 ? (
                    <div className="p-10">
                      <EmptyState
                        title="No submissions yet"
                        description="Complete a writing task to see your history here."
                        action={{ label: 'Browse Writing Library', onClick: () => router.push('/writing/library') }}
                      />
                    </div>
                  ) : submissions.map((sub) => (
                    <div key={sub.id} className="p-5 hover:bg-background-light transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => router.push(`/writing/result?id=${sub.id}`)}>
                      <div>
                        <h3 className="font-bold text-navy mb-1 group-hover:text-primary transition-colors">{sub.taskTitle}</h3>
                        <div className="flex items-center gap-3 text-sm text-muted">
                          <span className="flex items-center gap-1"><Calendar className="w-4 h-4" /> {new Date(sub.submittedAt).toLocaleDateString()}</span>
                          {sub.scoreEstimate && <span className="font-semibold text-navy">{sub.scoreEstimate}</span>}
                        </div>
                      </div>
                      <div className="flex items-center gap-3 shrink-0">
                        <StatusBadge status={sub.evalStatus === 'completed' ? 'completed' : sub.evalStatus === 'processing' ? 'processing' : 'queued'} />
                        <Button size="sm" variant="ghost">View <ArrowRight className="w-4 h-4" /></Button>
                      </div>
                    </div>
                  ))}
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </MotionSection>

        {/* Recent Mock Reports — cross-module evidence, matches Listening / Reading parity. */}
        <MotionSection delayIndex={3}>
          <LearnerSurfaceSectionHeader
            eyebrow="Recent Mock Reports"
            title="Track writing impact inside full mocks"
            description="Confirm whether criterion gains from isolated practice are transferring under full-exam pressure."
            action={<Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>}
            className="mb-4"
          />
          {mockReports.length > 0 ? (
            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              {mockReports.slice(0, 2).map((report, index) => (
                <MotionItem key={report.id} delayIndex={index}>
                  <LearnerSurfaceCard card={{
                    kind: 'evidence',
                    sourceType: 'backend_summary',
                    accent: 'slate',
                    eyebrow: 'Mock Evidence',
                    eyebrowIcon: FileText,
                    title: report.title,
                    description: report.summary,
                    metaItems: [
                      { label: report.date },
                      { label: report.overallScore },
                    ],
                    primaryAction: { label: 'View Report', href: `/mocks/report/${report.id}`, variant: 'outline' },
                  }} />
                </MotionItem>
              ))}
            </div>
          ) : (
            <div className="rounded-[24px] border border-dashed border-gray-200 bg-surface/80 p-6 text-sm text-muted">
              <p>Complete a mock to see writing transfer evidence here.</p>
              <div className="mt-3 flex flex-wrap gap-4">
                <Link href="/mocks" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Open Mock Center <ArrowRight className="h-4 w-4" /></Link>
                <Link href="/progress" className="inline-flex items-center gap-1 font-bold text-primary hover:underline">Track Progress <ArrowRight className="h-4 w-4" /></Link>
              </div>
            </div>
          )}
        </MotionSection>
      </div>
    </LearnerDashboardShell>
  );
}
