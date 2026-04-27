'use client';

import { useState, useEffect, useCallback, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import Link from 'next/link';
import {
  PenTool,
  Star,
  FileText,
  Clock,
  Layers,
  Target,
  History,
  Award,
  ArrowRight,
  Calendar,
  BookOpen,
  RefreshCw,
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { MotionSection, MotionItem, MotionCollapse } from '@/components/ui/motion-primitives';
import { Badge, StatusBadge } from '@/components/ui/badge';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingHome, fetchWritingTasks, fetchWritingSubmissions, fetchMockReports } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import type { WritingTask, WritingSubmission, MockReport } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { useProfessions } from '@/lib/hooks/use-professions';

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

const difficultyFilterGroup: FilterGroup = { id: 'difficulty', label: 'Difficulty', options: [
  { id: 'Easy', label: 'Easy' }, { id: 'Medium', label: 'Medium' }, { id: 'Hard', label: 'Hard' },
]};

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
  const { professions } = useProfessions();
  const filterGroups = useMemo<FilterGroup[]>(() => [
    {
      id: 'profession',
      label: 'Profession',
      options: professions.map((p) => ({ id: p.label, label: p.label })),
    },
    difficultyFilterGroup,
  ], [professions]);

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
          description="Pick the right letter task, focus on the criteria that matter, and request expert review when it counts."
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
                { icon: RefreshCw, label: 'In progress' },
              ],
              primaryAction: { label: 'Resume Draft', href: resumeAction.route },
              secondaryAction: { label: 'Start a Fresh Task', href: '/writing/library', variant: 'outline' },
            }} />
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={1}>
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'indigo',
              eyebrow: 'Rulebook',
              eyebrowIcon: BookOpen,
              title: 'Know exactly how your writing is judged',
              description: 'The same rules the live checker applies to your draft. Open the rulebook to see the criteria behind every score and tip.',
              metaItems: [
                { icon: FileText, label: 'Full criteria' },
                { icon: Star, label: 'Model answers' },
              ],
              primaryAction: {
                label: 'Open Rulebook',
                href: '/writing/rulebook',
              },
              secondaryAction: {
                label: 'Model Answers',
                href: '/writing/model',
                variant: 'outline',
              },
            }}
          />
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
              description="Open the full card to see per-criterion bands, detailed feedback, and your revision options."
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
            description="The cards above show your next move. Below you'll find the full task library, targeted drills, and your submission history."
            className="mb-4"
          />

          <div className="flex overflow-x-auto border-b border-border mb-6 scrollbar-hide -mx-1">
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
            description="See whether your practice gains hold up under real exam pressure."
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
                      { icon: Calendar, label: report.date },
                      { icon: Target, label: report.overallScore },
                    ],
                    primaryAction: { label: 'View Report', href: `/mocks/report/${report.id}`, variant: 'outline' },
                  }} />
                </MotionItem>
              ))}
            </div>
          ) : (
            <div className="rounded-[24px] border border-dashed border-border bg-surface/80 p-6 text-sm text-muted">
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
