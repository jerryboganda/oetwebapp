'use client';

import { Badge } from '@/components/ui/badge';

const BLANK_PATTERN = /(____+|\[\s*\]|\{\{\s*blank\s*\}\})/i;

export interface PartARendererProps {
  questionNumber: number;
  partLabel: string;
  prompt: string;
  inputId?: string;
  value: string;
  onChange: (value: string) => void;
  locked?: boolean;
}

export function PartARenderer({
  questionNumber,
  partLabel,
  prompt,
  inputId: providedInputId,
  value,
  onChange,
  locked = false,
}: PartARendererProps) {
  const inputId = providedInputId ?? `listening-answer-q${questionNumber}`;
  const segments = prompt.split(BLANK_PATTERN).filter(Boolean);
  const hasAuthoredBlank = segments.some((segment) => BLANK_PATTERN.test(segment));

  const answerInput = (
    <input
      id={inputId}
      type="text"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      readOnly={locked}
      autoComplete="off"
      autoCapitalize="none"
      spellCheck={false}
      placeholder="Write the exact words heard"
      className="mx-1 inline-flex min-h-11 min-w-44 rounded-lg border border-border bg-surface px-3 py-2 text-[1em] font-semibold text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/15 read-only:cursor-not-allowed read-only:bg-background-light read-only:text-muted sm:min-w-56"
      aria-label={`Answer for question ${questionNumber}`}
      aria-describedby={`${inputId}-hint`}
    />
  );

  return (
    <section
      data-testid="part-a-clinical-note"
      aria-labelledby={`part-a-q${questionNumber}-label`}
      className="rounded-2xl border border-border bg-surface p-5 shadow-sm sm:p-6"
    >
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Badge variant="info">Q{questionNumber}</Badge>
        <span id={`part-a-q${questionNumber}-label`} className="text-xs font-black uppercase tracking-widest text-muted">
          {partLabel} clinical notes
        </span>
      </div>

      <div className="rounded-xl border border-border bg-background-light p-4">
        {hasAuthoredBlank ? (
          <p className="text-[1em] leading-10 text-navy">
            {segments.map((segment, segmentIndex) => {
              if (BLANK_PATTERN.test(segment)) {
                return <span key={`blank-${segmentIndex}`}>{answerInput}</span>;
              }
              return <span key={`text-${segmentIndex}`}>{segment}</span>;
            })}
          </p>
        ) : (
          <div className="space-y-3">
            <p className="text-[1em] leading-relaxed text-navy">{prompt}</p>
            {answerInput}
          </div>
        )}
      </div>

      <p id={`${inputId}-hint`} className="sr-only">
        Type the short answer for Part A question {questionNumber}. Spelling and word form are assessed after submission.
      </p>
    </section>
  );
}