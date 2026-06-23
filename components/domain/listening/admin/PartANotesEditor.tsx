'use client';

/**
 * Part A note-completion — full-screen professional WYSIWYG editor (TipTap).
 *
 * Replaces the plain textarea for OET Listening Part A authoring. The editor is
 * a *projection* of the canonical `____`/`## `/`- ` notes grammar:
 *   - load:   content = grammarToTiptapDoc(value)
 *   - change: onChange(tiptapDocToGrammar(editor.getJSON()))
 * so the persisted format (and therefore the learner renderer + grader + the 5
 * production papers) is unchanged. See `lib/listening-part-a-notes-tiptap.ts`.
 *
 * The fill-in-the-blank is an inline ATOM node (`PartAGap`): the author drops it
 * with the "Insert blank" button, text flows before/after it on the same line,
 * and it can never be half-typed or accidentally created by typing "(1)". This
 * is the structural fix for the original "0 gaps" data-entry bug.
 *
 * This component is rendered ONLY in a real browser; `PartANotesBuilder` keeps a
 * textarea fallback for SSR + jsdom unit tests (ProseMirror's contenteditable is
 * not meaningfully testable under jsdom).
 */
import { useCallback, useEffect, useState } from 'react';
import { useEditor, EditorContent, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import {
  Heading2,
  Heading3,
  List,
  ListTree,
  Pilcrow,
  Minus,
  SeparatorHorizontal,
  LayoutTemplate,
  Undo2,
  Redo2,
  Maximize2,
  Minimize2,
} from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { cn } from '@/lib/utils';
import { PART_A_SCAFFOLD } from '@/lib/listening-part-a-notes';
import { grammarToTiptapDoc, tiptapDocToGrammar } from '@/lib/listening-part-a-notes-tiptap';
import { PartAGap, PartAListAttribute, PART_A_GAP_NODE_NAME } from './part-a-gap-node';

export interface PartANotesEditorProps {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  id?: string;
  /** Live gap count (computed by the parent from the same `value`). */
  gapCount?: number;
  ariaDescribedBy?: string;
}

const SURFACE_CLASS =
  'part-a-notes-editor-surface min-h-[180px] rounded-b-2xl border border-border bg-background-light px-4 py-3 text-sm leading-8 text-navy focus:outline-none';

export function PartANotesEditor({
  value,
  onChange,
  disabled = false,
  id,
  gapCount = 0,
  ariaDescribedBy,
}: PartANotesEditorProps) {
  const [fullscreen, setFullscreen] = useState(false);

  const editor = useEditor({
    immediatelyRender: false,
    editable: !disabled,
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        // The OET note grammar is a flat 2-level layout, not native nested
        // lists; bullets are paragraphs tagged via PartAListAttribute. Disable
        // the list/quote/code nodes so authors can't create structures the
        // grammar serializer would drop.
        bulletList: false,
        orderedList: false,
        listItem: false,
        blockquote: false,
        codeBlock: false,
      }),
      PartAListAttribute,
      PartAGap,
    ],
    content: grammarToTiptapDoc(value),
    editorProps: {
      attributes: {
        id: id ?? 'part-a-notes-editor',
        'aria-label': 'Note-completion editor',
        ...(ariaDescribedBy ? { 'aria-describedby': ariaDescribedBy } : {}),
        spellcheck: 'false',
        class: SURFACE_CLASS,
      },
    },
    onUpdate: ({ editor: ed }) => {
      onChange(tiptapDocToGrammar(ed.getJSON()));
    },
  });

  // Reflect external value changes (AI-OCR prefill, scaffold drop, programmatic
  // resets) into the document WITHOUT emitting an update (avoids a loop). We
  // compare against the editor's current serialization so user keystrokes —
  // which already round-trip through `value` — never trigger a redundant reset.
  useEffect(() => {
    if (!editor) return;
    const current = tiptapDocToGrammar(editor.getJSON());
    if (current === value) return;
    editor.commands.setContent(grammarToTiptapDoc(value), false);
  }, [editor, value]);

  useEffect(() => {
    editor?.setEditable(!disabled);
  }, [editor, disabled]);

  // Escape exits full-screen.
  useEffect(() => {
    if (!fullscreen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setFullscreen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [fullscreen]);

  const run = useCallback(
    (fn: (chain: ReturnType<Editor['chain']>) => ReturnType<Editor['chain']>) => {
      if (!editor) return;
      fn(editor.chain().focus()).run();
    },
    [editor],
  );

  const setBulletLevel = useCallback(
    (level: 0 | 1 | 2) => {
      if (!editor) return;
      editor.chain().focus().setParagraph().updateAttributes('paragraph', { partAList: level }).run();
    },
    [editor],
  );

  const onScaffold = useCallback(() => {
    if (!value.trim()) {
      onChange(PART_A_SCAFFOLD);
      return;
    }
    const blocks = grammarToTiptapDoc(`\n${PART_A_SCAFFOLD}`).content ?? [];
    editor?.chain().focus('end').insertContent(blocks).run();
  }, [editor, value, onChange]);

  if (!editor) {
    return (
      <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted" aria-busy="true">
        Loading editor…
      </div>
    );
  }

  const isActive = (name: string, attrs?: Record<string, unknown>) => editor.isActive(name, attrs);

  return (
    <div
      className={cn(
        'flex flex-col',
        fullscreen && 'fixed inset-0 z-[60] m-0 gap-1.5 overflow-auto bg-surface p-4',
      )}
      data-fullscreen={fullscreen ? 'true' : 'false'}
    >
      <div
        role="toolbar"
        aria-label="Note formatting"
        className="flex flex-wrap items-center gap-1.5 rounded-t-2xl border border-b-0 border-border bg-background-light px-2 py-2"
      >
        <Button
          type="button"
          variant="secondary"
          size="sm"
          onClick={() => run((c) => c.insertContent({ type: PART_A_GAP_NODE_NAME }))}
          disabled={disabled}
          startIcon={<Minus className="h-4 w-4" />}
        >
          Insert blank
        </Button>

        <span className="mx-0.5 h-5 w-px bg-border" aria-hidden="true" />

        <ToolbarToggle
          label="Heading"
          active={isActive('heading', { level: 2 })}
          disabled={disabled}
          icon={<Heading2 className="h-4 w-4" />}
          onClick={() => run((c) => c.toggleHeading({ level: 2 }))}
        />
        <ToolbarToggle
          label="Sub-heading"
          active={isActive('heading', { level: 3 })}
          disabled={disabled}
          icon={<Heading3 className="h-4 w-4" />}
          onClick={() => run((c) => c.toggleHeading({ level: 3 }))}
        />
        <ToolbarToggle
          label="Bullet"
          active={isActive('paragraph', { partAList: 1 })}
          disabled={disabled}
          icon={<List className="h-4 w-4" />}
          onClick={() => setBulletLevel(1)}
        />
        <ToolbarToggle
          label="Sub-bullet"
          active={isActive('paragraph', { partAList: 2 })}
          disabled={disabled}
          icon={<ListTree className="h-4 w-4" />}
          onClick={() => setBulletLevel(2)}
        />
        <ToolbarToggle
          label="Normal text"
          active={isActive('paragraph', { partAList: 0 })}
          disabled={disabled}
          icon={<Pilcrow className="h-4 w-4" />}
          onClick={() => setBulletLevel(0)}
        />
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => run((c) => c.setHorizontalRule())}
          disabled={disabled}
          startIcon={<SeparatorHorizontal className="h-4 w-4" />}
        >
          Divider
        </Button>

        <span className="mx-0.5 h-5 w-px bg-border" aria-hidden="true" />

        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => run((c) => c.undo())}
          disabled={disabled || !editor.can().undo()}
          startIcon={<Undo2 className="h-4 w-4" />}
          aria-label="Undo"
        >
          <span className="sr-only">Undo</span>
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => run((c) => c.redo())}
          disabled={disabled || !editor.can().redo()}
          startIcon={<Redo2 className="h-4 w-4" />}
          aria-label="Redo"
        >
          <span className="sr-only">Redo</span>
        </Button>

        <span className="mx-0.5 ml-auto h-5 w-px bg-border" aria-hidden="true" />

        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onScaffold}
          disabled={disabled}
          startIcon={<LayoutTemplate className="h-4 w-4" />}
        >
          Scaffold
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => setFullscreen((f) => !f)}
          startIcon={fullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
        >
          {fullscreen ? 'Exit full screen' : 'Full screen'}
        </Button>
        <span
          aria-live="polite"
          data-testid="part-a-gap-count"
          className="rounded-full bg-admin-bg-subtle px-2.5 py-1 text-xs font-semibold tabular-nums text-admin-fg-muted"
        >
          {gapCount} gap{gapCount !== 1 ? 's' : ''}
        </span>
      </div>

      <EditorContent editor={editor} className={cn(fullscreen && 'flex-1')} />

      <p className="mt-1 text-xs leading-5 text-muted">
        Click <strong>Insert blank</strong> to drop a fill-in-the-blank where the candidate types. Headings, bullets and
        dividers are layout-only and mirror the official OET note paper.
      </p>
    </div>
  );
}

function ToolbarToggle({
  label,
  active,
  disabled,
  icon,
  onClick,
}: {
  label: string;
  active: boolean;
  disabled?: boolean;
  icon: React.ReactNode;
  onClick: () => void;
}) {
  return (
    <Button
      type="button"
      variant={active ? 'secondary' : 'ghost'}
      size="sm"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      startIcon={icon}
    >
      {label}
    </Button>
  );
}
