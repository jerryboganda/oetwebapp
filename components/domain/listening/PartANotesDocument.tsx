'use client';

import { parseNotesDocument, countGaps } from '@/lib/listening-part-a-notes';
import type { NotesNode, NotesSegment } from '@/lib/listening-part-a-notes';

export interface PartANotesDocumentProps {
  partLabel: string;
  notesBody: string;
  questions: Array<{ id: string; number: number }>;
  answers: Record<string, string>;
  onAnswerChange: (questionId: string, value: string) => void;
  locked?: boolean;
  /**
   * OET Listening exam-mode rule L-R08.1 (PDF) — Part A: NO highlighting
   * available to the candidate. Drives `user-select: none` on the document so
   * native browser text-selection cannot be used as a workaround.
   *
   * Default `false` so exam mode is safe-by-default. Learning / practice
   * modes pass `true`.
   */
  highlightingEnabled?: boolean;
}

/** Shared input className mirroring PartARenderer. */
const INPUT_CLASS =
  'mx-1 inline-flex min-h-11 min-w-44 rounded-lg border border-border bg-surface px-3 py-2 text-[1em] font-semibold text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/15 read-only:cursor-not-allowed read-only:bg-background-light read-only:text-muted sm:min-w-56';

interface GapInputProps {
  gapIndex: number;
  questions: Array<{ id: string; number: number }>;
  answers: Record<string, string>;
  onAnswerChange: (questionId: string, value: string) => void;
  locked: boolean;
}

function GapInput({ gapIndex, questions, answers, onAnswerChange, locked }: GapInputProps) {
  // DEFENSIVE: if gapIndex >= questions.length render a disabled placeholder
  if (gapIndex >= questions.length) {
    return (
      <span style={{ userSelect: 'auto', WebkitUserSelect: 'auto' }}>
        <input
          type="text"
          disabled
          className={INPUT_CLASS}
          aria-label={`Unbound gap ${gapIndex + 1}`}
          spellCheck={false}
          autoCapitalize="none"
          autoCorrect="off"
          autoComplete="off"
        />
      </span>
    );
  }

  const q = questions[gapIndex];
  const inputId = `listening-answer-${q.id}`;
  return (
    <>
      <label htmlFor={inputId} className="mx-0.5 font-semibold text-navy">({q.number})</label>
      <span style={{ userSelect: 'auto', WebkitUserSelect: 'auto' }}>
        <input
          id={inputId}
          type="text"
          value={answers[q.id] ?? ''}
          onChange={(e) => onAnswerChange(q.id, e.target.value)}
          readOnly={locked}
          spellCheck={false}
          autoCapitalize="none"
          autoCorrect="off"
          autoComplete="off"
          className={INPUT_CLASS}
          aria-label={`Answer for question ${q.number}`}
        />
      </span>
    </>
  );
}

interface SegmentsProps {
  segments: NotesSegment[];
  questions: Array<{ id: string; number: number }>;
  answers: Record<string, string>;
  onAnswerChange: (questionId: string, value: string) => void;
  locked: boolean;
}

function RenderSegments({ segments, questions, answers, onAnswerChange, locked }: SegmentsProps) {
  return (
    <>
      {segments.map((seg, i) => {
        if (seg.kind === 'text') {
          return <span key={i}>{seg.text}</span>;
        }
        // seg.kind === 'gap'
        return (
          <GapInput
            key={i}
            gapIndex={seg.gapIndex}
            questions={questions}
            answers={answers}
            onAnswerChange={onAnswerChange}
            locked={locked}
          />
        );
      })}
    </>
  );
}

function RenderNode({
  node,
  questions,
  answers,
  onAnswerChange,
  locked,
}: {
  node: NotesNode;
  questions: Array<{ id: string; number: number }>;
  answers: Record<string, string>;
  onAnswerChange: (questionId: string, value: string) => void;
  locked: boolean;
}) {
  if (node.kind === 'divider') {
    return <hr className="my-3 border-border" />;
  }

  if (node.kind === 'heading') {
    return (
      <h3 className="mt-4 mb-1 text-sm font-black uppercase tracking-widest text-navy first:mt-0">
        <RenderSegments
          segments={node.segments}
          questions={questions}
          answers={answers}
          onAnswerChange={onAnswerChange}
          locked={locked}
        />
      </h3>
    );
  }

  if (node.kind === 'subheading') {
    return (
      <h4 className="mt-3 mb-0.5 text-[0.95em] font-bold tracking-tight text-navy first:mt-0">
        <RenderSegments
          segments={node.segments}
          questions={questions}
          answers={answers}
          onAnswerChange={onAnswerChange}
          locked={locked}
        />
      </h4>
    );
  }

  if (node.kind === 'bullet') {
    const indent = node.level === 2 ? 'ml-6' : 'ml-0';
    return (
      <div className={`flex items-baseline gap-1.5 leading-10 text-navy ${indent}`}>
        <span className="shrink-0 select-none text-muted" aria-hidden="true">
          {node.level === 2 ? '◦' : '•'}
        </span>
        <span className="flex flex-wrap items-center">
          <RenderSegments
            segments={node.segments}
            questions={questions}
            answers={answers}
            onAnswerChange={onAnswerChange}
            locked={locked}
          />
        </span>
      </div>
    );
  }

  // node.kind === 'context'
  return (
    <p className="text-[1em] leading-10 text-navy">
      <RenderSegments
        segments={node.segments}
        questions={questions}
        answers={answers}
        onAnswerChange={onAnswerChange}
        locked={locked}
      />
    </p>
  );
}

export function PartANotesDocument({
  partLabel,
  notesBody,
  questions,
  answers,
  onAnswerChange,
  locked = false,
  highlightingEnabled = false,
}: PartANotesDocumentProps) {
  const nodes = parseNotesDocument(notesBody);
  const gapCount = countGaps(notesBody);

  // Determine which questions are "leftover" (their index >= gap count in the document)
  const leftoverQuestions = questions.slice(gapCount);

  return (
    <section
      data-testid="part-a-notes-document"
      aria-label={`${partLabel} note-completion`}
    >
      <div
        className="rounded-xl border border-border bg-background-light p-4"
        data-highlighting-enabled={highlightingEnabled}
        style={highlightingEnabled ? undefined : { userSelect: 'none', WebkitUserSelect: 'none' }}
      >
        <div className="space-y-0.5">
          {nodes.map((node, i) => (
            <RenderNode
              key={i}
              node={node}
              questions={questions}
              answers={answers}
              onAnswerChange={onAnswerChange}
              locked={locked}
            />
          ))}
        </div>

        {/* DEFENSIVE: leftover questions whose index >= gap count get their own answer fields */}
        {leftoverQuestions.length > 0 && (
          <div className="mt-4 border-t border-border pt-3 space-y-2">
            <p className="text-xs font-semibold uppercase tracking-widest text-muted">
              Additional answer{leftoverQuestions.length > 1 ? 's' : ''}
            </p>
            {leftoverQuestions.map((q) => (
              <div key={q.id} className="flex flex-wrap items-center gap-2 leading-10">
                <label htmlFor={`listening-answer-${q.id}`} className="font-semibold text-navy">({q.number})</label>
                <span style={{ userSelect: 'auto', WebkitUserSelect: 'auto' }}>
                  <input
                    id={`listening-answer-${q.id}`}
                    type="text"
                    value={answers[q.id] ?? ''}
                    onChange={(e) => onAnswerChange(q.id, e.target.value)}
                    readOnly={locked}
                    spellCheck={false}
                    autoCapitalize="none"
                    autoCorrect="off"
                    autoComplete="off"
                    className={INPUT_CLASS}
                    aria-label={`Answer for question ${q.number}`}
                  />
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
