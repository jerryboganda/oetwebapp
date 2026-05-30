'use client';

import { useMemo, useState, type ReactNode } from 'react';

import { WRITING_PROFESSION_LABELS } from '@/lib/writing/types';
import { WRITING_LETTER_TYPE_LABELS, type WritingTaskFormState } from './builder-state';

interface TaskPreviewProps {
  form: WritingTaskFormState;
}

type PreviewTab = 'paper' | 'computer';

/**
 * Representative learner-facing preview of the task (spec §19.2). Two modes:
 *
 * - Paper: a single booklet column (case notes + task + instructions stacked).
 * - Computer: a split layout (notes/task on the left, a writing surface stub on
 *   the right) approximating the on-screen simulation.
 *
 * Read-only and deliberately lightweight — it does not reproduce the real
 * timed simulation, only how the source material reads to a candidate.
 */
export function TaskPreview({ form }: TaskPreviewProps) {
  const [tab, setTab] = useState<PreviewTab>('paper');

  const tabs: Array<{ id: PreviewTab; label: string }> = [
    { id: 'paper', label: 'Paper preview' },
    { id: 'computer', label: 'Computer preview' },
  ];

  return (
    <div className="space-y-4">
      <div
        className="inline-flex rounded-lg border border-slate-200 bg-slate-50 p-0.5"
        role="tablist"
        aria-label="Preview mode"
      >
        {tabs.map((t) => {
          const active = tab === t.id;
          return (
            <button
              key={t.id}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setTab(t.id)}
              className={`rounded-md px-3 py-1.5 text-sm font-medium outline-none transition-colors duration-150 focus-visible:ring-2 focus-visible:ring-violet-200 motion-reduce:transition-none ${
                active
                  ? 'bg-white text-violet-700 shadow-sm'
                  : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              {t.label}
            </button>
          );
        })}
      </div>

      {tab === 'paper' ? <PaperPreview form={form} /> : <ComputerPreview form={form} />}
    </div>
  );
}

function CaseNotesBlock({ form }: TaskPreviewProps) {
  const sections = form.caseNoteSections.filter(
    (s) => s.heading.trim() || s.items.some((i) => i.trim()),
  );
  if (sections.length === 0) {
    return (
      <p className="text-sm italic text-slate-400">
        Case notes will appear here once you add sections.
      </p>
    );
  }
  return (
    <div className="space-y-4">
      {sections.map((section) => {
        const items = section.items.filter((i) => i.trim());
        return (
          <div key={section.key}>
            <h4 className="text-sm font-semibold uppercase tracking-wide text-slate-700">
              {section.heading || '(untitled)'}
            </h4>
            <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm leading-relaxed text-slate-700">
              {items.length > 0 ? (
                items.map((item, i) => <li key={i}>{item}</li>)
              ) : (
                <li className="list-none italic text-slate-400">(no notes)</li>
              )}
            </ul>
          </div>
        );
      })}
    </div>
  );
}

function TaskBlock({ form }: TaskPreviewProps) {
  const professionLabel = useMemo(
    () => WRITING_PROFESSION_LABELS[form.profession],
    [form.profession],
  );
  const letterTypeLabel = useMemo(
    () => WRITING_LETTER_TYPE_LABELS[form.letterType],
    [form.letterType],
  );
  const instructions = form.fixedInstructions.filter((l) => l.trim());
  const recipientLines = [
    form.recipient.name,
    form.recipient.role,
    form.recipient.organisation,
    form.recipient.address,
  ].filter((l) => l && l.trim());

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-1.5">
        <Tag>{professionLabel}</Tag>
        <Tag>{letterTypeLabel}</Tag>
        {form.todayDate.trim() && <Tag>Today: {form.todayDate}</Tag>}
      </div>

      {form.writerRole.trim() && (
        <p className="text-sm text-slate-700">
          <span className="font-semibold">Your role: </span>
          {form.writerRole}
        </p>
      )}

      {recipientLines.length > 0 && (
        <div className="text-sm text-slate-700">
          <span className="font-semibold">Write to:</span>
          <div className="mt-0.5 whitespace-pre-line text-slate-600">
            {recipientLines.join('\n')}
          </div>
        </div>
      )}

      <div>
        <h4 className="text-sm font-semibold text-slate-700">Writing task</h4>
        <p className="mt-1 whitespace-pre-line text-sm leading-relaxed text-slate-700">
          {form.taskPromptMarkdown.trim() || (
            <span className="italic text-slate-400">
              The task prompt will appear here.
            </span>
          )}
        </p>
      </div>

      {instructions.length > 0 && (
        <ul className="list-disc space-y-0.5 pl-5 text-sm leading-relaxed text-slate-600">
          {instructions.map((line, i) => (
            <li key={i}>{line}</li>
          ))}
        </ul>
      )}

      <p className="text-sm font-medium text-slate-700">
        In your answer, write approximately {form.wordGuideMin}–{form.wordGuideMax}{' '}
        words.
      </p>
    </div>
  );
}

function Tag({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex items-center rounded-full bg-violet-50 px-2 py-0.5 text-xs font-medium text-violet-700">
      {children}
    </span>
  );
}

function PaperPreview({ form }: TaskPreviewProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-100 p-4">
      <div className="mx-auto max-w-2xl space-y-6 rounded-lg bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <header className="border-b border-slate-200 pb-3">
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-400">
            Occupational English Test · Writing
          </p>
          <h3 className="mt-1 text-lg font-semibold text-slate-900">
            {form.title.trim() || 'Untitled task'}
          </h3>
        </header>
        <section>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-400">
            Notes
          </p>
          <CaseNotesBlock form={form} />
        </section>
        <section className="border-t border-slate-200 pt-4">
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-400">
            Task
          </p>
          <TaskBlock form={form} />
        </section>
      </div>
    </div>
  );
}

function ComputerPreview({ form }: TaskPreviewProps) {
  return (
    <div className="overflow-hidden rounded-xl border border-slate-300 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-slate-200 bg-slate-800 px-4 py-2">
        <span className="text-sm font-medium text-white">
          {form.title.trim() || 'Untitled task'}
        </span>
        <span className="rounded bg-slate-700 px-2 py-0.5 text-xs font-medium tabular-nums text-slate-200">
          45:00
        </span>
      </div>
      <div className="grid gap-px bg-slate-200 md:grid-cols-2">
        <div className="space-y-5 bg-white p-4">
          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-400">
              Notes
            </p>
            <CaseNotesBlock form={form} />
          </section>
          <section className="border-t border-slate-200 pt-4">
            <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-400">
              Task
            </p>
            <TaskBlock form={form} />
          </section>
        </div>
        <div className="flex flex-col bg-slate-50 p-4">
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-slate-400">
            Your response
          </p>
          <div className="min-h-40 flex-1 rounded-lg border border-dashed border-slate-300 bg-white p-3 text-sm text-slate-400">
            The candidate types their letter here.
          </div>
          <div className="mt-2 flex items-center justify-between text-xs text-slate-500">
            <span>0 words</span>
            <span>
              Guide: {form.wordGuideMin}–{form.wordGuideMax}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
