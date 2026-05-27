'use client';

import { useMemo, useState } from 'react';
import { GripVertical } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import type {
  WritingDrillDto,
  WritingDrillResponseDto,
} from '@/lib/writing/types';

export interface DrillCardProps {
  drill: WritingDrillDto;
  onSubmit: (response: WritingDrillResponseDto) => void | Promise<void>;
  submitting?: boolean;
  className?: string;
}

/**
 * Single drill input card. Renders the right input variant based on
 * the drill's `inputVariant` field:
 *   - mcq      — radio group, single correct
 *   - fill     — short text input, expected-answer check
 *   - open     — multi-line textarea + char counter, AI-graded
 *   - drag-drop — ordered list of items the learner reorders
 *
 * All variants share the same submit contract (`WritingDrillResponseDto`).
 */
export function DrillCard({ drill, onSubmit, submitting = false, className }: DrillCardProps) {
  const [text, setText] = useState('');
  const [selected, setSelected] = useState<number | null>(null);
  const [ordered, setOrdered] = useState<string[]>(() =>
    drill.options ? [...drill.options] : drill.alternatives ?? [],
  );
  const [dragIndex, setDragIndex] = useState<number | null>(null);

  const charCount = useMemo(() => text.length, [text]);

  const handleSubmit = () => {
    const base = { drillId: drill.id, responseText: '' };
    if (drill.inputVariant === 'mcq') {
      if (selected === null) return;
      void onSubmit({ ...base, responseText: drill.options?.[selected] ?? '', selectedOptionIndex: selected });
    } else if (drill.inputVariant === 'drag-drop') {
      void onSubmit({ ...base, responseText: ordered.join(' | '), orderedItems: ordered });
    } else {
      if (!text.trim()) return;
      void onSubmit({ ...base, responseText: text.trim() });
    }
  };

  const canSubmit =
    drill.inputVariant === 'mcq'
      ? selected !== null
      : drill.inputVariant === 'drag-drop'
        ? ordered.length > 0
        : text.trim().length > 0;

  // ─── DnD handlers (HTML5 drag/drop, keyboard reorder fallback) ───────────
  const reorder = (from: number, to: number) => {
    setOrdered((prev) => {
      if (from < 0 || from >= prev.length || to < 0 || to >= prev.length) return prev;
      const next = prev.slice();
      const [moved] = next.splice(from, 1);
      next.splice(to, 0, moved);
      return next;
    });
  };

  return (
    <Card padding="lg" className={cn('flex flex-col gap-4', className)} aria-label={`Drill: ${drill.drillType}`}>
      <CardContent>
        <div className="flex items-center justify-between gap-2 mb-3 flex-wrap">
          <h3 className="font-extrabold text-base">
            {drill.drillType.replace(/-/g, ' ').replace(/\b\w/g, (s) => s.toUpperCase())}
          </h3>
          <span className="text-[10px] uppercase tracking-wider font-bold text-muted">
            Targets {drill.targetSubSkill}
            {drill.targetCanonRuleId ? ` · ${drill.targetCanonRuleId}` : ''}
          </span>
        </div>

        <div className="prose prose-sm dark:prose-invert max-w-none">
          <p className="whitespace-pre-wrap text-sm">{drill.promptMarkdown}</p>
        </div>

        <div className="mt-4">
          {drill.inputVariant === 'mcq' ? (
            <fieldset>
              <legend className="sr-only">Choose the correct option</legend>
              <div className="space-y-2">
                {(drill.options ?? []).map((opt, idx) => (
                  <label
                    key={idx}
                    className={cn(
                      'flex items-start gap-2 rounded-lg border p-2.5 cursor-pointer transition-colors',
                      selected === idx ? 'border-primary bg-primary/5' : 'border-border hover:border-primary/50',
                    )}
                  >
                    <input
                      type="radio"
                      name={`drill-${drill.id}`}
                      value={idx}
                      checked={selected === idx}
                      onChange={() => setSelected(idx)}
                      className="mt-0.5 accent-primary"
                    />
                    <span className="text-sm">{opt}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          ) : drill.inputVariant === 'fill' ? (
            <label className="block">
              <span className="block text-xs font-bold text-muted mb-1">Your answer</span>
              <input
                type="text"
                value={text}
                onChange={(e) => setText(e.target.value)}
                className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                placeholder="Type the canon-form sentence…"
                maxLength={500}
                aria-label="Drill answer"
              />
            </label>
          ) : drill.inputVariant === 'drag-drop' ? (
            <ul className="space-y-2" aria-label="Reorder the items">
              {ordered.map((item, idx) => (
                <li
                  key={`${item}-${idx}`}
                  className="flex items-center gap-2 rounded-lg border border-border bg-surface p-2.5 cursor-move"
                  draggable
                  onDragStart={() => setDragIndex(idx)}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => {
                    if (dragIndex !== null && dragIndex !== idx) reorder(dragIndex, idx);
                    setDragIndex(null);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'ArrowUp') {
                      e.preventDefault();
                      reorder(idx, Math.max(0, idx - 1));
                    } else if (e.key === 'ArrowDown') {
                      e.preventDefault();
                      reorder(idx, Math.min(ordered.length - 1, idx + 1));
                    }
                  }}
                  tabIndex={0}
                  role="listitem"
                  aria-grabbed={dragIndex === idx}
                >
                  <GripVertical className="w-4 h-4 text-muted shrink-0" aria-hidden="true" />
                  <span className="text-sm flex-1">{item}</span>
                  <span className="text-[10px] uppercase tracking-wider font-bold text-muted">{idx + 1}</span>
                </li>
              ))}
            </ul>
          ) : (
            <label className="block">
              <span className="block text-xs font-bold text-muted mb-1">Your answer</span>
              <textarea
                value={text}
                onChange={(e) => setText(e.target.value)}
                className="w-full min-h-[120px] rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-relaxed font-sans resize-y focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                placeholder="Write the canon-form sentence…"
                maxLength={2000}
                aria-label="Drill open response"
              />
              <span className="text-xs text-muted block text-right mt-1 tabular-nums">{charCount} / 2000</span>
            </label>
          )}
        </div>

        <div className="mt-4 flex justify-end">
          <Button
            type="button"
            variant="primary"
            size="md"
            disabled={!canSubmit || submitting}
            loading={submitting}
            onClick={handleSubmit}
          >
            Submit
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
