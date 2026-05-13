'use client';

import { useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { Highlighter, Strikethrough } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

export interface BCQuestionRendererProps {
  questionNumber: number;
  partLabel: string;
  prompt: string;
  options: string[];
  value: string;
  onChange: (value: string) => void;
  locked?: boolean;
}

export function BCQuestionRenderer({
  questionNumber,
  partLabel,
  prompt,
  options,
  value,
  onChange,
  locked = false,
}: BCQuestionRendererProps) {
  const [isStemHighlighted, setIsStemHighlighted] = useState(false);
  const [struckOptions, setStruckOptions] = useState<Set<string>>(() => new Set());
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const headingId = `listening-bc-q${questionNumber}-heading`;
  const statusId = `listening-bc-q${questionNumber}-status`;
  const struckCount = struckOptions.size;

  const struckSummary = useMemo(() => {
    if (struckCount === 0) return 'No options are struck out.';
    return `${struckCount} option${struckCount === 1 ? '' : 's'} struck out.`;
  }, [struckCount]);

  const toggleStruck = (option: string) => {
    if (locked) return;
    setStruckOptions((current) => {
      const next = new Set(current);
      if (next.has(option)) {
        next.delete(option);
      } else {
        next.add(option);
      }
      return next;
    });
  };

  const selectAndFocusOption = (index: number) => {
    if (locked || options.length === 0) return;
    const nextIndex = (index + options.length) % options.length;
    const nextOption = options[nextIndex];
    onChange(nextOption);
    optionRefs.current[nextIndex]?.focus();
  };

  const handleOptionKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (locked) return;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      selectAndFocusOption(index + 1);
      return;
    }
    if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      selectAndFocusOption(index - 1);
      return;
    }
    if (event.key === 'Home') {
      event.preventDefault();
      selectAndFocusOption(0);
      return;
    }
    if (event.key === 'End') {
      event.preventDefault();
      selectAndFocusOption(options.length - 1);
    }
  };

  return (
    <section
      data-testid="bc-question-renderer"
      aria-labelledby={headingId}
      className="rounded-2xl border border-border bg-surface p-6 shadow-sm scroll-mt-48 sm:p-8"
    >
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="info">Q{questionNumber}</Badge>
          <span className="text-xs font-black uppercase tracking-widest text-muted">
            {partLabel}
          </span>
        </div>
        <Button
          type="button"
          variant={isStemHighlighted ? 'secondary' : 'outline'}
          size="sm"
          aria-pressed={isStemHighlighted}
          aria-label={`${isStemHighlighted ? 'Remove highlight from' : 'Highlight'} question ${questionNumber} stem`}
          onClick={() => !locked && setIsStemHighlighted((current) => !current)}
          disabled={locked}
        >
          <Highlighter className="h-4 w-4" aria-hidden="true" />
          Stem
        </Button>
      </div>

      <h3
        id={headingId}
        className={`mb-6 rounded-xl p-3 text-[1.125em] font-medium leading-relaxed text-navy transition-colors ${
          isStemHighlighted ? 'bg-warning/20 ring-2 ring-warning/30' : 'bg-background-light'
        }`}
      >
        {prompt}
      </h3>

      <div role="radiogroup" aria-labelledby={headingId} aria-describedby={statusId} className="space-y-3">
        {options.map((option, index) => {
          const isSelected = value === option;
          const isStruck = struckOptions.has(option);
          const optionLabel = String.fromCharCode(65 + index);
          return (
            <div key={option} className="flex gap-2">
              <button
                type="button"
                role="radio"
                aria-checked={isSelected}
                aria-describedby={isStruck ? statusId : undefined}
                tabIndex={isSelected || (!value && index === 0) ? 0 : -1}
                ref={(element) => {
                  optionRefs.current[index] = element;
                }}
                onClick={() => !locked && onChange(option)}
                onKeyDown={(event) => handleOptionKeyDown(event, index)}
                onContextMenu={(event) => {
                  event.preventDefault();
                  toggleStruck(option);
                }}
                disabled={locked}
                className={`flex min-h-14 flex-1 items-center gap-4 rounded-xl border-2 p-4 text-left transition-all sm:p-5 ${
                  isSelected
                    ? 'border-primary bg-primary/5 font-medium text-primary'
                    : 'border-border text-navy hover:border-border-hover hover:bg-background-light'
                } ${locked ? 'cursor-not-allowed opacity-60' : ''}`}
              >
                <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border-2 text-[0.75em] font-black transition-colors ${
                  isSelected ? 'border-primary bg-primary text-white' : 'border-border-hover text-muted'
                }`}>
                  {optionLabel}
                </span>
                <span className={`leading-relaxed ${isStruck ? 'text-muted line-through decoration-2' : ''}`}>{option}</span>
              </button>
              <Button
                type="button"
                variant={isStruck ? 'secondary' : 'outline'}
                size="sm"
                aria-pressed={isStruck}
                aria-label={`${isStruck ? 'Remove strikethrough from' : 'Strike out'} option ${optionLabel}`}
                onClick={() => toggleStruck(option)}
                disabled={locked}
                className="self-stretch px-3"
              >
                <Strikethrough className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>
          );
        })}
      </div>

      <p id={statusId} className="sr-only" aria-live="polite">
        {struckSummary}
      </p>
    </section>
  );
}