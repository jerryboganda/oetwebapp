'use client';

import { useState, useEffect, useCallback } from 'react';
import { BookOpen, Brain, FileText, Monitor, UserRoundCheck } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { fetchWritingTasks, type WritingAssessorType, type WritingExamMode } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingTask } from '@/lib/mock-data';

const filterGroups: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [
    { id: 'Nursing', label: 'Nursing' }, { id: 'Medicine', label: 'Medicine' },
    { id: 'Physiotherapy', label: 'Physiotherapy' },
  ]},
  { id: 'difficulty', label: 'Difficulty', options: [
    { id: 'Easy', label: 'Easy' }, { id: 'Medium', label: 'Medium' }, { id: 'Hard', label: 'Hard' },
  ]},
];

export default function WritingTaskLibrary() {
  const router = useRouter();
  const [tasks, setTasks] = useState<WritingTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFilters] = useState<Record<string, string[]>>({});
  const [selectedExamMode, setSelectedExamMode] = useState<WritingExamMode>('computer');
  const [selectedAssessor, setSelectedAssessor] = useState<WritingAssessorType>('ai');

  const loadTasks = useCallback(() => {
    setLoading(true);
    setError(null);
    fetchWritingTasks()
      .then(setTasks)
      .catch(() => setError('Failed to load writing tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('module_entry', { subtest: 'writing', page: 'library' });
    const initialLoad = setTimeout(() => { loadTasks(); }, 0);
    return () => clearTimeout(initialLoad);
  }, [loadTasks]);

  const handleFilterChange = useCallback((groupId: string, optionId: string) => {
    setFilters(prev => {
      const current = prev[groupId] ?? [];
      return { ...prev, [groupId]: current.includes(optionId) ? current.filter(id => id !== optionId) : [...current, optionId] };
    });
  }, []);

  const filtered = tasks.filter(t => {
    if (filters.profession?.length && !filters.profession.includes(t.profession)) return false;
    if (filters.difficulty?.length && !filters.difficulty.includes(t.difficulty)) return false;
    return true;
  });

  return (
    <LearnerDashboardShell pageTitle="Writing Task Library">
      <header className="bg-navy text-white pt-10 pb-12 px-4 sm:px-6 lg:px-8">
        <div>
          <div className="flex items-center gap-3 mb-2">
            <div className="w-10 h-10 rounded-lg bg-white/10 flex items-center justify-center">
              <BookOpen className="w-5 h-5 text-info" />
            </div>
            <h1 className="text-3xl sm:text-4xl font-bold">Writing Task Library</h1>
          </div>
          <p className="text-muted/40 text-lg">
            Choose strict exam simulation for the 5-minute reading + 40-minute writing workflow, or learning mode for guided planning and rulebook support.
          </p>
        </div>
      </header>

      <main className="-mt-6 relative z-10">
        <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange}
          onClear={() => setFilters({})} className="mb-6 bg-surface p-4 rounded-xl border border-border shadow-sm" />

        <section className="mb-6 rounded-xl border border-border bg-surface p-4 shadow-sm">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Exam mode</p>
              <div className="mt-2 grid grid-cols-2 gap-2">
                {([
                  { value: 'computer' as const, label: 'Computer', icon: Monitor },
                  { value: 'paper' as const, label: 'Paper', icon: FileText },
                ]).map((option) => {
                  const Icon = option.icon;
                  const active = selectedExamMode === option.value;
                  return (
                    <button
                      key={option.value}
                      type="button"
                      onClick={() => setSelectedExamMode(option.value)}
                      className={`flex min-h-12 items-center justify-center gap-2 rounded-lg border px-3 text-sm font-bold transition-colors ${active ? 'border-primary bg-primary text-white' : 'border-border bg-background-light text-navy hover:border-primary/50'}`}
                      aria-pressed={active}
                    >
                      <Icon className="h-4 w-4" />
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>

            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Assessor</p>
              <div className="mt-2 grid grid-cols-2 gap-2">
                {([
                  { value: 'ai' as const, label: 'AI', icon: Brain },
                  { value: 'instructor' as const, label: 'Dr. Ahmed', icon: UserRoundCheck },
                ]).map((option) => {
                  const Icon = option.icon;
                  const active = selectedAssessor === option.value;
                  return (
                    <button
                      key={option.value}
                      type="button"
                      onClick={() => setSelectedAssessor(option.value)}
                      className={`flex min-h-12 items-center justify-center gap-2 rounded-lg border px-3 text-sm font-bold transition-colors ${active ? 'border-primary bg-primary text-white' : 'border-border bg-background-light text-navy hover:border-primary/50'}`}
                      aria-pressed={active}
                    >
                      <Icon className="h-4 w-4" />
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>
        </section>

        {loading ? (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
            {[1,2,3,4,5,6].map(i => <Skeleton key={i} className="h-56 rounded-2xl" />)}
          </div>
        ) : error ? (
          <ErrorState message={error} onRetry={loadTasks} />
        ) : filtered.length === 0 ? (
          <EmptyState title="No tasks match your filters" description="Try adjusting your filters to see more results." />
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
            {filtered.map((task, index) => (
              <MotionItem key={task.id} delayIndex={index}>
                <TaskCard
                  id={task.id}
                  title={task.title}
                  subtest={task.scenarioType}
                  profession={task.profession}
                  duration={task.time}
                  difficulty={task.difficulty}
                  description={`${task.letterType ?? task.scenarioType} task${task.criteriaFocus ? ` · Focus: ${task.criteriaFocus}` : ''}`}
                  startLabel="Start Exam"
                  onStart={() => {
                    analytics.track('task_started', { taskId: task.id, subtest: 'writing', mode: 'exam', examMode: selectedExamMode, assessorType: selectedAssessor });
                    router.push(`/writing/player?taskId=${encodeURIComponent(task.id)}&mode=exam&examMode=${selectedExamMode}&assessor=${selectedAssessor}`);
                  }}
                  secondaryActionLabel="Learning Mode"
                  onSecondaryAction={() => {
                    analytics.track('task_started', { taskId: task.id, subtest: 'writing', mode: 'learning' });
                    router.push(`/writing/player?taskId=${task.id}&mode=learning`);
                  }}
                />
              </MotionItem>
            ))}
          </div>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
