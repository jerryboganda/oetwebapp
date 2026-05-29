'use client';

import { useState, useEffect, useMemo, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { motion } from 'motion/react';
import {
  ChevronLeft,
  ChevronRight,
  Calendar as CalendarIcon,
  CheckCircle2,
  Clock,
  AlertTriangle,
  BookOpen,
  Headphones,
  FilePenLine,
  Mic,
} from 'lucide-react';
import { Button } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { AsyncStateWrapper } from '@/components/state';
import { fetchStudyPlan } from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';

// ─── Constants ──────────────────────────────────────────────────────
const DAYS_OF_WEEK = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const;

const SUBTEST_ICONS: Record<SubTest, React.ElementType> = {
  Reading: BookOpen,
  Listening: Headphones,
  Writing: FilePenLine,
  Speaking: Mic,
};

const SUBTEST_DOT: Record<SubTest, string> = {
  Reading: 'bg-blue-500',
  Listening: 'bg-purple-500',
  Writing: 'bg-amber-500',
  Speaking: 'bg-emerald-500',
};

const STATUS_ICON: Record<string, { Icon: React.ElementType; className: string }> = {
  completed: { Icon: CheckCircle2, className: 'text-success' },
  not_started: { Icon: Clock, className: 'text-muted' },
  in_progress: { Icon: Clock, className: 'text-info' },
  missed: { Icon: AlertTriangle, className: 'text-danger' },
};

type ViewMode = 'week' | 'month';

// ─── Helpers ────────────────────────────────────────────────────────
function startOfWeek(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  // ISO week: Monday=0
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

function isSameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function formatMonthYear(date: Date): string {
  return date.toLocaleString('en-GB', { month: 'long', year: 'numeric' });
}

function formatWeekRange(start: Date): string {
  const end = addDays(start, 6);
  const opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' };
  return `${start.toLocaleDateString('en-GB', opts)} – ${end.toLocaleDateString('en-GB', opts)}`;
}

// ─── Component ──────────────────────────────────────────────────────
export default function StudyPlanCalendarPage() {
  const router = useRouter();
  const [tasks, setTasks] = useState<StudyPlanTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<ViewMode>('week');
  const [cursor, setCursor] = useState<Date>(() => startOfWeek(new Date()));

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

  useEffect(() => { loadData(); }, [loadData]);

  // Navigation handlers
  const navigatePrev = () => {
    setCursor((c) => (view === 'week' ? addDays(c, -7) : new Date(c.getFullYear(), c.getMonth() - 1, 1)));
  };
  const navigateNext = () => {
    setCursor((c) => (view === 'week' ? addDays(c, 7) : new Date(c.getFullYear(), c.getMonth() + 1, 1)));
  };
  const goToday = () => setCursor(startOfWeek(new Date()));

  // Build calendar grid
  const calendarDays = useMemo(() => {
    if (view === 'week') {
      const weekStart = startOfWeek(cursor);
      return Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));
    }
    // Month view: start from the Monday before month start
    const monthStart = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
    const monthEnd = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
    const gridStart = startOfWeek(monthStart);
    const days: Date[] = [];
    let d = gridStart;
    while (d <= monthEnd || days.length % 7 !== 0) {
      days.push(new Date(d));
      d = addDays(d, 1);
    }
    return days;
  }, [view, cursor]);

  // Index tasks by ISO date for fast lookup
  const tasksByDate = useMemo(() => {
    const map = new Map<string, StudyPlanTask[]>();
    for (const task of tasks) {
      if (!task.dueDate) continue;
      const key = task.dueDate.slice(0, 10); // YYYY-MM-DD
      const arr = map.get(key) ?? [];
      arr.push(task);
      map.set(key, arr);
    }
    return map;
  }, [tasks]);

  const today = new Date();

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Study Calendar"
        description="View your study plan tasks across the week or month. Stay on track with completed, pending, and missed indicators."
        icon={CalendarIcon}
        accent="amber"
      />

      <div className="mt-6 space-y-4">
        {/* Toolbar */}
        <div className="flex items-center justify-between rounded-xl border border-border bg-surface px-4 py-3">
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={navigatePrev} aria-label="Previous">
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <span className="min-w-[180px] text-center text-sm font-medium text-navy">
              {view === 'week' ? formatWeekRange(cursor) : formatMonthYear(cursor)}
            </span>
            <Button variant="ghost" size="sm" onClick={navigateNext} aria-label="Next">
              <ChevronRight className="h-4 w-4" />
            </Button>
            <Button variant="ghost" size="sm" onClick={goToday} className="ml-2 text-xs">
              Today
            </Button>
          </div>

          <div className="flex rounded-lg border border-border">
            <button
              onClick={() => setView('week')}
              className={`px-3 py-1.5 text-xs font-medium transition-colors ${view === 'week' ? 'bg-primary text-primary-foreground' : 'text-muted hover:text-navy'}`}
            >
              Week
            </button>
            <button
              onClick={() => setView('month')}
              className={`px-3 py-1.5 text-xs font-medium transition-colors ${view === 'month' ? 'bg-primary text-primary-foreground' : 'text-muted hover:text-navy'}`}
            >
              Month
            </button>
          </div>
        </div>

        {/* Calendar grid */}
        <AsyncStateWrapper status={loading ? 'loading' : error ? 'error' : 'success'} errorMessage={error ?? undefined} onRetry={loadData}>
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.2 }}
            className="rounded-xl border border-border bg-surface"
          >
            {/* Day headers */}
            <div className="grid grid-cols-7 border-b border-border">
              {DAYS_OF_WEEK.map((day) => (
                <div key={day} className="px-2 py-2 text-center text-xs font-medium text-muted">
                  {day}
                </div>
              ))}
            </div>

            {/* Day cells */}
            <div className={`grid grid-cols-7 ${view === 'month' ? 'auto-rows-[100px]' : 'auto-rows-[140px]'}`}>
              {calendarDays.map((day) => {
                const key = day.toISOString().slice(0, 10);
                const dayTasks = tasksByDate.get(key) ?? [];
                const isToday = isSameDay(day, today);
                const isCurrentMonth = day.getMonth() === cursor.getMonth();

                return (
                  <div
                    key={key}
                    className={`relative border-b border-r border-border p-1.5 ${
                      !isCurrentMonth && view === 'month' ? 'bg-background-light' : ''
                    } ${isToday ? 'bg-primary/5' : ''}`}
                  >
                    {/* Date number */}
                    <span
                      className={`inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-medium ${
                        isToday
                          ? 'bg-primary text-primary-foreground'
                          : isCurrentMonth || view === 'week'
                            ? 'text-navy'
                            : 'text-muted'
                      }`}
                    >
                      {day.getDate()}
                    </span>

                    {/* Task indicators */}
                    <div className="mt-1 space-y-0.5 overflow-hidden">
                      {dayTasks.slice(0, view === 'month' ? 3 : 5).map((task) => {
                        const StatusMeta = STATUS_ICON[task.status] ?? STATUS_ICON.not_started;
                        const Icon = SUBTEST_ICONS[task.subTest] ?? BookOpen;
                        return (
                          <button
                            key={task.id}
                            onClick={() => task.route && router.push(task.route)}
                            className="flex w-full items-center gap-1 rounded px-1 py-0.5 text-left text-[11px] leading-tight transition-colors hover:bg-background-light"
                          >
                            <span className={`h-1.5 w-1.5 flex-shrink-0 rounded-full ${SUBTEST_DOT[task.subTest]}`} />
                            <StatusMeta.Icon className={`h-3 w-3 flex-shrink-0 ${StatusMeta.className}`} />
                            <span className="truncate text-navy">
                              {task.title}
                            </span>
                          </button>
                        );
                      })}
                      {dayTasks.length > (view === 'month' ? 3 : 5) && (
                        <span className="block px-1 text-[10px] text-muted">
                          +{dayTasks.length - (view === 'month' ? 3 : 5)} more
                        </span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </motion.div>
        </AsyncStateWrapper>

        {/* Legend */}
        <div className="flex flex-wrap items-center gap-4 text-xs text-muted">
          {(Object.entries(SUBTEST_DOT) as [SubTest, string][]).map(([subtest, dotClass]) => (
            <span key={subtest} className="flex items-center gap-1">
              <span className={`inline-block h-2 w-2 rounded-full ${dotClass}`} />
              {subtest}
            </span>
          ))}
          <span className="mx-2 text-border" aria-hidden="true">|</span>
          <span className="flex items-center gap-1"><CheckCircle2 className="h-3 w-3 text-success" /> Completed</span>
          <span className="flex items-center gap-1"><Clock className="h-3 w-3 text-muted" /> Pending</span>
          <span className="flex items-center gap-1"><AlertTriangle className="h-3 w-3 text-danger" /> Missed</span>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
