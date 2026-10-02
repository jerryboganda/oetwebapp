import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import type { Editor } from '@tiptap/core';
import { WritingEditorV2 } from '../WritingEditorV2';

/**
 * Runs the REAL Tiptap editor in jsdom. jsdom has no layout engine, so the few
 * measuring APIs ProseMirror may touch are stubbed; nothing else is mocked.
 */
beforeAll(() => {
  const rect = { x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) };
  Range.prototype.getClientRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList;
  Range.prototype.getBoundingClientRect = () => rect as DOMRect;
  document.elementFromPoint = () => null;
});

/**
 * The live editor inside the writing-editor wrapper (Tiptap stores its instance
 * on the ProseMirror element "for tests"). Looked up afresh for every step:
 * @tiptap/react may re-create the instance after its first render, and a
 * re-created editor starts from the latest text, which this suite relies on.
 */
function liveEditor(): { dom: HTMLElement; editor: Editor } {
  const dom = screen.getByTestId('writing-editor').querySelector<HTMLElement>('.ProseMirror');
  const editor = (dom as unknown as { editor?: Editor } | null)?.editor;
  if (!dom || !editor || editor.isDestroyed) {
    throw new Error(`No live Tiptap editor inside writing-editor:\n${document.body.innerHTML.slice(0, 4000)}`);
  }
  return { dom, editor };
}

async function mountedEditor() {
  await waitFor(liveEditor, { timeout: 10_000 });
  // Let any post-mount re-creation settle before the test interacts.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 50));
  });
  return liveEditor();
}

/** Types like a key press does in ProseMirror: input rules get the first say, else the text is inserted. */
function typeInto(text: string) {
  act(() => {
    for (const char of text) {
      const { view } = liveEditor().editor;
      const { from, to } = view.state.selection;
      const insert = () => view.state.tr.insertText(char, from, to);
      const handled = view.someProp('handleTextInput', (handler) =>
        (handler as unknown as (...args: unknown[]) => boolean | void)(view, from, to, char, insert),
      );
      if (!handled) view.dispatch(insert());
    }
  });
}

describe('WritingEditorV2 (real Tiptap)', () => {
  it('restores a saved letter as real paragraphs and line breaks, without reporting a change', async () => {
    const letter = 'Dear Dr Green,\n\nMr Lee has had a cough for three weeks.\nHe remains stable.\n\nYours sincerely,';
    const onChange = vi.fn();
    render(<WritingEditorV2 mode="practice" initialContent={letter} onChange={onChange} inputId="practice-editor" />);

    const { dom, editor } = await mountedEditor();

    expect(dom.id).toBe('practice-editor');
    expect(dom.querySelectorAll('p')).toHaveLength(3);
    expect(dom.querySelector('p:nth-of-type(2) br')).not.toBeNull();
    expect(editor.getText()).toBe(letter);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('hands text typed into the pre-hydration textarea over to Tiptap', async () => {
    const onChange = vi.fn();
    render(<WritingEditorV2 mode="practice" onChange={onChange} />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Writing editor' }), {
      target: { value: 'Typed before the swap' },
    });
    expect(onChange).toHaveBeenLastCalledWith('Typed before the swap', 4);

    const { editor } = await mountedEditor();
    expect(editor.getText()).toBe('Typed before the swap');
  });

  it('keeps Markdown-style characters exactly as typed (no formatting rules)', async () => {
    const onChange = vi.fn();
    render(<WritingEditorV2 mode="practice" onChange={onChange} />);
    const { editor } = await mountedEditor();

    expect(editor.schema.nodes.heading).toBeUndefined();
    expect(editor.schema.nodes.bulletList).toBeUndefined();
    expect(editor.schema.marks.bold).toBeUndefined();

    for (const line of ['- Paracetamol 1 g', '1. Review in clinic', '# Plan', '> Noted', '**urgent**', '---']) {
      act(() => {
        liveEditor().editor.commands.clearContent();
      });
      typeInto(line);
      expect(liveEditor().editor.getText()).toBe(line);
      expect(onChange).toHaveBeenLastCalledWith(line, line.split(' ').length);
    }
  });

  it('reports learner edits and blur, and never calls onChange on mount or re-render', async () => {
    const first = vi.fn();
    const second = vi.fn();
    const onBlur = vi.fn();
    const { rerender } = render(
      <WritingEditorV2 mode="practice" initialContent="Dear Dr Green," onChange={first} onBlur={onBlur} />,
    );
    await mountedEditor();
    rerender(<WritingEditorV2 mode="practice" initialContent="Dear Dr Green," onChange={second} onBlur={onBlur} />);

    expect(first).not.toHaveBeenCalled();
    expect(second).not.toHaveBeenCalled();

    act(() => {
      const { editor } = liveEditor();
      editor.commands.setTextSelection(editor.state.doc.content.size - 1);
    });
    typeInto(' Hi');
    expect(second).toHaveBeenLastCalledWith('Dear Dr Green, Hi', 4);

    fireEvent.blur(liveEditor().dom);
    expect(onBlur).toHaveBeenCalledTimes(1);
  });
});
