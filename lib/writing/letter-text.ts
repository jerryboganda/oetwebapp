import type { JSONContent } from '@tiptap/core';

/** Whitespace-separated word count — the one the editor reports. */
export function countLetterWords(text: string): number {
  const trimmed = text.trim();
  return trimmed ? trimmed.split(/\s+/u).length : 0;
}

/**
 * Plain letter text → Tiptap document, the exact inverse of the Writing
 * editor's `editor.getText()`: paragraphs are joined by "\n\n" and a hard break
 * renders as "\n". Every character keeps ProseMirror position = text offset + 1
 * (a paragraph boundary is 2 positions for the 2-character separator, a hard
 * break is 1 position for "\n"), so character-offset annotations stay anchored.
 *
 * A JSON document is required: a string `content` is parsed as HTML, which
 * collapses the newlines and treats `<` / `&` in the letter as markup.
 */
export function letterTextToDoc(text: string): JSONContent {
  return {
    type: 'doc',
    content: text.split('\n\n').map((paragraph) => {
      const inline: JSONContent[] = [];
      paragraph.split('\n').forEach((line, index) => {
        if (index > 0) inline.push({ type: 'hardBreak' });
        // ProseMirror rejects empty text nodes.
        if (line) inline.push({ type: 'text', text: line });
      });
      return inline.length > 0 ? { type: 'paragraph', content: inline } : { type: 'paragraph' };
    }),
  };
}
