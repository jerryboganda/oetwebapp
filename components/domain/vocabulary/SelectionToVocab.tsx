'use client';

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { VocabLookupPopover, type VocabLookupSource } from './VocabLookupPopover';

/**
 * SelectionToVocab — wraps any content area and surfaces the
 * VocabLookupPopover whenever the user selects a word (or short phrase)
 * inside it.
 *
 * - Debounced to 150ms to avoid flicker.
 * - Ignores selections > 60 chars (likely a full sentence, not a term).
 * - Builds a context snippet (±120 chars around the selection).
 * - Generates a sourceRef from `source` + optional `sourceRefPrefix` +
 *   `start` offset.
 */
export interface SelectionToVocabProps {
  source: VocabLookupSource;
  /** Prefix like "reading:cp-042". Combined with offset → "reading:cp-042:134". */
  sourceRefPrefix?: string;
  children: ReactNode;
  className?: string;
}

export function SelectionToVocab({ source, sourceRefPrefix, children, className }: SelectionToVocabProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [popover, setPopover] = useState<{
    word: string;
    context: string;
    sourceRef: string;
    rect: DOMRect | null;
  } | null>(null);

  const handleSelection = useCallback(() => {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed) return;
    const range = sel.getRangeAt(0);
    if (!containerRef.current || !containerRef.current.contains(range.commonAncestorContainer)) return;

    const text = sel.toString().trim();
    if (!text || text.length > 60 || text.length < 2) return;

    // Build ±120-char context from the anchor node's text.
    const anchorText = range.commonAncestorContainer?.textContent ?? '';
    const startIdx = Math.max(0, range.startOffset - 120);
    const endIdx = Math.min(anchorText.length, range.endOffset + 120);
    const context = anchorText.slice(startIdx, endIdx);

    const rect = range.getBoundingClientRect();
    const sourceRef = sourceRefPrefix
      ? `${sourceRefPrefix}:${range.startOffset}`
      : `${source}:selection`;

    setPopover({ word: text, context, sourceRef, rect });
  }, [source, sourceRefPrefix]);

  useEffect(() => {
    function onMouseUp() {
      setTimeout(handleSelection, 150);
    }
    const el = containerRef.current;
    if (!el) return;
    el.addEventListener('mouseup', onMouseUp);
    el.addEventListener('touchend', onMouseUp);
    return () => {
      el.removeEventListener('mouseup', onMouseUp);
      el.removeEventListener('touchend', onMouseUp);
    };
  }, [handleSelection]);

  return (
    <div ref={containerRef} className={className}>
      {children}
      {popover && (
        <VocabLookupPopover
          word={popover.word}
          context={popover.context}
          source={source}
          sourceRef={popover.sourceRef}
          anchorRect={popover.rect}
          onClose={() => setPopover(null)}
        />
      )}
    </div>
  );
}
