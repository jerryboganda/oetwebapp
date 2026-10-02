import { afterEach, describe, expect, it } from 'vitest';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { letterTextToDoc } from '../letter-text';

const editors: Editor[] = [];

function editorFor(text: string): Editor {
  const editor = new Editor({ extensions: [StarterKit], content: letterTextToDoc(text) });
  editors.push(editor);
  return editor;
}

afterEach(() => {
  while (editors.length > 0) editors.pop()?.destroy();
});

const LETTERS = [
  '',
  'Dear Dr Green,',
  'Dear Dr Green,\n\nI am writing to refer Mr Adam Lee.',
  'Re: Mr Adam Lee\nDOB: 20 Sep 1970\n\nDear Dr Green,',
  'one\n\n\nthree newlines',
  'one\n\n\n\nfour newlines (an empty paragraph)',
  '\n\nleading paragraph break',
  'trailing line break\n',
  'trailing paragraph break\n\n',
  '\n',
  'BP <140/90 mmHg & <b>not markup</b> — 37.8 °C',
  '- Paracetamol 1 g\n1. Review\n# not a heading\n**not bold**',
];

describe('letterTextToDoc', () => {
  it.each(LETTERS)('is the exact inverse of the editor getText() for %j', (text) => {
    expect(editorFor(text).getText()).toBe(text);
  });

  it('builds paragraphs for "\\n\\n" and hard breaks for "\\n"', () => {
    expect(letterTextToDoc('Dear Dr Green,\n\nLine one\nLine two')).toEqual({
      type: 'doc',
      content: [
        { type: 'paragraph', content: [{ type: 'text', text: 'Dear Dr Green,' }] },
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Line one' },
            { type: 'hardBreak' },
            { type: 'text', text: 'Line two' },
          ],
        },
      ],
    });
  });

  it('never emits an empty text node', () => {
    expect(letterTextToDoc('\n\n\n')).toEqual({
      type: 'doc',
      content: [{ type: 'paragraph' }, { type: 'paragraph', content: [{ type: 'hardBreak' }] }],
    });
  });

  it('keeps ProseMirror position = text offset + 1 so annotations stay anchored', () => {
    const text = 'Dear Dr Green,\n\nMr Lee\nhas a cough.\n\n\nYours sincerely';
    const { doc } = editorFor(text).state;
    for (let offset = 0; offset < text.length; offset += 1) {
      const char = text[offset];
      if (char === '\n') continue;
      expect(doc.textBetween(offset + 1, offset + 2), `offset ${offset}`).toBe(char);
    }
  });
});
