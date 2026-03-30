'use client';

import { useState, useEffect, useCallback } from 'react';
import { motion } from 'motion/react';
import { BookOpen } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { fetchWritingTasks } from '@/lib/api';
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
              <BookOpen className="w-5 h-5 text-blue-300" />
            </div>
            <h1 className="text-3xl sm:text-4xl font-bold">Writing Task Library</h1>
          </div>
          <p className="text-gray-300 text-lg">Browse and select practice tasks to improve your clinical writing.</p>
        </div>
      </header>

      <main className="-mt-6 relative z-10">
        <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange}
          onClear={() => setFilters({})} className="mb-6 bg-white p-4 rounded-xl border border-gray-200 shadow-sm" />

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
              <motion.div key={task.id} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.05 }}>
                <TaskCard
                  id={task.id}
                  title={task.title}
                  subtest={task.scenarioType}
                  profession={task.profession}
                  duration={task.time}
                  difficulty={task.difficulty}
                  description={`Focus: ${task.criteriaFocus}`}
                  onStart={() => {
                    analytics.track('task_started', { taskId: task.id, subtest: 'writing' });
                    router.push(`/writing/player?taskId=${task.id}`);
                  }}
                />
              </motion.div>
            ))}
          </div>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
