'use client';

import { useState, useEffect, useCallback } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowRight,
  PlayCircle,
  Calendar,
  RefreshCw,
  CheckCircle2,
  Info,
  Clock,
  AlertTriangle,
  BookOpen,
  Headphones,
  FilePenLine,
  Mic,
  ChevronDown,
  ChevronUp,
  Target,
} from 'lucide-react';
import { Button, Card } from '@/components/ui';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionCollapse, MotionItem } from '@/components/ui/motion-primitives';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchStudyPlan, updateStudyPlanTask } from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';

const SUBTEST_ICONS: Record<SubTest, React.ElementType> = {
  Reading: BookOpen,
  Listening: Headphones,
  Writing: FilePenLine,
  Speaking: Mic,
};

// Sub-test identity (DESIGN.md §2 skill tokens), never a status colour.
const SUBTEST_COLORS: Record<SubTest, string> = {
  Reading: 'bg-skill-reading/10 text-skill-reading border-skill-reading/20',
  Listening: 'bg-skill-listening/10 text-skill-listening border-skill-listening/20',
  Writing: 'bg-skill-writing/10 text-skill-writing border-skill-writing/20',
  Speaking: 'bg-skill-speaking/10 text-skill-speaking border-skill-speaking/20',
};

type SectionType = 'today' | 'thisWeek' | 'nextCheckpoint' | 'weakSkillFocus';

const SECTIONS: { type: SectionType; title: string; icon: React.ElementType; eyebrow: string; description: string }[] = [
  { type: 'today', title: 'Today', icon: Calendar, eyebrow: 'Today', description: 'These are the tasks scheduled for today. Complete them in the order shown to keep momentum.' },
  { type: 'thisWeek', title: 'This Week', icon: Calendar, eyebrow: 'This Week', description: 'Upcoming work for the rest of this week. Start any of these early if today is light.' },
  { type: 'nextCheckpoint', title: 'Next Checkpoint', icon: Target, eyebrow: 'Next Checkpoint', description: 'Work that leads into your next progress checkpoint or mock attempt.' },
  { type: 'weakSkillFocus', title: 'Weak-Skill Focus', icon: AlertTriangle, eyebrow: 'Weak-Skill Focus', description: 'Targeted drills on the skills your recent attempts show need the most work.' },
];

export default function StudyPlanPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [tasks, setTasks] = useState<StudyPlanTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedRationale, setExpandedRationale] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchStudyPlan();
      setTasks(data);
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setError(err.userMessage ?? err.message ?? 'Failed to load study plan.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleMarkComplete = async (id: string, feedbackRating?: number) => {
    const prev = tasks.find(t => t.id === id);
    setTasks((all) => all.map((t) => (t.id === id ? { ...t, status: 'completed' } : t)));
    try {
      await updateStudyPlanTask(id, { status: 'completed', feedbackRating });
      track('plan_item_completed', { feedbackRating });
    } catch {
      // Rollback on failure
      if (prev) setTasks((all) => all.map((t) => (t.id === id ? { ...t, status: prev.status } : t)));
    }
  };

  const handleUndo = async (id: string) => {
    const prev = tasks.find(t => t.id === id);
    setTasks((all) => all.map((t) => (t.id === id ? { ...t, status: 'not_started' } : t)));
    try {
      await updateStudyPlanTask(id, { status: 'not_started' });
    } catch {
      if (prev) setTasks((all) => all.map((t) => (t.id === id ? { ...t, status: prev.status } : t)));
    }
  };

  const handleStart = (task: StudyPlanTask) => {
    track('task_started', { subTest: task.subTest, contentId: task.contentId });
    // Honor ContentRoute from backend (deep-link into specific content) when
    // present; fall back to the subtest landing route otherwise.
    const target = task.route && task.route.startsWith('/') ? task.route : `/${task.subTest.toLowerCase()}`;
    router.push(target);
  };

  const renderTaskCard = (task: StudyPlanTask, index: number) => {
    const isCompleted = task.status === 'completed';
    const SubIcon = SUBTEST_ICONS[task.subTest];
    const rationaleId = `study-plan-rationale-${task.id}`;
    const rationaleOpen = expandedRationale === task.id;

    return (
      <MotionItem key={task.id} delayIndex={Math.min(index, 5)}>
        <Card className={`transition-[border-color,box-shadow,opacity] duration-200 ${isCompleted ? 'opacity-60' : 'hover:border-border-hover hover:shadow-clinical'}`}>
          <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
            {/* Info */}
            <div className="min-w-0 flex-1">
              <div className="mb-2 flex flex-wrap items-center gap-2">
                <span className={`tile-label inline-flex items-center gap-1.5 rounded-control border px-2.5 py-1 ${SUBTEST_COLORS[task.subTest]}`}>
                  <SubIcon className="h-3.5 w-3.5" aria-hidden="true" />
                  {task.subTest}
                </span>
                {isCompleted && (
                  <span className="tile-label inline-flex items-center gap-1 rounded-control border border-success/20 bg-success/10 px-2 py-1 text-success-strong">
                    <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" /> Done
                  </span>
                )}
              </div>

              <h3 className={`mb-1.5 text-base font-bold ${isCompleted ? 'text-muted line-through' : 'text-navy'}`}>
                {task.title}
              </h3>

              <div className="flex flex-wrap items-center gap-3 text-sm text-muted">
                <span className="flex items-center gap-1"><Clock className="h-3.5 w-3.5" aria-hidden="true" />{task.duration}</span>
                <span className="flex items-center gap-1 tabular-nums"><Calendar className="h-3.5 w-3.5" aria-hidden="true" />{task.dueDate}</span>
              </div>

              {/* Rationale */}
              <div className="mt-2">
                <button
                  type="button"
                  onClick={() => setExpandedRationale(rationaleOpen ? null : task.id)}
                  aria-expanded={rationaleOpen}
                  aria-controls={rationaleId}
                  className="-mx-1 flex min-h-11 items-center gap-1 rounded-control px-1 text-sm font-semibold text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:min-h-0 sm:py-1"
                >
                  <Info className="h-3.5 w-3.5" aria-hidden="true" />
                  Why this is recommended
                  {rationaleOpen ? <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" /> : <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />}
                </button>
                <MotionCollapse open={rationaleOpen} id={rationaleId}>
                  <p className="mt-2 max-w-prose rounded-lg border border-info/20 bg-info/5 p-3 text-sm leading-relaxed text-navy">
                    {task.rationale}
                  </p>
                </MotionCollapse>
              </div>
            </div>

            {/* Actions */}
            <div className="flex shrink-0 flex-row items-center justify-end gap-2 border-t border-border pt-3 sm:flex-col sm:items-stretch sm:border-t-0 sm:pt-0">
              {!isCompleted ? (
                <>
                  <Button variant="primary" size="sm" onClick={() => handleStart(task)} className="flex-1 sm:flex-none">
                    <PlayCircle className="h-4 w-4" aria-hidden="true" /> Start
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => handleMarkComplete(task.id)} title="Mark Complete" aria-label="Mark Complete">
                    <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                  </Button>
                </>
              ) : (
                <Button variant="ghost" size="sm" onClick={() => handleUndo(task.id)}>
                  <RefreshCw className="h-4 w-4" aria-hidden="true" /> Undo
                </Button>
              )}
            </div>
          </div>
        </Card>
      </MotionItem>
    );
  };

  const asyncStatus = loading ? 'loading' : error ? 'error' : tasks.length === 0 ? 'empty' : 'success' as const;
  const todayTasks = tasks.filter((task) => task.section === 'today');
  const completedToday = todayTasks.filter((task) => task.status === 'completed').length;
  const nextCheckpointCount = tasks.filter((task) => task.section === 'nextCheckpoint').length;
  // The hero stays mounted in every state (one h1 per page); its counts wait for real data.
  const pendingCount = asyncStatus === 'loading' ? 'Loading...' : asyncStatus === 'error' ? '—' : null;

  return (
    <>
      <LearnerPageHero
        eyebrow="Action Plan"
        icon={Calendar}
        accent="primary"
        title="Keep today's study sequence visible"
        description="Use this plan to see what to do now, what comes next, and why each task is here."
        highlights={[
          { icon: Calendar, label: 'Today', value: pendingCount ?? `${todayTasks.length} scheduled` },
          { icon: CheckCircle2, label: 'Completed', value: pendingCount ?? `${completedToday} done` },
          { icon: Target, label: 'Next checkpoint', value: pendingCount ?? `${nextCheckpointCount} tasks` },
        ]}
        aside={(
          <Link
            href="/readiness"
            prefetch={false}
            className="inline-flex items-center gap-1.5 rounded-control text-xs font-bold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            See how this plan moves your readiness
            <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
          </Link>
        )}
      />

      <AsyncStateWrapper
        status={asyncStatus}
        onRetry={loadData}
        errorMessage={error ?? undefined}
        loadingContent={<LearnerSkeleton variant="list" />}
        emptyContent={
          <EmptyState
            icon={<Calendar className="h-8 w-8" />}
            title="No study plan yet"
            description="Set your goals to generate your personalised study plan."
            action={{ label: 'Set goals', href: '/goals' }}
          />
        }
      >
        <div className="learner-page-flow">
          {SECTIONS.map(({ type, title, icon: SectionIcon, eyebrow, description }) => {
            const sectionTasks = tasks.filter((t) => t.section === type);
            if (sectionTasks.length === 0) return null;

            return (
              <section key={type}>
                <LearnerSurfaceSectionHeader
                  eyebrow={eyebrow}
                  title={`${title} (${sectionTasks.length})`}
                  description={description}
                  icon={SectionIcon}
                  className="mb-4"
                />
                <div className="space-y-3">
                  {sectionTasks.map(renderTaskCard)}
                </div>
              </section>
            );
          })}
        </div>
      </AsyncStateWrapper>
    </>
  );
}
