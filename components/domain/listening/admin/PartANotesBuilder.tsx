'use client';

import { useCallback, useId, useRef, useState, type ClipboardEvent } from 'react';
import { Eye, Heading, LayoutTemplate, List, ListTree, Minus, SquareSplitHorizontal } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';
/**
 * Part A notes WYSIWYG builder — WORK-STREAM 6.
 *
 * Replaces the plain stem textarea for OET Listening Part A (A1/A2) short-answer
 * questions with a structured note-completion editor. The author writes the
 * clinical-note body in a controlled textarea and drops gap markers via the
 * toolbar; a live preview renders the SAME `PartARenderer` the learner sees so
 * the gap layout is WYSIWYG.
 *
 * Round-trips through the existing `Stem` field — no new wire shape. The only
 * canonical gap marker emitted is `____` (4 underscores), which the renderer's
 *   BLANK_PATTERN = /(____+|\[\s*\]|\{\{\s*blank\s*\}\})/i
 * splits on. Legacy `[ ]` / `{{ blank }}` markers an author may already have in
 * a stem still render — we just never author new ones.
 */

// Import the canonical gap marker, paste sanitizer, and gap detector from the
// grammar lib. Re-exported so existing imports from this path keep working.
import { countGaps, detectPastedGaps, PART_A_GAP_MARKER, sanitizePastedStem } from '@/lib/listening-part-a-notes';
export { PART_A_GAP_MARKER, sanitizePastedStem };

/**
 * Canonical OET Listening Part A note-completion skeleton, inserted by the
 * "Scaffold" toolbar button. It demonstrates every construct the grammar
 * understands — the boilerplate intro line, the "thirty seconds" reading-time
 * line, the `Patient:` label line, two `## ` section headings, level-1 bullets,
 * a level-2 sub-bullet, a mid-sentence gap, and a gap with trailing text — so a
 * data-entry author starts from the right shape and only replaces the
 * placeholders. The `[...]` placeholders are literal text (the notes grammar
 * only treats a run of 4+ underscores as a gap), so they render as obvious
 * "replace me" prompts and never become answer blanks.
 */
export const PART_A_SCAFFOLD = [
  'You hear a [professional, e.g. primary-care doctor] talking to a patient called [patient name]. For questions 1–12, complete the notes with a word or short phrase that you hear.',
  '',
  'You now have thirty seconds to look at the notes.',
  '',
  'Patient: [patient name]',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '- [note detail], ____ and [trailing detail]',
  '  - [sub-detail] ____',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '',
].join('\n');

export interface PartANotesBuilderProps {
  value: string;
  onChange: (value: string) => void;
  /** Preview chrome — mirrors the learner clinical-note header. */
  questionNumber?: number;
  partLabel?: string;
  /** Disables the textarea + toolbar (e.g. while saving). */
  disabled?: boolean;
  id?: string;
  /**
   * Optional custom preview renderer. When provided, renders
   * `renderPreview(value)` in place of the default `PartARenderer` preview.
   * When absent, keeps the original per-question `PartARenderer` behavior.
   */
  renderPreview?: (body: string) => React.ReactNode;
}

type Insertable =
  | { kind: 'inline'; text: string }
  | { kind: 'block'; text: string };


export function PartANotesBuilder({
  value,
  onChange,
  questionNumber = 1,
  partLabel = 'Part A',
  disabled = false,
  id,
  renderPreview,
}: PartANotesBuilderProps) {
  const reactId = useId();
  const editorId = id ?? `part-a-notes-${reactId}`;
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  // Live-preview answer state, keyed so multiple gaps each get an independent
  // field. Author-only — never persisted; resets are harmless.
  const [previewAnswers, setPreviewAnswers] = useState<Record<number, string>>({});

  /**
   * Insert content at the caret (or replace the selection). Inline insertions
   * (the gap marker) splice directly; block insertions (heading / bullet /
   * section break) ensure they start on their own line.
   */
  const insert = useCallback(
    (payload: Insertable) => {
      const el = textareaRef.current;
      const current = value;
      // Fall back to appending when the textarea isn't mounted/focused yet.
      const selStart = el ? el.selectionStart : current.length;
      const selEnd = el ? el.selectionEnd : current.length;

      const before = current.slice(0, selStart);
      const after = current.slice(selEnd);

      const snippet = payload.text;
      let leading = '';
      let trailing = '';

      if (payload.kind === 'block') {
        const needsLeadingBreak = before.length > 0 && !before.endsWith('\n');
        leading = needsLeadingBreak ? '\n' : '';
        const needsTrailingBreak = after.length > 0 && !after.startsWith('\n');
        trailing = needsTrailingBreak ? '\n' : '';
      }

      const next = `${before}${leading}${snippet}${trailing}${after}`;
      onChange(next);

      // Restore the caret just after the inserted snippet on the next frame
      // (after React re-renders the controlled value).
      const caret = before.length + leading.length + snippet.length;
      requestAnimationFrame(() => {
        const node = textareaRef.current;
        if (!node) return;
        node.focus();
        node.setSelectionRange(caret, caret);
      });
    },
    [value, onChange],
  );

  const insertGap = useCallback(() => {
    insert({ kind: 'inline', text: PART_A_GAP_MARKER });
  }, [insert]);

  const insertHeading = useCallback(() => {
    insert({ kind: 'block', text: '## Heading' });
  }, [insert]);

  const insertBullet = useCallback(() => {
    insert({ kind: 'block', text: '- ' });
  }, [insert]);

  const insertSubBullet = useCallback(() => {
    // Two-space indent + dash → grammar level-2 bullet (`  - `).
    insert({ kind: 'block', text: '  - ' });
  }, [insert]);

  const insertSectionBreak = useCallback(() => {
    insert({ kind: 'block', text: '\n---\n' });
  }, [insert]);

  const insertScaffold = useCallback(() => {
    // Empty editor → drop the whole skeleton in. Non-empty → splice it as a
    // block at the caret so existing work is never clobbered.
    if (value.trim().length === 0) {
      onChange(PART_A_SCAFFOLD);
      requestAnimationFrame(() => {
        const node = textareaRef.current;
        if (!node) return;
        node.focus();
        node.setSelectionRange(PART_A_SCAFFOLD.length, PART_A_SCAFFOLD.length);
      });
      return;
    }
    insert({ kind: 'block', text: PART_A_SCAFFOLD });
  }, [insert, value, onChange]);

  const handlePaste = useCallback(
    (event: ClipboardEvent<HTMLTextAreaElement>) => {
      const clipboard = event.clipboardData;
      if (!clipboard) return;
      // Prefer HTML so we can sanitize; fall back to plain text.
      // Use detectPastedGaps so that official OET "(n)____" markers are
      // automatically converted to the canonical ____ form on paste.
      const html = clipboard.getData('text/html');
      const text = clipboard.getData('text/plain');
      const raw = html || text;
      const { body: cleaned } = detectPastedGaps(raw);
      event.preventDefault();

      const el = event.currentTarget;
      const selStart = el.selectionStart;
      const selEnd = el.selectionEnd;
      const next = `${value.slice(0, selStart)}${cleaned}${value.slice(selEnd)}`;
      onChange(next);

      const caret = selStart + cleaned.length;
      requestAnimationFrame(() => {
        const node = textareaRef.current;
        if (!node) return;
        node.focus();
        node.setSelectionRange(caret, caret);
      });
    },
    [value, onChange],
  );

  const setPreviewAnswer = useCallback((slot: number, answer: string) => {
    setPreviewAnswers((current) => ({ ...current, [slot]: answer }));
  }, []);

  // Live gap count, announced politely so authors track blanks while typing.
  const gapCount = countGaps(value);

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-3">
        <label htmlFor={editorId} className="text-sm font-semibold tracking-tight text-navy">
          Stem (note-completion)
        </label>
        <span className="text-xs text-admin-fg-muted">
          {"Insert “Gap” to add an answer blank ("}
          <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-[0.7rem]">____</code>
          {")."}
        </span>
      </div>

      {/* Toolbar */}
      <div
        role="toolbar"
        aria-label="Note formatting"
        aria-controls={editorId}
        className="flex flex-wrap items-center gap-1.5 rounded-t-2xl border border-b-0 border-border bg-background-light px-2 py-2"
      >
        <Button
          type="button"
          variant="secondary"
          size="sm"
          onClick={insertGap}
          disabled={disabled}
          startIcon={<Minus className="h-4 w-4" />}
        >
          Insert gap
        </Button>
        <span className="mx-0.5 h-5 w-px bg-border" aria-hidden="true" />
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={insertHeading}
          disabled={disabled}
          startIcon={<Heading className="h-4 w-4" />}
        >
          Heading
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={insertBullet}
          disabled={disabled}
          startIcon={<List className="h-4 w-4" />}
        >
          Bullet
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={insertSubBullet}
          disabled={disabled}
          startIcon={<ListTree className="h-4 w-4" />}
        >
          Sub-bullet
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={insertSectionBreak}
          disabled={disabled}
          startIcon={<SquareSplitHorizontal className="h-4 w-4" />}
        >
          Section break
        </Button>

        <span className="mx-0.5 ml-auto h-5 w-px bg-border" aria-hidden="true" />
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={insertScaffold}
          disabled={disabled}
          startIcon={<LayoutTemplate className="h-4 w-4" />}
        >
          Scaffold
        </Button>
        <span
          aria-live="polite"
          data-testid="part-a-gap-count"
          className="rounded-full bg-admin-bg-subtle px-2.5 py-1 text-xs font-semibold tabular-nums text-admin-fg-muted"
        >
          {gapCount} gap{gapCount !== 1 ? 's' : ''}
        </span>
      </div>

      <textarea
        ref={textareaRef}
        id={editorId}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onPaste={handlePaste}
        disabled={disabled}
        rows={6}
        spellCheck={false}
        placeholder={'Patient reports pain located in the ____\nOnset: ____ before admission'}
        className="min-h-[120px] resize-y rounded-b-2xl border border-border bg-background-light px-4 py-3 font-mono text-sm leading-7 text-navy shadow-sm transition-[border-color,box-shadow,color,background-color] duration-200 hover:border-border-hover focus:border-primary focus:bg-surface focus:outline-none focus:ring-4 focus:ring-primary/15 disabled:cursor-not-allowed disabled:opacity-60"
        aria-describedby={`${editorId}-hint`}
      />
      <p id={`${editorId}-hint`} className="text-xs leading-5 text-muted">
        Each <code className="font-mono">____</code> becomes one answer blank in the exam. Headings, bullets and section
        breaks are layout-only.
      </p>

      {/* Live preview — renders the real learner component, read-only. */}
      <div className="mt-3">
        <div className="mb-2 flex items-center gap-1.5 text-xs font-black uppercase tracking-widest text-admin-fg-muted">
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
          Live preview
        </div>
        <div
          data-testid="part-a-notes-preview"
          // Author preview only — block all interaction so the embedded answer
          // inputs can't be typed into or focused during authoring.
          className="pointer-events-none select-none rounded-2xl border border-dashed border-border bg-background p-2"
        >
          {renderPreview ? (
            renderPreview(value)
          ) : (
            <PartARenderer
              questionNumber={questionNumber}
              partLabel={partLabel}
              prompt={value}
              value={previewAnswers[questionNumber] ?? ''}
              onChange={(answer) => setPreviewAnswer(questionNumber, answer)}
              locked
              highlightingEnabled={false}
            />
          )}
        </div>
      </div>
    </div>
  );
}
