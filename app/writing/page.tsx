'use client';

import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import Link from 'next/link';
import {
  PenTool,
  Star,
  PlayCircle,
  FileText,
  Clock,
  Layers,
  Target,
  History,
  Award,
  ArrowRight,
  Calendar,
  BookOpen,
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { MotionSection, MotionItem, MotionCollapse } from '@/components/ui/motion-primitives';
import { Badge, StatusBadge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingHome, fetchWritingTasks, fetchWritingSubmissions, type WritingCriterionDrill, type WritingHome } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import type { WritingTask, WritingSubmission } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { createLearnerMetaLabel, type LearnerSurfaceCardModel } from '@/lib/learner-surface';

type TabType = 'practice' | 'drills' | 'past';

function canonicalPlayerRoute(task: WritingTask, criterionCode?: string) {
  const route = task.route ?? `/writing/player?taskId=${encodeURIComponent(task.contentId ?? task.id)}`;
  if (!criterionCode) return route;
  const separator = route.includes('?') ? '&' : '?';
  return `${route}${separator}criterion=${encodeURIComponent(criterionCode)}`;
}

function drillRoute(drill: WritingCriterionDrill, fallbackTask?: WritingTask) {
  if (drill.route) return drill.route;
  const taskId = drill.contentId ?? drill.taskId ?? fallbackTask?.contentId ?? fallbackTask?.id;
  if (!taskId) return '/writing/library';
  const params = new URLSearchParams({ taskId });
  if (drill.criterionCode) params.set('criterion', drill.criterionCode);
  return `/writing/player?${params.toString()}`;
}

export default function WritingHome() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<TabType>('practice');
  const [home, setHome] = useState<WritingHome | null>(null);
  const [tasks, setTasks] = useState<WritingTask[]>([]);
  const [submissions, setSubmissions] = useState<WritingSubmission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFilters] = useState<Record<string, string[]>>({});

  useEffect(() => {
    analytics.track('module_entry', { module: 'writing' });
    Promise.all([fetchWritingHome(), fetchWritingTasks(), fetchWritingSubmissions()])
      .then(([writingHome, writingTasks, writingSubmissions]) => {
        setHome(writingHome);
        setTasks(writingTasks);
        setSubmissions(writingSubmissions);
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

  const practiceLibrary = (home?.practiceLibrary?.length ? home.practiceLibrary : tasks) as WritingTask[];
  const drillLibrary = home?.criterionDrillLibrary ?? [];
  const pastSubmissions = (home?.pastSubmissions?.length ? home.pastSubmissions : submissions) as WritingSubmission[];
  const hasActiveFilters = Object.values(filters).some(arr => arr && arr.length > 0);
  const filterGroups: FilterGroup[] = [
    {
      id: 'profession',
      label: 'Profession',
      options: Array.from(new Set(practiceLibrary.map((task) => task.profession).filter(Boolean)))
        .sort()
        .map((value) => ({ id: value, label: value })),
    },
    {
      id: 'difficulty',
      label: 'Difficulty',
      options: Array.from(new Set(practiceLibrary.map((task) => task.difficulty).filter(Boolean)))
        .sort()
        .map((value) => ({ id: value, label: value })),
    },
  ].filter((group) => group.options.length > 0);
  const filteredTasks = practiceLibrary.filter((t) => {
    if (filters.profession?.length && !filters.profession.includes(t.profession)) return false;
    if (filters.difficulty?.length && !filters.difficulty.includes(t.difficulty)) return false;
    return true;
  });
  const fallbackDrills: WritingCriterionDrill[] = filteredTasks.slice(0, 3).map((task) => ({
    criterionCode: task.criteriaFocusCodes?.[0],
    criterionLabel: task.criteriaFocus || 'Criterion focus',
    title: task.title,
    rationale: `Practise this task with a narrow focus on ${task.criteriaFocus || 'one Writing criterion'}.`,
    route: canonicalPlayerRoute(task, task.criteriaFocusCodes?.[0]),
    contentId: task.contentId ?? task.id,
  }));
  const visibleDrills = drillLibrary.length > 0 ? drillLibrary : fallbackDrills;

  const recommended = (home?.recommendedTask as WritingTask | undefined) ?? practiceLibrary[0];
  const credits = home?.reviewCredits?.available ?? 0;
  const fullMockEntry = home?.fullMockEntry;
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

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Writing">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
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
    title: recommended.title,
    description: `Focuses on ${recommendedCriteriaFocus ?? 'your current priority criteria'}, which is currently one of your most important writing areas.`,
    metaItems: [
      { icon: FileText, label: recommendedScenarioLabel },
      { icon: Clock, label: recommendedTimeLabel },
    ],
    primaryAction: {
      label: 'Start Recommended Task',
      href: recommended.route ?? (recommendedTaskId ? `/writing/player?taskId=${recommendedTaskId}` : '/writing/library'),
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

        <MotionSection delayIndex={1} className="rounded-[24px] border border-gray-200 bg-white p-5 shadow-sm">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Rulebook Source of Truth</p>
              <h2 className="mt-2 text-lg font-black text-navy">Study the exact rules your writing is judged against</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
                The live checker in the writing player uses Dr. Hesham&apos;s rulebook rules directly. Open any rule page to understand the real standard behind the feedback.
              </p>
            </div>

            <div className="flex flex-wrap gap-3">
              <Link href="/writing/rulebook/R03.4">
                <Button variant="outline" className="whitespace-nowrap">
                  <BookOpen className="h-4 w-4" /> Writing rules
                </Button>
              </Link>
              <Link href="/writing/rulebook/R14.2">
                <Button variant="ghost" className="whitespace-nowrap">
                  Discharge template rule
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

          <MotionCollapse open={activeTab === 'practice'}>
                <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange}
                  onClear={() => setFilters({})} className="mb-6 bg-white p-4 rounded-xl border border-gray-200 shadow-sm" />
          </MotionCollapse>

          <div className="bg-white rounded-2xl shadow-sm border border-gray-200 overflow-hidden">
            <AnimatePresence mode="wait">
              {activeTab === 'practice' && (
                <motion.div key="practice" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {filteredTasks.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title={hasActiveFilters ? 'No tasks match your filters' : 'No practice tasks available'} description={hasActiveFilters ? 'Try removing some filters to see more tasks.' : 'Check back later for new writing tasks.'} />
                    </div>
                  ) : filteredTasks.map((task) => (
                    <div key={task.id} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => { analytics.track('task_started', { taskId: task.id, subtest: 'writing' }); router.push(canonicalPlayerRoute(task)); }}>
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
                <motion.div key="drills" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {visibleDrills.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title="No criterion drills available" description="Complete a writing task to unlock targeted criterion drills." />
                    </div>
                  ) : visibleDrills.map((drill, index) => {
                    const fallbackTask = filteredTasks.find((task) => task.id === drill.contentId || task.id === drill.taskId) ?? filteredTasks[0];
                    const route = drillRoute(drill, fallbackTask);
                    return (
                    <div key={drill.route ?? drill.criterionCode ?? index} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => { analytics.track('task_started', { subtest: 'writing', mode: 'criterion-drill', criterionCode: drill.criterionCode, route }); router.push(route); }}>
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <Target className="w-4 h-4 text-amber-500" />
                          <h3 className="font-bold text-navy group-hover:text-primary transition-colors">{drill.title ?? drill.criterionLabel ?? fallbackTask?.title ?? 'Criterion drill'}</h3>
                        </div>
                        {drill.rationale ? <p className="max-w-2xl text-sm leading-6 text-muted">{drill.rationale}</p> : null}
                        <div className="flex flex-wrap items-center gap-2 mt-1">
                          <Badge variant="warning" size="sm">Focus: {drill.criterionLabel ?? drill.criterionCode ?? fallbackTask?.criteriaFocus ?? 'Criterion'}</Badge>
                          {fallbackTask ? <Badge variant={fallbackTask.difficulty === 'Hard' ? 'danger' : fallbackTask.difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{fallbackTask.difficulty}</Badge> : null}
                        </div>
                      </div>
                      <Button size="sm" variant="outline">Start Drill</Button>
                    </div>
                  );})}
                </motion.div>
              )}

              {activeTab === 'past' && (
                <motion.div key="past" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {pastSubmissions.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title="No submissions yet" description="Complete a writing task to see your history here." />
                    </div>
                  ) : pastSubmissions.map((sub) => {
                    const route = sub.route ?? (sub.evalStatus === 'completed'
                      ? `/writing/result?id=${encodeURIComponent(sub.id)}`
                      : `/writing/player?taskId=${encodeURIComponent(sub.taskId)}${sub.attemptId ? `&attemptId=${encodeURIComponent(sub.attemptId)}` : ''}`);
                    return (
                    <div key={sub.id} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => router.push(route)}>
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
                  );})}
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </MotionSection>
      </div>
    </LearnerDashboardShell>
  );
}
