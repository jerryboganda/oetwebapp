'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronLeft, ChevronRight, Highlighter, Minus, Pin, Strikethrough, StickyNote } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ReadingQuestionDto {
  id: string;
  passageId: string;
  stem: string;
  options: { key: string; text: string }[];
  questionType: string;
  partCode: number;
  skillCode?: string;
}

export interface ReadingPassageDto {
  id: string;
  title: string;
  bodyHtml: string;
  partCode: number;
}

export interface ReadingPlayerProps {
  mode: 'drill' | 'diagnostic' | 'mock' | 'review';
  questions: ReadingQuestionDto[];
  passages: ReadingPassageDto[];
  sessionId: string;
  onComplete: (answers: Record<string, string>) => void;
  focusSkill?: string;
  timeLimitSeconds?: number;
}

// ─── Progress dots (inline for co-location) ───────────────────────────────────

interface ProgressDotsProps {
  total: number;
  current: number;
  answered: Set<string>;
  marked: Set<string>;
  questionIds: string[];
  onJump: (index: number) => void;
}

function ProgressDots({ total, current, answered, marked, questionIds, onJump }: ProgressDotsProps) {
  return (
    <div className="flex flex-wrap gap-1" role="list" aria-label="Question progress">
      {Array.from({ length: total }, (_, i) => {
        const qid = questionIds[i];
        const isAnswered = qid ? answered.has(qid) : false;
        const isMarked = qid ? marked.has(qid) : false;
        const isCurrent = i === current;
        return (
          <button
            key={i}
            type="button"
            role="listitem"
            aria-label={`Question ${i + 1}${isAnswered ? ' answered' : ''}${isMarked ? ' marked' : ''}${isCurrent ? ' current' : ''}`}
            onClick={() => onJump(i)}
            className={cn(
              'h-5 w-5 rounded-full text-[9px] font-bold transition-[color,background-color,box-shadow] duration-200',
              isCurrent && 'ring-2 ring-primary ring-offset-1',
              isMarked
                ? 'bg-amber-400 text-white'
                : isAnswered
                  ? 'bg-emerald-500 text-white'
                  : 'bg-background-light text-muted dark:bg-border',
            )}
          >
            {i + 1}
          </button>
        );
      })}
    </div>
  );
}

// ─── Passage panel ────────────────────────────────────────────────────────────

type AnnotationTool = 'highlight' | 'underline' | 'strike' | 'note';

interface PlayerAnnotationRange {
  start: number;
  end: number;
}

function PassagePanel({ passage }: { passage: ReadingPassageDto | null }) {
  const scopeRef = useRef<HTMLDivElement>(null);
  const [activeTool, setActiveTool] = useState<AnnotationTool | null>(null);
  const [notePopover, setNotePopover] = useState<{ range: PlayerAnnotationRange; text: string } | null>(null);

  // Escape clears the pending selection / cancels an open note popover without
  // touching committed annotations.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      window.getSelection?.()?.removeAllRanges();
      setNotePopover(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const handleMouseUp = useCallback(() => {
    const scope = scopeRef.current;
    if (!scope || !activeTool) return;
    const range = readPlayerScopeSelection(scope);
    if (!range) return;
    if (activeTool === 'note') {
      // Capture the selection offsets before the popover steals focus.
      setNotePopover({ range, text: '' });
      window.getSelection?.()?.removeAllRanges();
      return;
    }
    paintPlayerAnnotation(scope, range, activeTool);
    window.getSelection?.()?.removeAllRanges();
  }, [activeTool]);

  const handleSaveNote = useCallback(() => {
    const scope = scopeRef.current;
    if (!scope || !notePopover) return;
    paintPlayerAnnotation(scope, notePopover.range, 'note', notePopover.text.trim() || 'Note');
    setNotePopover(null);
  }, [notePopover]);

  const toggleTool = useCallback((tool: AnnotationTool) => {
    setActiveTool((current) => (current === tool ? null : tool));
    setNotePopover(null);
  }, []);

  if (!passage) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted">
        No passage for this question.
      </div>
    );
  }

  const tools: Array<{ tool: AnnotationTool; label: string; icon: typeof Highlighter; active: string }> = [
    { tool: 'highlight', label: 'Highlight', icon: Highlighter, active: 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300' },
    { tool: 'underline', label: 'Underline', icon: Minus, active: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300' },
    { tool: 'strike', label: 'Strikethrough', icon: Strikethrough, active: 'bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300' },
    { tool: 'note', label: 'Note', icon: StickyNote, active: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300' },
  ];

  return (
    <div className="relative flex h-full flex-col">
      {/* Floating annotation toolbar */}
      <div className="mb-3 flex items-center gap-1 rounded-lg border border-border bg-surface px-3 py-2 shadow-sm">
        <span className="mr-2 text-xs font-semibold text-muted uppercase tracking-wide">Annotate</span>
        {tools.map(({ tool, label, icon: Icon, active }) => {
          const isActive = activeTool === tool;
          return (
            <button
              key={tool}
              type="button"
              title={label}
              aria-label={label}
              aria-pressed={isActive}
              // Prevent the click from collapsing the selection before mouseup applies it.
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => toggleTool(tool)}
              className={cn(
                'rounded p-1.5 text-muted transition-colors hover:bg-background-light',
                isActive && active,
              )}
            >
              <Icon className="h-4 w-4" aria-hidden />
            </button>
          );
        })}
        {activeTool ? (
          <span className="ml-2 text-[10px] font-semibold uppercase tracking-wide text-primary" aria-live="polite">
            {activeTool} on
          </span>
        ) : null}
      </div>

      {notePopover ? (
        <div
          className="absolute left-3 right-3 top-16 z-20 rounded-lg border border-border bg-surface p-3 shadow-lg"
          role="dialog"
          aria-label="Add note"
        >
          <label className="text-xs font-semibold text-muted" htmlFor="reading-player-note-input">
            Note for selected text
          </label>
          <textarea
            id="reading-player-note-input"
            autoFocus
            value={notePopover.text}
            onChange={(event) => setNotePopover((prev) => (prev ? { ...prev, text: event.target.value } : prev))}
            className="mt-1 h-16 w-full resize-none rounded-md border border-border bg-background-light px-2 py-1.5 text-sm text-navy outline-none focus:border-primary"
            placeholder="Type a note…"
          />
          <div className="mt-2 flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setNotePopover(null)}>Cancel</Button>
            <Button variant="primary" size="sm" onClick={handleSaveNote}>Save note</Button>
          </div>
        </div>
      ) : null}

      <div className="mb-2 text-sm font-semibold text-navy">{passage.title}</div>
      <div
        ref={scopeRef}
        onMouseUp={handleMouseUp}
        data-reading-annotate-scope="passage"
        className="prose prose-sm max-w-[70ch] flex-1 overflow-y-auto rounded-lg border border-border bg-surface p-4 text-sm leading-relaxed text-navy selection:bg-warning/30"
        dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(passage.bodyHtml) }}
      />
    </div>
  );
}

// ─── Question panel ───────────────────────────────────────────────────────────

interface QuestionPanelProps {
  question: ReadingQuestionDto;
  currentIndex: number;
  total: number;
  answers: Record<string, string>;
  markedForReview: Set<string>;
  mode: ReadingPlayerProps['mode'];
  isLast: boolean;
  onSelect: (questionId: string, optionKey: string) => void;
  onToggleMark: (questionId: string) => void;
  onPrev: () => void;
  onNext: () => void;
  onSubmit: () => void;
  feedbackVisible: boolean;
  correctKey: string | null;
}

function QuestionPanel({
  question,
  currentIndex,
  total,
  answers,
  markedForReview,
  mode,
  isLast,
  onSelect,
  onToggleMark,
  onPrev,
  onNext,
  onSubmit,
  feedbackVisible,
  correctKey,
}: QuestionPanelProps) {
  const selectedKey = answers[question.id];
  const isMarked = markedForReview.has(question.id);

  return (
    <div className="flex h-full flex-col gap-4 overflow-y-auto">
      {/* Q counter */}
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted">
          Q{currentIndex + 1} of {total}
        </span>
        <button
          type="button"
          onClick={() => onToggleMark(question.id)}
          title={isMarked ? 'Unmark' : 'Mark for review'}
          className={cn(
            'flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors',
            isMarked
              ? 'border-amber-300 bg-amber-50 text-amber-700 dark:border-amber-600 dark:bg-amber-900/30 dark:text-amber-400'
              : 'border-border text-muted hover:border-amber-300 hover:bg-amber-50',
          )}
        >
          <Pin className="h-3 w-3" aria-hidden />
          {isMarked ? 'Marked' : 'Mark'}
        </button>
      </div>

      {/* Stem */}
      <p className="text-sm font-medium leading-relaxed text-navy">{question.stem}</p>

      {/* Options */}
      <div className="flex flex-col gap-2" role="radiogroup" aria-label="Answer options">
        {question.options.map((opt) => {
          const isSelected = selectedKey === opt.key;
          const isCorrect = feedbackVisible && correctKey === opt.key;
          const isWrong = feedbackVisible && isSelected && selectedKey !== correctKey;
          return (
            <button
              key={opt.key}
              type="button"
              role="radio"
              aria-checked={isSelected}
              onClick={() => onSelect(question.id, opt.key)}
              className={cn(
                'flex items-start gap-3 rounded-xl border px-4 py-3 text-left text-sm transition-[color,background-color,border-color] duration-200',
                isCorrect
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-800 dark:border-emerald-600 dark:bg-emerald-900/20 dark:text-emerald-300'
                  : isWrong
                    ? 'border-rose-400 bg-rose-50 text-rose-800 dark:border-rose-600 dark:bg-rose-900/20 dark:text-rose-300'
                    : isSelected
                      ? 'border-primary bg-primary/5 text-primary dark:border-violet-500 dark:bg-violet-900/20 dark:text-violet-300'
                      : 'border-border bg-surface hover:border-primary/40 hover:bg-primary/5',
              )}
            >
              <span
                className={cn(
                  'mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full border text-xs font-bold',
                  isSelected ? 'border-current bg-current text-white' : 'border-current',
                  isCorrect && 'border-emerald-500 bg-emerald-500 text-white',
                  isWrong && 'border-rose-500 bg-rose-500 text-white',
                )}
              >
                {opt.key}
              </span>
              <span className="leading-relaxed">{opt.text}</span>
            </button>
          );
        })}
      </div>

      {/* Diagnostic: "I don't know" */}
      {mode === 'diagnostic' && !feedbackVisible && (
        <button
          type="button"
          onClick={() => onSelect(question.id, '__unknown__')}
          className="self-start rounded-lg border border-border px-3 py-2 text-xs text-muted hover:bg-surface"
        >
          I don&apos;t know
        </button>
      )}

      {/* Feedback (drill / review) */}
      {feedbackVisible && (mode === 'drill' || mode === 'review') && (
        <div
          className={cn(
            'rounded-xl border px-4 py-3 text-sm font-semibold',
            selectedKey === correctKey
              ? 'border-emerald-300 bg-emerald-50 text-emerald-700 dark:border-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-400'
              : 'border-rose-300 bg-rose-50 text-rose-700 dark:border-rose-700 dark:bg-rose-900/20 dark:text-rose-400',
          )}
        >
          {selectedKey === correctKey ? '✓ Correct' : '✗ Incorrect'}
        </div>
      )}

      {/* Navigation */}
      <div className="mt-auto flex items-center justify-between gap-3 pt-2">
        <Button variant="outline" size="sm" onClick={onPrev} disabled={currentIndex === 0}>
          <ChevronLeft className="h-4 w-4" aria-hidden /> Prev
        </Button>
        {isLast ? (
          <Button variant="primary" size="sm" onClick={onSubmit}>
            Submit
          </Button>
        ) : (
          <Button variant="primary" size="sm" onClick={onNext}>
            Next <ChevronRight className="h-4 w-4" aria-hidden />
          </Button>
        )}
      </div>
    </div>
  );
}

// ─── Timer display ────────────────────────────────────────────────────────────

function TimerDisplay({
  elapsed,
  remaining,
  mode,
}: {
  elapsed: number;
  remaining: number | null;
  mode: ReadingPlayerProps['mode'];
}) {
  const fmt = (seconds: number) => {
    const m = Math.floor(Math.abs(seconds) / 60);
    const s = Math.abs(seconds) % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  };

  const isLowTime = remaining !== null && remaining < 300;

  return (
    <div className="flex items-center gap-2 text-sm">
      {mode === 'mock' && remaining !== null ? (
        <span
          className={cn(
            'font-mono font-semibold tabular-nums',
            isLowTime ? 'text-danger' : 'text-navy',
          )}
        >
          {fmt(remaining)}
        </span>
      ) : (
        <span className="font-mono text-muted tabular-nums">{fmt(elapsed)}</span>
      )}
    </div>
  );
}

// ─── Mobile tab bar ───────────────────────────────────────────────────────────

type MobileTab = 'passage' | 'question';

function MobileTabBar({ active, onChange }: { active: MobileTab; onChange: (t: MobileTab) => void }) {
  return (
    <div className="flex border-b border-border">
      {(['passage', 'question'] as const).map((tab) => (
        <button
          key={tab}
          type="button"
          onClick={() => onChange(tab)}
          className={cn(
            'flex-1 py-3 text-xs font-semibold uppercase tracking-wider transition-colors',
            active === tab
              ? 'border-b-2 border-primary text-primary'
              : 'text-muted hover:text-navy',
          )}
        >
          {tab}
        </button>
      ))}
    </div>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export default function ReadingPlayer({
  mode,
  questions,
  passages,
  sessionId: _sessionId,
  onComplete,
  timeLimitSeconds,
}: ReadingPlayerProps) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [markedForReview, setMarkedForReview] = useState<Set<string>>(new Set());
  const [elapsed, setElapsed] = useState(0);
  const [remaining, setRemaining] = useState<number | null>(
    timeLimitSeconds ?? null,
  );
  const [feedbackVisible, setFeedbackVisible] = useState(false);
  const [mobileTab, setMobileTab] = useState<MobileTab>('passage');

  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Timer
  useEffect(() => {
    intervalRef.current = setInterval(() => {
      setElapsed((e) => e + 1);
      if (timeLimitSeconds !== undefined) {
        setRemaining((r) => (r !== null ? Math.max(0, r - 1) : null));
      }
    }, 1000);
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [timeLimitSeconds]);

  // Auto-submit when time expires in mock mode
  useEffect(() => {
    if (mode === 'mock' && remaining === 0) {
      if (intervalRef.current) clearInterval(intervalRef.current);
      onComplete(answers);
    }
  }, [mode, remaining, answers, onComplete]);

  const currentQuestion = questions[currentIndex];
  const currentPassage = currentQuestion
    ? passages.find((p) => p.id === currentQuestion.passageId) ?? null
    : null;

  const handleSelect = useCallback((questionId: string, optionKey: string) => {
    setAnswers((prev) => ({ ...prev, [questionId]: optionKey }));
    // In drill/review mode show feedback immediately on answer
    if (mode === 'drill' || mode === 'review') {
      setFeedbackVisible(true);
    }
  }, [mode]);

  const handleToggleMark = useCallback((questionId: string) => {
    setMarkedForReview((prev) => {
      const next = new Set(prev);
      if (next.has(questionId)) next.delete(questionId);
      else next.add(questionId);
      return next;
    });
  }, []);

  const handlePrev = useCallback(() => {
    setFeedbackVisible(false);
    setCurrentIndex((i) => Math.max(0, i - 1));
  }, []);

  const handleNext = useCallback(() => {
    setFeedbackVisible(false);
    setCurrentIndex((i) => Math.min(questions.length - 1, i + 1));
    // Switch mobile tab to passage for new question
    setMobileTab('passage');
  }, [questions.length]);

  const handleJump = useCallback((index: number) => {
    setFeedbackVisible(false);
    setCurrentIndex(index);
    setMobileTab('passage');
  }, []);

  const handleSubmit = useCallback(() => {
    if (intervalRef.current) clearInterval(intervalRef.current);
    onComplete(answers);
  }, [answers, onComplete]);

  // Keyboard shortcuts
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;

      switch (e.key) {
        case '1': currentQuestion && handleSelect(currentQuestion.id, 'A'); break;
        case '2': currentQuestion && handleSelect(currentQuestion.id, 'B'); break;
        case '3': currentQuestion && handleSelect(currentQuestion.id, 'C'); break;
        case '4': currentQuestion && handleSelect(currentQuestion.id, 'D'); break;
        case 'ArrowLeft': handlePrev(); break;
        case 'ArrowRight':
          if (currentIndex < questions.length - 1) handleNext();
          break;
        case 'Enter':
          if (currentIndex === questions.length - 1) handleSubmit();
          else handleNext();
          break;
        default: break;
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [currentQuestion, currentIndex, questions.length, handleSelect, handlePrev, handleNext, handleSubmit]);

  if (!currentQuestion) {
    return (
      <div className="flex h-64 items-center justify-center text-muted">
        No questions available.
      </div>
    );
  }

  const questionIds = questions.map((q) => q.id);
  const answeredSet = new Set(Object.keys(answers));
  // correctKey: in review mode the correct answer would come from the server;
  // for now we can't derive it client-side — show no feedback marker.
  const correctKey: string | null = null;

  const isLast = currentIndex === questions.length - 1;

  return (
    <div className="flex h-full flex-col" data-testid="reading-player-root">
      {/* ── Sticky header (timer + question jump dots) ─────────────────────────
          The OET computer-based sample test keeps a thin status strip pinned at
          the top of the screen. We mirror that affordance so the candidate always
          sees the timer and jump dots even when scrolling a long passage. */}
      <div className="sticky top-0 z-10 flex items-center justify-between gap-4 border-b border-border bg-surface px-4 py-2 shadow-sm">
        <ProgressDots
          total={questions.length}
          current={currentIndex}
          answered={answeredSet}
          marked={markedForReview}
          questionIds={questionIds}
          onJump={handleJump}
        />
        <TimerDisplay elapsed={elapsed} remaining={remaining} mode={mode} />
      </div>

      {/* ── Mobile: tab switcher (hidden at md+ where the split-screen kicks in) ─ */}
      <div className="md:hidden">
        <MobileTabBar active={mobileTab} onChange={setMobileTab} />
      </div>

      {/* ── Body: official OET-style split-screen at md+ ─────────────────────
          • Mobile (<md): one column at a time, toggled by MobileTabBar above.
          • Tablet / desktop (md+): CSS Grid two-column layout, passage left
            (~60%) and questions right (~40%), each pane scrolls independently.
          The owner directive (2026-05-27 §7) requires the official passage-left
          / questions-right exam layout — we use a fixed grid (not a draggable
          resizer) to match the official sample-test affordance exactly. */}
      <div
        className={cn(
          'flex-1 overflow-hidden',
          'md:grid md:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]',
        )}
        data-testid="reading-split-screen"
      >
        {/* Passage pane */}
        <div
          className={cn(
            'flex flex-col overflow-hidden border-border p-4 md:border-r',
            mobileTab === 'question' ? 'hidden md:flex' : 'flex',
          )}
          data-testid="reading-passage-pane"
        >
          <PassagePanel passage={currentPassage} />
        </div>

        {/* Question pane */}
        <div
          className={cn(
            'overflow-hidden p-4',
            mobileTab === 'passage' ? 'hidden md:block' : 'block',
          )}
          data-testid="reading-question-pane"
        >
          <QuestionPanel
            question={currentQuestion}
            currentIndex={currentIndex}
            total={questions.length}
            answers={answers}
            markedForReview={markedForReview}
            mode={mode}
            isLast={isLast}
            onSelect={handleSelect}
            onToggleMark={handleToggleMark}
            onPrev={handlePrev}
            onNext={handleNext}
            onSubmit={handleSubmit}
            feedbackVisible={feedbackVisible}
            correctKey={correctKey}
          />
        </div>
      </div>
    </div>
  );
}

// ─── Passage annotation helpers ───────────────────────────────────────────────
// Session-only DOM annotations scoped strictly to the passage container. Marks
// are applied with the active toolbar tool: highlight (yellow), underline,
// strikethrough, and note (dotted underline + tooltip). They never touch
// answers or scoring and live only as long as the passage HTML stays mounted.

const PLAYER_ANNOTATION_CLASS: Record<AnnotationTool, string> = {
  highlight: 'rounded-[2px] bg-amber-200/80 text-navy dark:bg-amber-300/40 dark:text-amber-50',
  underline: 'bg-transparent underline decoration-2 decoration-sky-600 underline-offset-2 dark:decoration-sky-400',
  strike: 'bg-transparent line-through decoration-2 decoration-rose-500 dark:decoration-rose-400',
  note: 'bg-amber-50 underline decoration-dotted decoration-amber-500 cursor-help dark:bg-amber-900/20',
};

function playerOffsetWithin(scope: HTMLElement, node: Node, nodeOffset: number): number {
  const measure = document.createRange();
  measure.selectNodeContents(scope);
  try {
    measure.setEnd(node, nodeOffset);
  } catch {
    return 0;
  }
  return (measure.cloneContents().textContent ?? '').length;
}

function readPlayerScopeSelection(scope: HTMLElement): PlayerAnnotationRange | null {
  if (typeof window === 'undefined') return null;
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return null;
  const range = selection.getRangeAt(0);
  if (!scope.contains(range.startContainer) || !scope.contains(range.endContainer)) return null;
  const a = playerOffsetWithin(scope, range.startContainer, range.startOffset);
  const b = playerOffsetWithin(scope, range.endContainer, range.endOffset);
  const start = Math.min(a, b);
  const end = Math.max(a, b);
  if (end - start < 1) return null;
  return { start, end };
}

function paintPlayerAnnotation(
  scope: HTMLElement,
  { start, end }: PlayerAnnotationRange,
  tool: AnnotationTool,
  title?: string,
): void {
  const segments: Array<{ node: Text; from: number; to: number }> = [];
  const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
  let offset = 0;
  let node = walker.nextNode() as Text | null;
  while (node) {
    const len = node.textContent?.length ?? 0;
    const from = Math.max(start, offset);
    const to = Math.min(end, offset + len);
    if (from < to) segments.push({ node, from: from - offset, to: to - offset });
    offset += len;
    node = walker.nextNode() as Text | null;
  }
  for (const segment of segments.reverse()) {
    const range = document.createRange();
    range.setStart(segment.node, segment.from);
    range.setEnd(segment.node, segment.to);
    const mark = document.createElement('mark');
    mark.setAttribute('data-reading-annotation', tool);
    mark.className = PLAYER_ANNOTATION_CLASS[tool];
    if (title) mark.title = title;
    try {
      range.surroundContents(mark);
    } catch {
      // Segment crosses an element boundary — skip; remaining segments still apply.
    }
  }
}

