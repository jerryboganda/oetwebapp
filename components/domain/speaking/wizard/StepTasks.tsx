'use client';

/**
 * Card wizard — step 3: candidate task bullets.
 * Up to 5 bullets; at least 3 are recommended before publish (surfaced as a
 * live badge + hint, not a hard block — the backend only hard-gates on the
 * interlocutor script).
 */

import { useCallback, useMemo, useState } from 'react';
import { Input } from '@/components/ui/form-controls';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { adminPatchRolePlayCard, type RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';

function fillTasks(tasks: string[] | undefined): [string, string, string, string, string] {
  const out: [string, string, string, string, string] = ['', '', '', '', ''];
  if (!tasks) return out;
  for (let i = 0; i < Math.min(5, tasks.length); i++) out[i] = tasks[i] ?? '';
  return out;
}

export function StepTasks() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;
  const initial = fillTasks(card.tasks);

  const [task1, setTask1] = useState(initial[0]);
  const [task2, setTask2] = useState(initial[1]);
  const [task3, setTask3] = useState(initial[2]);
  const [task4, setTask4] = useState(initial[3]);
  const [task5, setTask5] = useState(initial[4]);

  const taskCount = useMemo(
    () => [task1, task2, task3, task4, task5].filter((t) => t.trim().length > 0).length,
    [task1, task2, task3, task4, task5],
  );

  const canAdvance = taskCount >= 1;

  const submit = useCallback(async () => {
    await adminPatchRolePlayCard(card.cardId, {
      task1: task1.trim(),
      task2: task2.trim(),
      task3: task3.trim(),
      task4: task4.trim(),
      task5: task5.trim(),
    });
    await wizard.refresh();
  }, [card.cardId, task1, task2, task3, task4, task5, wizard]);

  useStepRegistration('tasks', { canAdvance, submit });

  const fields = [
    { label: 'Task 1', value: task1, set: setTask1 },
    { label: 'Task 2', value: task2, set: setTask2 },
    { label: 'Task 3', value: task3, set: setTask3 },
    { label: 'Task 4 (optional)', value: task4, set: setTask4 },
    { label: 'Task 5 (optional)', value: task5, set: setTask5 },
  ];

  return (
    <div className="space-y-5">
      <div className="flex items-end justify-between gap-3">
        <header className="space-y-1">
          <h2 className="text-lg font-bold text-navy">Task bullets</h2>
          <p className="text-sm text-muted">Up to 5 bullet tasks shown on the candidate card. At least 3 are recommended before publishing.</p>
        </header>
        <span
          className={`shrink-0 rounded-full px-3 py-1 text-xs font-bold uppercase tracking-wider ${
            taskCount >= 3 ? 'bg-emerald-500/10 text-emerald-600' : 'bg-amber-500/10 text-amber-600'
          }`}
        >
          {taskCount}/5 tasks
        </span>
      </div>

      <div className="grid gap-3">
        {fields.map(({ label, value, set }) => (
          <Input
            key={label}
            label={label}
            value={value}
            onChange={(e) => set(e.target.value)}
            placeholder='e.g. "Explain the discharge medication regimen."'
            maxLength={500}
          />
        ))}
      </div>
    </div>
  );
}
