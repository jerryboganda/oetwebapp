'use client';

import { useEffect, useState } from 'react';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ClipboardList, MessageCircleQuestion } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { TaskCard } from '@/components/domain/task-card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { LearnerSurfaceCard } from '@/components/domain';
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
        <MotionSection>
          <div className="space-y-1">
            <h2 className="text-lg font-bold text-navy sm:text-xl">Prepare for your OET Speaking</h2>
            <p className="text-[13px] text-muted sm:text-sm">
              Review the assessment criteria and the common introductory questions used across professions.
            </p>
          </div>
          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: 'purple',
                eyebrow: 'Reference',
                eyebrowIcon: ClipboardList,
                title: 'Speaking Assessment Criteria',
                description: 'The 9 criteria your role-plays are assessed against, with weights and what to do well. Same for all professions.',
                metaItems: [
                  { icon: ClipboardList, label: '9 sections' },
                  { icon: ClipboardList, label: '42 points' },
                ],
                primaryAction: {
                  label: 'Open Assessment Criteria',
                  href: '/speaking/assessment-criteria',
                },
              }}
            />
            <LearnerSurfaceCard
              card={{
                kind: 'navigation',
                sourceType: 'frontend_navigation',
                accent: 'purple',
                eyebrow: 'Reference',
                eyebrowIcon: MessageCircleQuestion,
                title: 'Speaking Intro Questions',
                description: '12 common introductory questions with adaptable sample answers for every profession. Personalise the highlighted details.',
                metaItems: [
                  { icon: MessageCircleQuestion, label: '12 questions' },
                  { icon: MessageCircleQuestion, label: 'All professions' },
                ],
                primaryAction: {
                  label: 'Open Intro Questions',
                  href: '/speaking/intro-questions',
                },
              }}
            />
          </div>
        </MotionSection>

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
                    window.location.href = `/speaking/roleplay/${encodeURIComponent(task.id)}`;
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
