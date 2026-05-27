/**
 * Tiptap (ProseMirror) annotation overlay extension.
 *
 * Renders inline decorations for canon-violation, coach-hint and feedback
 * annotations supplied by the parent editor. Decorations are mapped through
 * document changes so they stay anchored to the same character range as the
 * learner edits around them — the parent re-configures the extension with a
 * fresh set whenever the underlying server payload changes.
 *
 * Companion CSS in `app/globals.css` ("Writing V2 annotation overlay")
 * styles each annotation type with a coloured underline + faint background
 * tint. The `title` attribute drives native browser tooltips; screen-reader
 * users still get the textual summary rendered by `useAnnotationOverlay` in
 * `WritingEditorV2.tsx`, which is wrapped in `aria-live="polite"`.
 */
import { Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import { Decoration, DecorationSet } from '@tiptap/pm/view';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';

export type AnnotationDecorationType =
  | 'canon-violation'
  | 'coach-hint'
  | 'feedback'
  | 'info';

export interface AnnotationDecoration {
  /** Zero-based character offset (inclusive). */
  charStart: number;
  /** Zero-based character offset (exclusive). */
  charEnd: number;
  type: AnnotationDecorationType;
  note: string;
  ruleId?: string;
}

export interface AnnotationsExtensionOptions {
  annotations: AnnotationDecoration[];
}

export const AnnotationsKey = new PluginKey<DecorationSet>('writing-annotations');

export const AnnotationsExtension = Extension.create<AnnotationsExtensionOptions>({
  name: 'writing-annotations',

  addOptions() {
    return { annotations: [] };
  },

  addProseMirrorPlugins() {
    const extension = this;
    return [
      new Plugin<DecorationSet>({
        key: AnnotationsKey,
        state: {
          init(_, { doc }) {
            return buildDecorations(doc, extension.options.annotations);
          },
          // Map existing decorations through every transaction so they remain
          // anchored to the same logical range as the user types around them.
          // Avoids the cost of rebuilding the full DecorationSet on every
          // keystroke (the parent only re-runs the build via reconfigure()
          // when the upstream annotation list changes).
          apply(tr, oldSet) {
            return tr.docChanged ? oldSet.map(tr.mapping, tr.doc) : oldSet;
          },
        },
        props: {
          decorations(state) {
            return AnnotationsKey.getState(state);
          },
        },
      }),
    ];
  },
});

function buildDecorations(
  doc: ProseMirrorNode,
  annotations: AnnotationDecoration[],
): DecorationSet {
  if (!annotations || annotations.length === 0) {
    return DecorationSet.empty;
  }
  // ProseMirror positions are 1-based and account for the opening doc token,
  // whereas our annotations carry plain char offsets into the text content.
  // The +1 shift converts the start/end pair into PM positions. We also
  // clamp to the document size so a stale annotation range past EOF
  // (eg. learner deleted the trailing paragraph) does not throw.
  const docSize = doc.content.size;
  const decorations: Decoration[] = [];
  for (const a of annotations) {
    const fromRaw = Math.max(0, Math.floor(a.charStart));
    const toRaw = Math.max(fromRaw, Math.floor(a.charEnd));
    const from = Math.min(fromRaw + 1, docSize);
    const to = Math.min(toRaw + 1, docSize);
    if (from >= to) continue;
    decorations.push(
      Decoration.inline(from, to, {
        class: `writing-annotation writing-annotation--${a.type}`,
        'data-rule-id': a.ruleId ?? '',
        'data-note': a.note,
        title: a.note,
      }),
    );
  }
  return DecorationSet.create(doc, decorations);
}
