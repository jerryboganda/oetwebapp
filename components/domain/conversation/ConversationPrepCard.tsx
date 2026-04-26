'use client';

import { Mic } from 'lucide-react';
import type { ConversationScenario } from '@/lib/types/conversation';

interface Props {
  scenario: ConversationScenario;
  prepCountdown: number;
  onStart: () => void;
}

function formatTime(s: number) {
  return `${Math.floor(s / 60).toString().padStart(2, '0')}:${(s % 60).toString().padStart(2, '0')}`;
}

export function ConversationPrepCard({ scenario, prepCountdown, onStart }: Props) {
  const objectives = scenario.objectives ?? [];
  return (
    <section className="rounded-3xl border border-purple-200/60 bg-gradient-to-br from-purple-50 to-indigo-50 p-6 shadow-sm dark:border-purple-800/40 dark:from-purple-950/40 dark:to-indigo-950/40">
      <div className="mb-2 text-xs font-bold uppercase tracking-[0.2em] text-purple-600 dark:text-purple-400">
        Preparation phase
      </div>
      <h1 className="mb-2 text-2xl font-bold text-navy dark:text-white">{scenario.title}</h1>
      {scenario.context && <p className="mb-4 text-muted dark:text-muted/40">{scenario.context}</p>}

      <div className="mb-4 grid grid-cols-1 gap-3 md:grid-cols-2">
        <div className="rounded-2xl bg-white/70 p-3 dark:bg-gray-800/70">
          <div className="mb-1 text-xs font-semibold uppercase text-muted">Your role</div>
          <div className="text-sm font-bold text-navy dark:text-white">
            {scenario.clinicianRole ?? 'Clinician'}
          </div>
        </div>
        <div className="rounded-2xl bg-white/70 p-3 dark:bg-gray-800/70">
          <div className="mb-1 text-xs font-semibold uppercase text-muted">
            {scenario.taskTypeCode === 'oet-handover' ? 'Colleague' : 'Patient'}
          </div>
          <div className="text-sm font-bold text-navy dark:text-white">
            {scenario.patientRole ?? 'Patient'}
          </div>
        </div>
      </div>

      {objectives.length > 0 && (
        <div className="mb-4 rounded-2xl bg-white/70 p-3 dark:bg-gray-800/70">
          <div className="mb-2 text-xs font-semibold uppercase text-muted">Objectives</div>
          <ul className="space-y-1">
            {objectives.map((objective, i) => (
              <li key={i} className="flex items-start gap-2 text-sm text-navy/80 dark:text-muted/40">
                <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-purple-100 text-xs font-bold text-purple-600 dark:bg-purple-900/40 dark:text-purple-400">
                  {i + 1}
                </span>
                {objective}
              </li>
            ))}
          </ul>
        </div>
      )}

      {scenario.expectedRedFlags && scenario.expectedRedFlags.length > 0 && (
        <div className="mb-4 rounded-2xl border border-red-200/60 bg-red-50/70 p-3 dark:border-red-800/40 dark:bg-red-950/30">
          <div className="mb-1 text-xs font-semibold uppercase text-red-600 dark:text-red-400">
            Watch for red flags
          </div>
          <ul className="list-disc space-y-0.5 pl-5 text-xs text-red-700 dark:text-red-300">
            {scenario.expectedRedFlags.map((flag, i) => (<li key={i}>{flag}</li>))}
          </ul>
        </div>
      )}

      <div className="flex items-center justify-between">
        <div className="tabular-nums text-3xl font-bold text-purple-600 dark:text-purple-400">
          {formatTime(prepCountdown)}
        </div>
        <button onClick={onStart} type="button"
          className="flex items-center gap-2 rounded-xl bg-purple-600 px-6 py-2.5 font-semibold text-white transition-colors hover:bg-purple-700">
          <Mic className="h-4 w-4" /> Start now
        </button>
      </div>
    </section>
  );
}
