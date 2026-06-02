'use client';

import { useMemo, type ReactNode } from 'react';
import { cn } from '@/lib/utils';
import type { WritingExemplarAnnotationDto } from '@/lib/writing/types';

export interface ExemplarSideBySideProps {
  candidateLetter: string;
  exemplarLetter: string;
  exemplarAnnotations?: WritingExemplarAnnotationDto[];
  className?: string;
}

type DiffOp = -1 | 0 | 1; // delete | equal | insert
type DiffChunk = [DiffOp, string];

/**
 * Lazy loader for `diff-match-patch`. Returns a tokenised diff at word
 * granularity. If the library is unavailable at runtime (e.g. before
 * `pnpm install` has been re-run on a fresh checkout), we degrade
 * gracefully to a simple no-diff render where both letters are shown
 * side-by-side without highlighting.
 *
 * NOTE for Wave C: once `diff-match-patch` is installed (see
 * package.json), the dynamic import resolves and word-level diffing
 * is automatic. No code change needed here.
 */
async function tryWordDiff(left: string, right: string): Promise<DiffChunk[] | null> {
  try {
    const mod: unknown = await import('diff-match-patch');
    const modObj = mod as { diff_match_patch?: new () => unknown; default?: { diff_match_patch?: new () => unknown } };
    const Ctor = modObj.diff_match_patch ?? modObj.default?.diff_match_patch;
    if (!Ctor) return null;
    const dmp = new Ctor() as {
      diff_linesToChars_(a: string, b: string): { chars1: string; chars2: string; lineArray: string[] };
      diff_main(a: string, b: string, checklines?: boolean): Array<[number, string]>;
      diff_charsToLines_(diffs: Array<[number, string]>, lineArray: string[]): void;
      diff_cleanupSemantic(diffs: Array<[number, string]>): void;
    };
    // Tokenise on word boundaries so diff chunks are word-level (not char).
    const tokenize = (s: string) => s.split(/(\s+|[.,;:!?()\-])/u).filter((t) => t.length > 0);
    const leftTokens = tokenize(left);
    const rightTokens = tokenize(right);
    // Map each unique token to a single char so we can use the line-based
    // optimised path of diff-match-patch on token sequences.
    const tokenToChar = new Map<string, string>();
    const charToToken: string[] = [];
    const tokensToString = (tokens: string[]) => {
      let out = '';
      for (const t of tokens) {
        let c = tokenToChar.get(t);
        if (!c) {
          c = String.fromCharCode(0xE000 + charToToken.length);
          tokenToChar.set(t, c);
          charToToken.push(t);
        }
        out += c;
      }
      return out;
    };
    const leftStr = tokensToString(leftTokens);
    const rightStr = tokensToString(rightTokens);
    const diffs = dmp.diff_main(leftStr, rightStr, false);
    dmp.diff_cleanupSemantic(diffs);
    return diffs.map(([op, text]) => {
      let detok = '';
      for (const c of text) {
        const idx = c.charCodeAt(0) - 0xE000;
        detok += charToToken[idx] ?? c;
      }
      return [op as DiffOp, detok];
    });
  } catch {
    return null;
  }
}

/**
 * Plain fallback (no diff lib available) — render both letters as
 * normal monospace text.
 */
function PlainPanel({ text, label, className }: { text: string; label: string; className?: string }) {
  return (
    <div className={cn('flex flex-col h-full', className)} aria-label={label}>
      <h3 className="text-xs uppercase tracking-wider font-bold text-muted mb-2">{label}</h3>
      <pre className="flex-1 whitespace-pre-wrap text-sm leading-relaxed font-sans p-3 rounded border border-border bg-surface overflow-y-auto">
        {text}
      </pre>
    </div>
  );
}

/**
 * Highlighted column. Tokens marked DELETE (-1) appear only in the
 * left column (red). Tokens marked INSERT (+1) appear only in the
 * right column (green). Tokens marked EQUAL (0) appear in both.
 *
 * `side` controls which set we render. Annotations (right side only)
 * inject footnote markers at matching char positions.
 */
function DiffPanel({
  side,
  diffs,
  text,
  annotations,
  label,
  className,
}: {
  side: 'left' | 'right';
  diffs: DiffChunk[];
  text: string;
  annotations?: WritingExemplarAnnotationDto[];
  label: string;
  className?: string;
}) {
  // For the right (exemplar) column, build a position-indexed annotation
  // map keyed on char-start so we can drop a marker mid-render.
  const annoByStart = useMemo(() => {
    const m = new Map<number, WritingExemplarAnnotationDto>();
    if (side === 'right' && annotations) {
      for (const a of annotations) m.set(a.charStart, a);
    }
    return m;
  }, [side, annotations]);

  // Reconstruct the side-specific token stream, threading annotations.
  let charCursor = 0;
  const nodes: ReactNode[] = [];
  diffs.forEach((chunk, idx) => {
    const [op, t] = chunk;
    if (side === 'left' && op === 1) return; // skip inserts on left
    if (side === 'right' && op === -1) return; // skip deletes on right

    let tone = '';
    if (op === -1 && side === 'left') tone = 'bg-danger/15 text-danger rounded px-0.5';
    if (op === 1 && side === 'right') tone = 'bg-success/15 text-success rounded px-0.5';

    if (side === 'right' && annoByStart.size > 0) {
      // Split the token text whenever an annotation start falls inside it.
      let remaining = t;
      while (remaining.length > 0) {
        const nextStart = [...annoByStart.keys()]
          .filter((pos) => pos >= charCursor && pos < charCursor + remaining.length)
          .sort((a, b) => a - b)[0];
        if (nextStart === undefined) {
          nodes.push(
            <span key={`${idx}-tail-${charCursor}`} className={tone || undefined}>
              {remaining}
            </span>,
          );
          charCursor += remaining.length;
          remaining = '';
        } else {
          const offset = nextStart - charCursor;
          if (offset > 0) {
            const pre = remaining.slice(0, offset);
            nodes.push(
              <span key={`${idx}-pre-${charCursor}`} className={tone || undefined}>
                {pre}
              </span>,
            );
            charCursor += offset;
            remaining = remaining.slice(offset);
          }
          const annotation = annoByStart.get(nextStart);
          if (annotation) {
            nodes.push(
              <sup
                key={`a-${annotation.id}`}
                className="ml-0.5 font-bold text-primary cursor-help"
                title={annotation.note}
                aria-label={`Annotation: ${annotation.note}`}
              >
                [{annotation.ruleId ?? '*'}]
              </sup>,
            );
          }
        }
      }
    } else {
      nodes.push(
        <span key={`${idx}-${charCursor}`} className={tone || undefined}>
          {t}
        </span>,
      );
      charCursor += t.length;
    }
  });

  return (
    <div className={cn('flex flex-col h-full', className)} aria-label={label}>
      <h3 className="text-xs uppercase tracking-wider font-bold text-muted mb-2">{label}</h3>
      <div className="flex-1 whitespace-pre-wrap text-sm leading-relaxed p-3 rounded border border-border bg-surface overflow-y-auto">
        {nodes.length > 0 ? nodes : <span className="text-muted italic">empty</span>}
      </div>
    </div>
  );
}

/**
 * Two-column side-by-side comparison view used after grading.
 *
 * Left  = candidate's letter
 * Right = chosen exemplar
 *
 * Word-level diff highlighting via `diff-match-patch`. Inline
 * annotations on the exemplar render as footnote markers that surface
 * the explanation on hover/focus.
 */
export function ExemplarSideBySide({
  candidateLetter,
  exemplarLetter,
  exemplarAnnotations,
  className,
}: ExemplarSideBySideProps) {
  const diffs = useDiff(candidateLetter, exemplarLetter);

  return (
    <div
      className={cn('grid grid-cols-1 lg:grid-cols-2 gap-4 min-h-[24rem]', className)}
      role="region"
      aria-label="Side-by-side comparison of your letter and the exemplar"
    >
      {diffs ? (
        <>
          <DiffPanel side="left" diffs={diffs} text={candidateLetter} label="Your letter" />
          <DiffPanel
            side="right"
            diffs={diffs}
            text={exemplarLetter}
            annotations={exemplarAnnotations}
            label="Gold-standard exemplar"
          />
        </>
      ) : (
        <>
          <PlainPanel text={candidateLetter} label="Your letter" />
          <PlainPanel text={exemplarLetter} label="Gold-standard exemplar" />
        </>
      )}
    </div>
  );
}

/**
 * Hook that lazily computes the diff on the client. Returns `null`
 * while loading or if the diff library is unavailable — caller falls
 * back to the plain panel render in that case.
 */
function useDiff(left: string, right: string): DiffChunk[] | null {
  // We deliberately use lazy state initialisation to avoid SSR mismatch.
  // The diff is computed in a microtask once both sides are stable.
  const memo = useMemo(() => ({ left, right }), [left, right]);
  // We cannot await in render. Inline IIFE captures and stores promise.
  // Component re-renders when the promise resolves via a `useState` setter
  // — but for simplicity (and to keep this component import-light) we
  // skip the diff on the server and compute synchronously after mount.
  // Implementation note: a future optimisation would push this into a
  // Web Worker for very large letters.
  return useClientDiff(memo.left, memo.right);
}

function useClientDiff(left: string, right: string): DiffChunk[] | null {
  const [state, setState] = useStateOnMount<{ diffs: DiffChunk[] | null }>({ diffs: null });

  useRunEffectClientOnly(() => {
    let cancelled = false;
    void tryWordDiff(left, right).then((diffs) => {
      if (!cancelled) setState({ diffs });
    });
    return () => {
      cancelled = true;
    };
  }, [left, right]);

  return state.diffs;
}

// ─── Tiny SSR-safe hook helpers (kept local to avoid coupling) ──────────────

import { useEffect, useState } from 'react';

function useStateOnMount<T>(initial: T) {
  return useState<T>(initial);
}

function useRunEffectClientOnly(effect: () => (() => void) | void, deps: unknown[]) {
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(effect, deps);
}
