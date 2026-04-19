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
  Moon,
  Download,
  Sparkles,
} from 'lucide-react';
import { Button } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { useAnalytics } from '@/hooks/use-analytics';
import {
  fetchStudyPlan,
  updateStudyPlanTask,
  rescheduleStudyPlanTask,
  snoozeStudyPlanTask,
  startStudyPlanTask,
  regenerateStudyPlan,
  studyPlanIcsUrl,
} from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';

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

function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const dt = new Date(Date.UTC(y, (m ?? 1) - 1, d ?? 1));
  dt.setUTCDate(dt.getUTCDate() + days);
  return dt.toISOString().slice(0, 10);
}

export default function StudyPlanPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [tasks, setTasks] = useState<StudyPlanTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedRationale, setExpandedRationale] = useState<string | null>(null);
  const [regenerating, setRegenerating] = useState(false);
  const [rescheduling, setRescheduling] = useState<string | null>(null);

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

  // Reschedule: now actually calls the API. Defaults to +1 day; future work =
  // open a date picker popover.
  const handleReschedule = async (id: string) => {
    const current = tasks.find(t => t.id === id);
    if (!current) return;
    setRescheduling(id);
    const nextDate = addDaysIso(current.dueDate, 1);
    const prev = { ...current };
    setTasks((all) => all.map((t) => (t.id === id ? { ...t, dueDate: nextDate, status: 'rescheduled' } : t)));
    try {
      await rescheduleStudyPlanTask(id, nextDate);
      track('plan_item_rescheduled', { newDueDate: nextDate });
    } catch {
      setTasks((all) => all.map((t) => (t.id === id ? prev : t)));
    } finally {
      setRescheduling(null);
    }
  };

  // Snooze: hides from "today" for 1 day. Replaces the previously dead Swap
  // button with a useful behaviour for learners.
  const handleSnooze = async (id: string) => {
    const tomorrow = new Date(Date.now() + 86_400_000).toISOString();
    try {
      await snoozeStudyPlanTask(id, tomorrow);
      track('study_plan_item_snoozed', { itemId: id });
      setTasks((all) => all.map((t) => (t.id === id ? { ...t, snoozedUntil: tomorrow } : t)));
    } catch {
      // no-op; ghost button
    }
  };

  const handleStart = async (task: StudyPlanTask) => {
    track('task_started', { subTest: task.subTest, contentId: task.contentId });
    try {
      const { startUrl } = await startStudyPlanTask(task.id);
      router.push(startUrl || task.startUrl || `/${task.subTest.toLowerCase()}`);
    } catch {
      router.push(task.startUrl || `/${task.subTest.toLowerCase()}`);
    }
  };

  const handleRegenerate = async () => {
    setRegenerating(true);
    track('study_plan_regenerate_clicked');
    try {
      await regenerateStudyPlan();
      await loadData();
    } finally {
      setRegenerating(false);
    }
  };

  const handleExportIcs = () => {
    track('study_plan_ics_exported');
    // Use full URL so cookie auth applies. Opening in a new tab triggers a
    // download because of the text/calendar MIME type.
    const a = document.createElement('a');
    a.href = studyPlanIcsUrl();
    a.download = 'oet-study-plan.ics';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  };

  const renderTaskCard = (task: StudyPlanTask) => {
    const isCompleted = task.status === 'completed';
    const SubIcon = SUBTEST_ICONS[task.subTest];
    const isRescheduling = rescheduling === task.id;

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
                {task.aiRationaleAddendum && (
                  <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-bold uppercase bg-violet-100 text-violet-700 border border-violet-200" title="Personalised by AI">
                    <Sparkles className="w-3.5 h-3.5" /> AI
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
                      <p className="mt-2 p-3 bg-blue-50/50 border border-blue-100 rounded-lg text-sm text-navy/80 leading-relaxed whitespace-pre-wrap">
                        {task.rationale}
                        {task.aiRationaleAddendum ? `\n\n${task.aiRationaleAddendum}` : ''}
                      </p>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>

            <div className="flex flex-row sm:flex-col items-center sm:items-stretch justify-end gap-2 shrink-0 border-t sm:border-t-0 pt-3 sm:pt-0 border-gray-100">
              {!isCompleted ? (
                <>
                  <Button variant="primary" size="sm" onClick={() => handleStart(task)} className="flex-1 sm:flex-none">
                    <PlayCircle className="w-4 h-4 mr-1" /> Start
                  </Button>
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Button variant="ghost" size="sm" onClick={() => handleMarkComplete(task.id)} title="Mark complete" aria-label="Mark complete">
                      <CheckCircle2 className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => handleReschedule(task.id)} disabled={isRescheduling} title="Move to tomorrow" aria-label="Reschedule to tomorrow">
                      <Calendar className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => handleSnooze(task.id)} title="Snooze 1 day" aria-label="Snooze this task">
                      <Moon className="w-4 h-4" />
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

          {/* Plan actions toolbar */}
          <div className="flex flex-wrap items-center justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={handleExportIcs}>
              <Download className="w-4 h-4 mr-1" /> Add to calendar
            </Button>
            <Button variant="ghost" size="sm" onClick={handleRegenerate} disabled={regenerating} aria-label="Regenerate study plan">
              <RefreshCw className={`w-4 h-4 mr-1 ${regenerating ? 'animate-spin' : ''}`} />
              {regenerating ? 'Regenerating…' : 'Regenerate plan'}
            </Button>
          </div>

          {SECTIONS.map(({ type, title, icon: SectionIcon, iconColor }) => {
            const sectionTasks = tasks.filter((t) => t.section === type);
            if (sectionTasks.length === 0) return null;

            return (
              <section key={type}>
                <LearnerSurfaceSectionHeader
                  eyebrow="Plan Section"
                  title={`${title} (${sectionTasks.length})`}
                  description="Each section groups work by timing or purpose so the learner can see why these tasks belong together."
                  className="mb-3"
                />
                <div className="flex items-center gap-2 text-sm font-semibold text-muted mb-3">
                  <SectionIcon className={`w-5 h-5 ${iconColor}`} />
                  {title}
                </div>
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
