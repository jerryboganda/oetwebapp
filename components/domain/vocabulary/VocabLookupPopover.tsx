'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { BookOpen, Loader2, Plus, CheckCircle2, X, Volume2 } from 'lucide-react';
import {
  lookupVocabularyTerm,
  addToMyVocabulary,
  fetchRecallsAudio,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import { cleanExampleSentence } from '@/lib/vocabulary-example-sentence';
import { AiHelpTooltip } from '@/components/ui/ai-help-tooltip';
import type { VocabularyLookupResult } from '@/lib/types/vocabulary';

export type VocabLookupSource =
  | 'reading'
  | 'writing'
  | 'speaking'
  | 'listening'
  | 'mock'
  | 'generic';

export interface VocabLookupPopoverProps {
  /** The word or short phrase the user highlighted. */
  word: string;
  /** Optional surrounding context snippet (passed to the AI gloss). */
  context?: string;
  /** Surface name for analytics + sourceRef tagging. */
  source: VocabLookupSource;
  /** Opaque ref to anchor the saved term back to (e.g. "reading:cp-042:134"). */
  sourceRef?: string;
  /** Close callback — invoked when the user dismisses the popover. */
  onClose: () => void;
  /** Optional custom anchor element (for fixed positioning). */
  anchorRect?: DOMRect | null;
}

/**
 * VocabLookupPopover — the unified "save to word bank" surface for
 * Reading / Writing / Speaking / Listening / Mock.
 *
 * Flow:
 *  1. On mount, look the word up in the term catalog.
 *  2. If found → show the existing term + "Add to my list" button.
 *  3. If not found → offer "Ask AI gloss" (consumes VocabularyGloss quota).
 *  4. On add, fire analytics and close after 1s confirmation.
 */
export function VocabLookupPopover({
  word,
  context,
  source,
  sourceRef,
  onClose,
  anchorRect,
}: VocabLookupPopoverProps) {
  const [lookup, setLookup] = useState<VocabularyLookupResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [adding, setAdding] = useState(false);
  const [added, setAdded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await lookupVocabularyTerm(word);
        if (cancelled) return;
        setLookup(res as VocabularyLookupResult);
      } catch {
        if (!cancelled) setError('Lookup failed.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [word]);

  // Close on Escape or outside click.
  useEffect(() => {
    function onKey(ev: KeyboardEvent) { if (ev.key === 'Escape') onClose(); }
    function onDoc(ev: MouseEvent) {
      if (!containerRef.current) return;
      if (!containerRef.current.contains(ev.target as Node)) onClose();
    }
    window.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onDoc);
    return () => {
      window.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onDoc);
    };
  }, [onClose]);

  async function handleAdd(termId: string) {
    setAdding(true);
    try {
      await addToMyVocabulary(termId, {
        sourceRef: sourceRef ?? `${source}:unknown`,
        context,
      });
      setAdded(true);
      const analyticsEvent =
        source === 'reading' ? 'vocab_saved_from_reading' :
        source === 'writing' ? 'vocab_saved_from_writing' :
        source === 'speaking' ? 'vocab_saved_from_speaking' :
        source === 'listening' ? 'vocab_saved_from_listening' :
        source === 'mock' ? 'vocab_saved_from_mock' :
        'vocab_added';
      analytics.track(analyticsEvent, { termId, source });
      setTimeout(onClose, 1000);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not save term.';
      setError(msg);
    } finally {
      setAdding(false);
    }
  }

  async function playAudio(termId: string) {
    const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
    if (!response) return;
    playTransientAudio(response.url);
  }

  const style: React.CSSProperties | undefined = anchorRect
    ? {
        position: 'fixed',
        top: Math.min(anchorRect.bottom + 8, window.innerHeight - 360),
        left: Math.min(anchorRect.left, window.innerWidth - 360),
        zIndex: 1000,
      }
    : undefined;

  return (
    <AnimatePresence>
      <motion.div
        ref={containerRef}
        initial={{ opacity: 0, y: 6, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        exit={{ opacity: 0, y: 6, scale: 0.98 }}
        transition={{ duration: 0.15 }}
        role="dialog"
        aria-modal="false"
        aria-label={`Vocabulary lookup for ${word}`}
        className="w-[340px] max-w-[92vw] rounded-2xl border border-border bg-surface p-4 shadow-xl"
        style={style}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <BookOpen className="h-4 w-4 text-primary" />
            <span className="text-xs font-semibold uppercase text-muted">Vocabulary</span>
          </div>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-full p-1 text-muted hover:bg-background-light hover:text-navy"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-1 mb-3">
          <div className="text-lg font-bold text-navy">{word}</div>
          {context && <div className="mt-1 line-clamp-2 text-xs italic text-muted">{context}</div>}
        </div>

        {loading && (
          <div className="flex items-center gap-2 rounded-xl bg-background-light px-3 py-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Looking up…
          </div>
        )}

        {error && (
          <div className="rounded-xl bg-red-50 px-3 py-2 text-xs text-red-700">{error}</div>
        )}

        {!loading && lookup?.found && lookup.term && (
          <TermPanel
            heading="In your word bank catalog"
            word={lookup.term.term}
            ipa={lookup.term.ipaPronunciation}
            definition={lookup.term.definition}
            example={cleanExampleSentence(lookup.term.term, lookup.term.exampleSentence) || null}
            canPlayAudio
            onPlayAudio={() => void playAudio(lookup.term!.id)}
            cta={added ? (
              <span className="inline-flex items-center gap-1 text-xs font-medium text-green-600">
                <CheckCircle2 className="h-3.5 w-3.5" /> Saved
              </span>
            ) : (
              <button
                onClick={() => handleAdd(lookup.term!.id)}
                disabled={adding}
                className="inline-flex items-center gap-1 rounded-full bg-primary px-3 py-1 text-xs font-medium text-white hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
              >
                <Plus className="h-3 w-3" /> Add to my list
              </button>
            )}
          />
        )}

        {!loading && lookup && !lookup.found && (
          <div className="space-y-2">
            <div className="rounded-xl bg-background-light px-3 py-2 text-xs text-muted">
              Not found in the catalog.
              {lookup.suggestions.length > 0 && (
                <>
                  <div className="mt-2 font-semibold text-navy">Did you mean?</div>
                  <ul className="mt-1 space-y-1">
                    {lookup.suggestions.slice(0, 3).map(s => (
                      <li key={s.id}>
                        <button
                          onClick={() => handleAdd(s.id)}
                          className="text-xs font-medium text-primary hover:underline"
                        >
                          {s.term}
                        </button>
                        {': '}
                        <span className="text-muted">{s.definition.slice(0, 60)}{s.definition.length > 60 ? '…' : ''}</span>
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </div>
            <div className="flex items-center justify-between gap-2 rounded-xl bg-primary/5 px-3 py-2">
              <span className="text-xs text-primary">Need a definition? The AI can suggest one.</span>
              <AiHelpTooltip variant="vocabulary" />
            </div>
          </div>
        )}
        {audioUpgradeModal}
      </motion.div>
    </AnimatePresence>
  );
}

function TermPanel({
  heading,
  word,
  ipa,
  definition,
  example,
  canPlayAudio = false,
  onPlayAudio,
  cta,
  footer,
}: {
  heading: string;
  word: string;
  ipa: string | null;
  definition: string | null;
  example: string | null;
  canPlayAudio?: boolean;
  onPlayAudio?: () => void;
  cta?: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div className="rounded-xl border border-border bg-background-light p-3">
      <div className="mb-1 flex items-center justify-between gap-2">
        <div className="text-xs font-semibold uppercase text-muted">{heading}</div>
        {cta}
      </div>
      <div className="flex items-center gap-2">
        <span className="text-sm font-bold text-navy">{word}</span>
        {ipa && <span className="text-xs italic text-muted">{ipa}</span>}
        {canPlayAudio && onPlayAudio && (
          <button onClick={onPlayAudio} className="rounded-full p-1 text-muted hover:bg-surface hover:text-primary" aria-label="Play pronunciation">
            <Volume2 className="h-3 w-3" />
          </button>
        )}
      </div>
      <div className="mt-1 text-xs text-navy">{definition}</div>
      {example && <div className="mt-1 text-xs italic text-muted">&quot;{example}&quot;</div>}
      {footer}
    </div>
  );
}
