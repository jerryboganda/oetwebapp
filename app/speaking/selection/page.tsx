'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Search } from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { fetchSpeakingTasks } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { SpeakingTask } from '@/lib/mock-data';

const FILTER_GROUPS: FilterGroup[] = [
  {
    id: 'profession',
    label: 'Profession',
    options: [
      { id: 'Nursing', label: 'Nursing' },
      { id: 'Medicine', label: 'Medicine' },
      { id: 'Pharmacy', label: 'Pharmacy' },
      { id: 'Physiotherapy', label: 'Physiotherapy' },
    ],
  },
  {
    id: 'difficulty',
    label: 'Difficulty',
    options: [
      { id: 'Easy', label: 'Easy' },
      { id: 'Medium', label: 'Medium' },
      { id: 'Hard', label: 'Hard' },
    ],
  },
];

export default function SpeakingTaskSelection() {
  const [tasks, setTasks] = useState<SpeakingTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<Record<string, string[]>>({});

  useEffect(() => {
    fetchSpeakingTasks()
      .then(setTasks)
      .finally(() => setLoading(false));
  }, []);

  const filtered = tasks.filter((t) => {
    const profFilter = selected.profession;
    const diffFilter = selected.difficulty;
    if (profFilter?.length && !profFilter.includes(t.profession)) return false;
    if (diffFilter?.length && !diffFilter.includes(t.difficulty)) return false;
    return true;
  });

  const handleFilterChange = (groupId: string, optionId: string) => {
    setSelected(prev => {
      const current = prev[groupId] ?? [];
      const next = current.includes(optionId)
        ? current.filter(id => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: next };
    });
  };

  return (
    <AppShell pageTitle="Select Speaking Task">
      <div className="space-y-6">
        <FilterBar
          groups={FILTER_GROUPS}
          selected={selected}
          onChange={handleFilterChange}
          onClear={() => setSelected({})}
        />

        {loading ? (
          <div className="grid grid-cols-1 gap-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-28 w-full rounded-xl" />
            ))}
          </div>
        ) : filtered.length === 0 ? (
          <EmptyState
            title="No tasks found"
            description="Try adjusting your filters."
            action={{ label: 'Clear Filters', onClick: () => setSelected({}) }}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4">
            {filtered.map((task, i) => (
              <motion.div
                key={task.id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: i * 0.04 }}
              >
                <TaskCard
                  id={task.id}
                  title={task.title}
                  subtest="Speaking"
                  profession={task.profession}
                  duration={task.duration}
                  difficulty={task.difficulty}
                  description={`Focus: ${task.criteriaFocus}`}
                  tags={[task.scenarioType]}
                  onStart={() => {
                    analytics.track('task_started', { taskId: task.id, subtest: 'speaking' });
                    window.location.href = `/speaking/check?taskId=${task.id}`;
                  }}
                />
              </motion.div>
            ))}
          </div>
        )}
      </div>
    </AppShell>
  );
}
