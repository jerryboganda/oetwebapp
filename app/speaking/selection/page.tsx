'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { BookOpen } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
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
    <LearnerDashboardShell pageTitle="Select Speaking Task">
      <div className="space-y-6">
        <Card className="border-border bg-surface p-5 shadow-sm">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Rulebook Source of Truth</p>
              <h2 className="mt-2 text-lg font-black text-navy">See the exact speaking rules behind the transcript audit</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
                Every speaking audit is grounded in Dr. Hesham&apos;s rulebook, from plain-English explanations to the Breaking Bad News protocol and the ping-pong conversation rule.
              </p>
            </div>

            <div className="flex flex-wrap gap-3">
              <Link href="/speaking/rulebook/RULE_22">
                <Button variant="outline" className="whitespace-nowrap">
                  <BookOpen className="h-4 w-4" /> Speaking rules
                </Button>
              </Link>
              <Link href="/speaking/rulebook/RULE_44">
                <Button variant="ghost" className="whitespace-nowrap">
                  Breaking bad news
                </Button>
              </Link>
            </div>
          </div>
        </Card>

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
              <MotionItem
                key={task.id}
                delayIndex={i}
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
              </MotionItem>
            ))}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
