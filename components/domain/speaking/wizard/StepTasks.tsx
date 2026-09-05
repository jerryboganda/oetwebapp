'use client';

/**
 * Card wizard — step 3: candidate task bullets.
 * The list is unbounded (real printed cards run to nine bullets); at least 3
 * are required before publish, surfaced as a live badge + hint, not a hard
 * block on advancing.
 */

import { useCallback, useState } from 'react';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { TaskListEditor, normaliseTasks } from '@/components/domain/speaking/TaskListEditor';
import { adminPatchRolePlayCard, type RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';

export function StepTasks() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;

  const [tasks, setTasks] = useState<string[]>(() => {
    const initial = card.tasks ?? [];
    return initial.length > 0 ? [...initial] : ['', '', ''];
  });

  const canAdvance = normaliseTasks(tasks).length >= 1;

  const submit = useCallback(async () => {
    await adminPatchRolePlayCard(card.cardId, { tasks: normaliseTasks(tasks) });
    await wizard.refresh();
  }, [card.cardId, tasks, wizard]);

  useStepRegistration('tasks', { canAdvance, submit });

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Task bullets</h2>
        <p className="text-sm text-muted">
          Every bullet printed on the candidate card, in printed order. At least 3 are required
          before publishing.
        </p>
      </header>

      <TaskListEditor
        tasks={tasks}
        onChange={setTasks}
        addLabel="Add task"
        itemNoun="Task"
        placeholder='e.g. "Explain the discharge medication regimen."'
        emptyHint="No task bullets yet."
        minRecommended={3}
      />
    </div>
  );
}
