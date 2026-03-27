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
  Flame,
} from 'lucide-react';
import { Button, Badge } from '@/components/ui';
import { AppShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchStudyPlan, updateStudyPlanTask } from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';

const SUBTEST_ICONS: Record<SubTest, React.ElementType> = {
  Reading: BookOpen,
  Listening: Headphones,
  Writing: FilePenLine,
  Speaking: Mic,
};

const SUBTEST_COLORS: Record<SubTest, string> = {
  Reading: 'bg-blue-100 text-blue-700 border-blue-200',
  Listening: 'bg-purple-100 text-purple-700 border-purple-200',
  Writing: 'bg-amber-100 text-amber-700 border-amber-200',
  Speaking: 'bg-emerald-100 text-emerald-700 border-emerald-200',
};

type SectionType = 'today' | 'thisWeek' | 'nextCheckpoint' | 'weakSkillFocus';

const SECTIONS: { type: SectionType; title: string; icon: React.ElementType; iconColor: string }[] = [
  { type: 'today', title: 'Today', icon: Calendar, iconColor: 'text-primary' },
  { type: 'thisWeek', title: 'This Week', icon: Calendar, iconColor: 'text-navy/60' },
  { type: 'nextCheckpoint', title: 'Next Checkpoint', icon: Target, iconColor: 'text-purple-500' },
  { type: 'weakSkillFocus', title: 'Weak-Skill Focus', icon: AlertTriangle, iconColor: 'text-amber-500' },
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
    } catch (e: any) {
      setError(e.userMessage ?? e.message ?? 'Failed to load study plan.');
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

  const handleReschedule = (id: string) => {
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
        className={`bg-surface border rounded-xl overflow-hidden transition-all duration-200 ${
          isCompleted ? 'border-gray-200 opacity-60' : 'border-gray-200/60 shadow-sm hover:border-gray-300 hover:shadow-md'
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
                  <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-bold uppercase bg-green-100 text-green-700 border border-green-200">
                    <CheckCircle2 className="w-3.5 h-3.5" /> Done
                  </span>
                )}
              </div>

              <h3 className={`text-base font-bold text-navy mb-1.5 ${isCompleted ? 'line-through text-gray-500' : ''}`}>
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
                      <p className="mt-2 p-3 bg-blue-50/50 border border-blue-100 rounded-lg text-sm text-navy/80 leading-relaxed">
                        {task.rationale}
                      </p>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>

            {/* Actions */}
            <div className="flex flex-row sm:flex-col items-center sm:items-stretch justify-end gap-2 shrink-0 border-t sm:border-t-0 pt-3 sm:pt-0 border-gray-100">
              {!isCompleted ? (
                <>
                  <Button variant="primary" size="sm" onClick={() => handleStart(task)} className="flex-1 sm:flex-none">
                    <PlayCircle className="w-4 h-4 mr-1" /> Start
                  </Button>
                  <div className="flex items-center gap-1.5">
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

  return (
    <AppShell pageTitle="Study Plan">
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
        <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-6 space-y-8">
          {/* Page header */}
          <div>
            <h1 className="text-2xl font-bold text-navy">Your Study Plan</h1>
            <p className="text-muted mt-1">Personalised tasks based on your goals and performance.</p>
          </div>

          {/* Sections */}
          {SECTIONS.map(({ type, title, icon: SectionIcon, iconColor }) => {
            const sectionTasks = tasks.filter((t) => t.section === type);
            if (sectionTasks.length === 0) return null;

            return (
              <section key={type}>
                <h2 className="text-lg font-bold text-navy mb-3 flex items-center gap-2 border-b pb-2">
                  <SectionIcon className={`w-5 h-5 ${iconColor}`} />
                  {title}
                  <span className="ml-1 bg-gray-100 text-muted text-xs py-0.5 px-2 rounded-full font-bold">
                    {sectionTasks.length}
                  </span>
                </h2>
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
    </AppShell>
  );
}
