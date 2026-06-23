/**
 * Part A note-completion grammar <-> TipTap document converters.
 *
 * The canonical persisted format is the `____`/`## `/`### `/`- ` notes grammar
 * defined in `lib/listening-part-a-notes.ts`. The full-screen WYSIWYG editor
 * (`components/domain/listening/admin/PartANotesEditor.tsx`) is a *projection*
 * of that grammar: on load we deserialize grammar -> a ProseMirror/TipTap JSON
 * document; on every change we serialize the document back to grammar and feed
 * it through the existing `value: string` / `onChange(value)` contract.
 *
 * These two functions are inverses by construction, so
 *   tiptapDocToGrammar(grammarToTiptapDoc(body)) === body
 * for every canonical body (verified in the unit tests). This identity is what
 * lets the editor replace the old textarea WITHOUT mutating the 5 production
 * papers already stored in the `____` grammar.
 *
 * Document shape (flat, line-oriented — deliberately NOT native nested lists, so
 * the 2-level grammar round-trips losslessly including blank lines):
 *   - heading:        { type:'heading', attrs:{ level: 2|3 }, content:[inline] }
 *   - bullet:         { type:'paragraph', attrs:{ partAList: 1|2 }, content:[inline] }
 *   - context line:   { type:'paragraph', attrs:{ partAList: 0 }, content:[inline] }
 *   - blank line:     { type:'paragraph', attrs:{ partAList: 0 } }   (no content)
 *   - divider (`---`): { type:'horizontalRule' }
 * Inline:
 *   - text:           { type:'text', text }
 *   - gap (`____`):    { type:'partAGap' }
 */
import type { JSONContent } from '@tiptap/core';
import { PART_A_GAP_MARKER } from '@/lib/listening-part-a-notes';

/** Same gap notation the grammar recognises: a run of 4+ underscores. */
const GAP_PATTERN = /(_{4,})/;

/** Split one line of raw text into TipTap inline nodes (text + gap atoms). */
function lineToInline(text: string): JSONContent[] {
  if (text.length === 0) return [];
  const parts = text.split(GAP_PATTERN).filter((p) => p.length > 0);
  return parts.map((part) =>
    GAP_PATTERN.test(part) ? { type: 'partAGap' } : { type: 'text', text: part },
  );
}

/** Serialize a node's inline children back to raw grammar text. */
function inlineToText(content: JSONContent[] | undefined): string {
  if (!content) return '';
  let out = '';
  for (const node of content) {
    if (node.type === 'partAGap') out += PART_A_GAP_MARKER;
    else if (node.type === 'text') out += node.text ?? '';
  }
  return out;
}

/** Deserialize a canonical notes body into a TipTap JSON document. */
export function grammarToTiptapDoc(body: string | null | undefined): JSONContent {
  const lines = (body ?? '').split('\n');
  const content: JSONContent[] = lines.map((line) => {
    // Divider — exactly `---` (the canonical form emitted by the toolbar).
    if (line === '---') return { type: 'horizontalRule' };

    // Headings — `## ` (level 2) and `### ` (level 3).
    if (line.startsWith('### ')) {
      return { type: 'heading', attrs: { level: 3 }, content: lineToInline(line.slice(4)) };
    }
    if (line.startsWith('## ')) {
      return { type: 'heading', attrs: { level: 2 }, content: lineToInline(line.slice(3)) };
    }

    // Sub-bullet (level 2) — two-space indent then `- ` (canonical form).
    if (line.startsWith('  - ')) {
      return { type: 'paragraph', attrs: { partAList: 2 }, content: lineToInline(line.slice(4)) };
    }

    // Bullet (level 1) — `- `.
    if (line.startsWith('- ')) {
      return { type: 'paragraph', attrs: { partAList: 1 }, content: lineToInline(line.slice(2)) };
    }

    // Blank line — empty paragraph (preserves paragraph breaks for round-trip).
    if (line.length === 0) return { type: 'paragraph', attrs: { partAList: 0 } };

    // Context line — any other text.
    return { type: 'paragraph', attrs: { partAList: 0 }, content: lineToInline(line) };
  });

  return { type: 'doc', content };
}

/** Serialize a TipTap JSON document back into a canonical notes body. */
export function tiptapDocToGrammar(doc: JSONContent | null | undefined): string {
  const blocks = doc?.content ?? [];
  const lines = blocks.map((block) => {
    if (block.type === 'horizontalRule') return '---';

    if (block.type === 'heading') {
      const level = block.attrs?.level === 3 ? 3 : 2;
      const prefix = level === 3 ? '### ' : '## ';
      return prefix + inlineToText(block.content);
    }

    // paragraph (context / bullet / blank)
    const level = block.attrs?.partAList;
    const text = inlineToText(block.content);
    if (level === 2) return '  - ' + text;
    if (level === 1) return '- ' + text;
    return text;
  });

  return lines.join('\n');
}
