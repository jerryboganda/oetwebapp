'use client';

import { useState, useEffect, useMemo, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import {
  ChevronLeft,
  ChevronRight,
  Calendar as CalendarIcon,
  CheckCircle2,
  Clock,
  AlertTriangle,
} from 'lucide-react';
import { Button, Card, TabPanel, Tabs } from '@/components/ui';
import { EmptyState } from '@/components/ui/empty-error';
import { LearnerPageHero } from '@/components/domain';
import { AsyncStateWrapper } from '@/components/state';
import { fetchStudyPlan } from '@/lib/api';
import type { StudyPlanTask, SubTest } from '@/lib/mock-data';

// ─── Constants ──────────────────────────────────────────────────────
const DAYS_OF_WEEK = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const;

// Sub-test identity (DESIGN.md §2 skill tokens), never a status colour.
const SUBTEST_DOT: Record<SubTest, string> = {
  Reading: 'bg-skill-reading',
  Listening: 'bg-skill-listening',
  Writing: 'bg-skill-writing',
  Speaking: 'bg-skill-speaking',
};

const STATUS_ICON: Record<string, { Icon: React.ElementType; className: string }> = {
  completed: { Icon: CheckCircle2, className: 'text-success-strong' },
  not_started: { Icon: Clock, className: 'text-muted' },
  in_progress: { Icon: Clock, className: 'text-info' },
  missed: { Icon: AlertTriangle, className: 'text-danger-strong' },
};

type ViewMode = 'week' | 'month';

const VIEW_TABS: { id: ViewMode; label: string }[] = [
  { id: 'week', label: 'Week' },
  { id: 'month', label: 'Month' },
];

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
  const asyncStatus = loading ? 'loading' : error ? 'error' : tasks.length === 0 ? 'empty' : 'success' as const;
  const visibleTaskLimit = view === 'month' ? 3 : 5;

  return (
    <>
      <LearnerPageHero
        title="Study Calendar"
        description="View your study plan tasks across the week or month. Stay on track with completed, pending, and missed indicators."
        icon={CalendarIcon}
        accent="amber"
      />

      {/* Toolbar */}
      <Card padding="sm" className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 items-center gap-1 sm:gap-2">
          <Button variant="ghost" size="sm" onClick={navigatePrev} aria-label="Previous">
            <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          </Button>
          <span className="min-w-0 text-center text-sm font-medium tabular-nums text-navy sm:min-w-44">
            {view === 'week' ? formatWeekRange(cursor) : formatMonthYear(cursor)}
          </span>
          <Button variant="ghost" size="sm" onClick={navigateNext} aria-label="Next">
            <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          </Button>
          <Button variant="ghost" size="sm" onClick={goToday} className="sm:ms-2">
            Today
          </Button>
        </div>

        <Tabs
          tabs={VIEW_TABS}
          activeTab={view}
          onChange={(id) => setView(id as ViewMode)}
          scrollable={false}
          className="w-auto"
        />
      </Card>

      {/* Calendar grid */}
      <AsyncStateWrapper
        status={asyncStatus}
        errorMessage={error ?? undefined}
        onRetry={loadData}
        emptyContent={
          <EmptyState
            icon={<CalendarIcon className="h-8 w-8" />}
            title="No study plan yet"
            description="Set your goals to generate your personalised study plan."
            action={{ label: 'Set goals', href: '/goals' }}
          />
        }
      >
        <TabPanel id={view} activeTab={view}>
          {/* Seven columns need room for task titles: below that width the grid scrolls inside its card. */}
          <Card padding="none" className="overflow-x-auto">
            <div className="min-w-176">
              {/* Day headers */}
              <div className="grid grid-cols-7 border-b border-border">
                {DAYS_OF_WEEK.map((day) => (
                  <div key={day} className="px-2 py-2 text-center text-xs font-medium text-muted">
                    {day}
                  </div>
                ))}
              </div>

              {/* Day cells: rows start at a fixed height and grow with their tasks instead of clipping them. */}
              <div className={`grid grid-cols-7 ${view === 'month' ? 'auto-rows-[minmax(6.25rem,auto)]' : 'auto-rows-[minmax(8.75rem,auto)]'}`}>
                {calendarDays.map((day) => {
                  const key = day.toISOString().slice(0, 10);
                  const dayTasks = tasksByDate.get(key) ?? [];
                  const isToday = isSameDay(day, today);
                  const isCurrentMonth = day.getMonth() === cursor.getMonth();

                  return (
                    <div
                      key={key}
                      className={`relative min-w-0 border-b border-e border-border p-1.5 ${
                        !isCurrentMonth && view === 'month' ? 'bg-background-light' : ''
                      } ${isToday ? 'bg-primary/5' : ''}`}
                    >
                      {/* Date number */}
                      <span
                        className={`inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-medium tabular-nums ${
                          isToday
                            ? 'bg-primary text-primary-foreground dark:bg-violet-700'
                            : isCurrentMonth || view === 'week'
                              ? 'text-navy'
                              : 'text-muted'
                        }`}
                      >
                        {day.getDate()}
                      </span>

                      {/* Task indicators */}
                      <div className="mt-1 space-y-0.5">
                        {dayTasks.slice(0, visibleTaskLimit).map((task) => {
                          const StatusMeta = STATUS_ICON[task.status] ?? STATUS_ICON.not_started;
                          return (
                            <button
                              key={task.id}
                              type="button"
                              onClick={() => task.route && router.push(task.route)}
                              className="flex min-h-6 w-full items-center gap-1 rounded-md px-1 py-0.5 text-start text-2xs leading-tight transition-colors hover-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                            >
                              <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${SUBTEST_DOT[task.subTest]}`} aria-hidden="true" />
                              <StatusMeta.Icon className={`h-3 w-3 shrink-0 ${StatusMeta.className}`} aria-hidden="true" />
                              <span className="truncate text-navy">
                                {task.title}
                              </span>
                            </button>
                          );
                        })}
                        {dayTasks.length > visibleTaskLimit && (
                          <span className="block px-1 text-3xs tabular-nums text-muted">
                            +{dayTasks.length - visibleTaskLimit} more
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          </Card>
        </TabPanel>
      </AsyncStateWrapper>

      {/* Legend */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs text-muted">
        {(Object.entries(SUBTEST_DOT) as [SubTest, string][]).map(([subtest, dotClass]) => (
          <span key={subtest} className="flex items-center gap-1">
            <span className={`inline-block h-2 w-2 rounded-full ${dotClass}`} aria-hidden="true" />
            {subtest}
          </span>
        ))}
        <span className="mx-2 text-border" aria-hidden="true">|</span>
        <span className="flex items-center gap-1"><CheckCircle2 className="h-3 w-3 text-success-strong" aria-hidden="true" /> Completed</span>
        <span className="flex items-center gap-1"><Clock className="h-3 w-3 text-muted" aria-hidden="true" /> Pending</span>
        <span className="flex items-center gap-1"><AlertTriangle className="h-3 w-3 text-danger-strong" aria-hidden="true" /> Missed</span>
      </div>
    </>
  );
}
