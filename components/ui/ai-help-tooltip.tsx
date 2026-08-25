'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { Sparkles, X } from 'lucide-react';

export type AiHelpVariant = 'class' | 'reading' | 'listening' | 'vocabulary';

interface AiHelpTooltipProps {
  variant: AiHelpVariant;
  /** Optional extra classes for the wrapper. */
  className?: string;
}

const GUIDANCE: Record<
  AiHelpVariant,
  { title: string; intro: string; points: string[] }
> = {
  class: {
    title: 'AI study assistant',
    intro: 'Ask the AI assistant to make sense of any class, right from the recording.',
    points: [
      'Summarise a live or past class into clear revision notes.',
      'Explain a tricky section or concept in plain language.',
      'Generate quick recall questions to test yourself afterwards.',
      'Answers are generated from the class content and are advisory only.',
    ],
  },
  reading: {
    title: 'AI reading helper',
    intro: 'Stuck on a Reading passage? The AI helper explains it without changing your marks.',
    points: [
      'Clarify what a paragraph or sentence actually means.',
      'Break down the passage structure and key arguments.',
      'Highlight the evidence behind a correct answer.',
      'Every answer is grounded in the stored passage and is advisory only.',
    ],
  },
  listening: {
    title: 'AI listening helper',
    intro: 'Unsure why an answer is right? The AI helper walks you through the evidence.',
    points: [
      'Show why the transcript supports a particular answer.',
      'Explain unfamiliar phrases or speaker intent.',
      'Point out the exact moment the evidence appears.',
      'Answers are grounded in approved Listening evidence and are advisory only.',
    ],
  },
  vocabulary: {
    title: 'AI glossary',
    intro: 'When a word is not yet in the catalog, the AI can suggest a definition.',
    points: [
      'Get a short, context-aware definition and example sentence.',
      'Hear the suggested pronunciation where available.',
      'Add the suggestion straight to your word bank.',
      'Suggested glosses are advisory and reviewed before they count as catalog terms.',
    ],
  },
};

/**
 * AiHelpTooltip — a consistent, professional "AI help" affordance.
 *
 * Renders a small icon button that, on click, reveals a popover with
 * static guidance for the given surface. Replaces the previous
 * interactive "Ask AI" chat/gloss surfaces that failed on the backend.
 */
export function AiHelpTooltip({ variant, className }: AiHelpTooltipProps) {
  const [open, setOpen] = useState(false);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const guide = GUIDANCE[variant];

  useEffect(() => {
    if (!open) return;
    function onKey(ev: KeyboardEvent) {
      if (ev.key === 'Escape') setOpen(false);
    }
    function onDoc(ev: MouseEvent) {
      if (!wrapperRef.current) return;
      if (!wrapperRef.current.contains(ev.target as Node)) setOpen(false);
    }
    window.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onDoc);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onDoc);
    };
  }, [open]);

  return (
    <div ref={wrapperRef} className={`relative inline-flex ${className ?? ''}`}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={panelId}
        aria-label={`About the ${guide.title}`}
        className="inline-flex items-center gap-1.5 rounded-full border border-primary/20 bg-primary/5 px-2.5 py-1 text-xs font-medium text-primary transition-colors hover:bg-primary/10 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700/10 dark:text-violet-300 dark:border-violet-400/20"
      >
        <Sparkles className="h-3.5 w-3.5" />
        AI help
      </button>

      <AnimatePresence>
        {open ? (
          <motion.div
            id={panelId}
            role="dialog"
            aria-label={guide.title}
            initial={{ opacity: 0, y: 6, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 6, scale: 0.98 }}
            transition={{ duration: 0.15 }}
            className="absolute right-0 top-full z-50 mt-2 w-72 max-w-[90vw] rounded-2xl border border-border bg-surface p-4 text-left shadow-xl"
          >
            <div className="mb-2 flex items-start justify-between gap-3">
              <div className="flex items-center gap-2">
                <Sparkles className="h-4 w-4 text-primary" />
                <span className="text-sm font-semibold text-navy dark:text-white">{guide.title}</span>
              </div>
              <button
                type="button"
                onClick={() => setOpen(false)}
                aria-label="Close"
                className="rounded-full p-1 text-muted hover:bg-background-light hover:text-navy"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            <p className="text-xs leading-5 text-muted">{guide.intro}</p>
            <ul className="mt-3 space-y-1.5">
              {guide.points.map((point) => (
                <li key={point} className="flex gap-2 text-xs leading-5 text-navy dark:text-white/90">
                  <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-primary" />
                  {point}
                </li>
              ))}
            </ul>
          </motion.div>
        ) : null}
      </AnimatePresence>
    </div>
  );
}
