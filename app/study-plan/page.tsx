'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import {
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
import { Button } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchStudyPlan, updateStudyPlanTask } from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';

const SUBTEST_ICONS: Record<SubTest, React.ElementType> = {
  Reading: BookOpen,
  Listening: Headphones,
  Writing: FilePenLine,
  Speaking: Mic,
};

// Subtest accents — DESIGN palette tier-2 tints.
const SUBTEST_COLORS: Record<SubTest, string> = {
  Reading: 'bg-blue-100 text-blue-700 border-blue-200',
  Listening: 'bg-purple-100 text-purple-700 border-purple-200',
  Writing: 'bg-amber-100 text-amber-700 border-amber-200',
  Speaking: 'bg-emerald-100 text-emerald-700 border-emerald-200',
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

  const handleMarkComplete = async (id: string) => {
    const prev = tasks.find(t => t.id === id);
    setTasks((all) => all.map((t) => (t.id === id ? { ...t, status: 'completed' } : t)));
    try {
      await updateStudyPlanTask(id, { status: 'completed' });
      track('plan_item_completed');
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

  const handleReschedule = (_id: string) => {
    track('plan_item_rescheduled');
  };

  const handleStart = (task: StudyPlanTask) => {
    track('task_started', { subTest: task.subTest, contentId: task.contentId });
    router.push(`/${task.subTest.toLowerCase()}`);
  };

  const renderTaskCard = (task: StudyPlanTask) => {
    const isCompleted = task.status === 'completed';
    const SubIcon = SUBTEST_ICONS[task.subTest];

    return (
      <motion.div
        layout
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.95 }}
        key={task.id}
        className={`bg-surface border border-border rounded-2xl overflow-hidden shadow-sm transition-all duration-200 ${
          isCompleted ? 'opacity-60' : 'hover:shadow-md'
        }`}
      >
        <div className="p-4 sm:p-5">
          <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-4">
            {/* Info */}
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2 mb-2 flex-wrap">
                <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-bold uppercase tracking-wider border ${SUBTEST_COLORS[task.subTest]}`}>
                  <SubIcon className="w-3.5 h-3.5" />
                  {task.subTest}
                </span>
                {isCompleted && (
                  <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-bold uppercase bg-success/10 text-success border border-success/20">
                    <CheckCircle2 className="w-3.5 h-3.5" /> Done
                  </span>
                )}
              </div>

              <h3 className={`text-base font-bold text-navy mb-1.5 ${isCompleted ? 'line-through text-muted' : ''}`}>
                {task.title}
              </h3>

              <div className="flex flex-wrap items-center gap-3 text-sm text-muted">
                <span className="flex items-center gap-1"><Clock className="w-3.5 h-3.5" />{task.duration}</span>
                <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" />{task.dueDate}</span>
              </div>

              {/* Rationale */}
              <div className="mt-2">
                <button
                  onClick={() => setExpandedRationale(expandedRationale === task.id ? null : task.id)}
                  aria-expanded={expandedRationale === task.id}
                  className="flex items-center gap-1 text-sm font-semibold text-primary hover:text-primary/80 transition-colors"
                >
                  <Info className="w-3.5 h-3.5" />
                  Why this is recommended
                  {expandedRationale === task.id ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                </button>
                <AnimatePresence>
                  {expandedRationale === task.id && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: 'auto', opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      className="overflow-hidden"
                    >
                      <p className="mt-2 p-3 bg-info/5 border border-info/20 rounded-lg text-sm text-navy/80 leading-relaxed">
                        {task.rationale}
                      </p>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>

            {/* Actions */}
            <div className="flex flex-row sm:flex-col items-center sm:items-stretch justify-end gap-2 shrink-0 border-t sm:border-t-0 pt-3 sm:pt-0 border-border">
              {!isCompleted ? (
                <>
                  <Button variant="primary" size="sm" onClick={() => handleStart(task)} className="flex-1 sm:flex-none">
                    <PlayCircle className="w-4 h-4 mr-1" /> Start
                  </Button>
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Button variant="ghost" size="sm" onClick={() => handleMarkComplete(task.id)} title="Mark Complete">
                      <CheckCircle2 className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => handleReschedule(task.id)} title="Reschedule">
                      <Calendar className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" title="Swap Task">
                      <RefreshCw className="w-4 h-4" />
                    </Button>
                  </div>
                </>
              ) : (
                <Button variant="ghost" size="sm" onClick={() => handleUndo(task.id)}>
                  <RefreshCw className="w-4 h-4 mr-1" /> Undo
                </Button>
              )}
            </div>
          </div>
        </div>
      </motion.div>
    );
  };

  const asyncStatus = loading ? 'loading' : error ? 'error' : tasks.length === 0 ? 'empty' : 'success' as const;
  const todayTasks = tasks.filter((task) => task.section === 'today');
  const completedToday = todayTasks.filter((task) => task.status === 'completed').length;
  const nextCheckpointCount = tasks.filter((task) => task.section === 'nextCheckpoint').length;

  return (
    <LearnerDashboardShell pageTitle="Study Plan">
      <AsyncStateWrapper
        status={asyncStatus}
        onRetry={loadData}
        errorMessage={error ?? undefined}
        emptyContent={
          <div className="py-12 text-center space-y-3">
            <p className="text-sm font-bold text-navy">No study plan yet</p>
            <p className="text-xs text-muted">Complete the diagnostic assessment to generate your personalised study plan.</p>
            <Button size="sm" onClick={() => router.push('/diagnostic')}>Start Diagnostic</Button>
          </div>
        }
      >
        <div className="space-y-8">
          <LearnerPageHero
            eyebrow="Action Plan"
            icon={Calendar}
            accent="primary"
            title="Keep today's study sequence visible"
            description="Use this plan to see what to do now, what comes next, and why each task is here."
            highlights={[
              { icon: Calendar, label: 'Today', value: `${todayTasks.length} scheduled` },
              { icon: CheckCircle2, label: 'Completed', value: `${completedToday} done` },
              { icon: Target, label: 'Next checkpoint', value: `${nextCheckpointCount} tasks` },
            ]}
          />

          {/* Sections */}
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
                  <AnimatePresence mode="popLayout">
                    {sectionTasks.map(renderTaskCard)}
                  </AnimatePresence>
                </div>
              </section>
            );
          })}
        </div>
      </AsyncStateWrapper>
    </LearnerDashboardShell>
  );
}
