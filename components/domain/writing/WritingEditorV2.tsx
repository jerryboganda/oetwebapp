'use client';

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { cn } from '@/lib/utils';
import type { WritingEditorMode } from '@/lib/writing/types';
import type {
  AnnotationDecoration,
  AnnotationDecorationType,
} from './tiptap-annotations';

export interface WritingEditorAnnotation {
  charStart: number;
  charEnd: number;
  type: 'canon' | 'feedback' | 'coach' | 'info';
  note: string;
  ruleId?: string;
}

/** Map the parent-facing `type` to the CSS-class-bearing decoration type. */
function toDecorationType(t: WritingEditorAnnotation['type']): AnnotationDecorationType {
  switch (t) {
    case 'canon':
      return 'canon-violation';
    case 'coach':
      return 'coach-hint';
    case 'feedback':
      return 'feedback';
    case 'info':
    default:
      return 'info';
  }
}

function toDecorations(annotations: WritingEditorAnnotation[]): AnnotationDecoration[] {
  return annotations.map((a) => ({
    charStart: a.charStart,
    charEnd: a.charEnd,
    type: toDecorationType(a.type),
    note: a.note,
    ruleId: a.ruleId,
  }));
}

export interface WritingEditorV2Props {
  initialContent?: string;
  mode: WritingEditorMode;
  onChange?: (content: string, wordCount: number) => void;
  onSubmit?: (content: string) => void;
  spellCheck?: boolean;
  annotations?: WritingEditorAnnotation[];
  disabled?: boolean;
  placeholder?: string;
  /**
   * Optional ID for the textarea/contenteditable surface — useful for
   * label-by associations from the parent.
   */
  inputId?: string;
  className?: string;
  /**
   * Render slot for the right rail (e.g. <CoachPanel />). The editor
   * lays itself out single-column; the parent wraps it in a grid if a
   * side panel is needed.
   */
  children?: ReactNode;
}

function countWords(s: string): number {
  if (!s) return 0;
  const trimmed = s.trim();
  if (trimmed.length === 0) return 0;
  return trimmed.split(/\s+/u).length;
}

interface TiptapModule {
  useEditor: (config: unknown) => unknown;
  EditorContent: React.ComponentType<{ editor: unknown; className?: string; spellCheck?: boolean }>;
}

interface StarterKitModule {
  default: { configure: (opts: Record<string, unknown>) => unknown };
}

interface AnnotationsModule {
  AnnotationsExtension: { configure: (opts: { annotations: AnnotationDecoration[] }) => unknown };
}

/**
 * Tiptap-backed writing editor (V2).
 *
 * Strategy:
 *   - On the server (SSR) we render a `<textarea>` fallback so the page
 *     stays interactive even before hydration. Once the client mounts,
 *     we lazy-import `@tiptap/react` + `@tiptap/starter-kit` and swap
 *     to the rich editor.
 *   - If Tiptap is not yet installed (fresh checkout pre-`npm install`)
 *     we keep the textarea fallback indefinitely so the editor still
 *     functions. The annotation overlay is rendered above either surface.
 *   - All changes go through `onChange(content, wordCount)`. The
 *     component is uncontrolled internally; do NOT push state changes
 *     back through `initialContent` after mount or you will fight the
 *     editor.
 *
 * Diagnostic mode: when `mode === 'diagnostic'` and `disabled === true`
 * (parent signals reading-window lock), the surface is read-only.
 */
export function WritingEditorV2({
  initialContent = '',
  mode,
  onChange,
  onSubmit,
  spellCheck,
  annotations = [],
  disabled = false,
  placeholder = 'Begin writing your response…',
  inputId,
  className,
  children,
}: WritingEditorV2Props) {
  const [tiptap, setTiptap] = useState<{
    react: TiptapModule;
    starterKit: StarterKitModule['default'];
    annotations: AnnotationsModule['AnnotationsExtension'];
  } | null>(null);
  const [fallbackValue, setFallbackValue] = useState(initialContent);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  // Spell-check rules: ON in practice modes, OFF in mock per spec §11.4.
  // Caller can override via `spellCheck` prop.
  const effectiveSpellCheck =
    typeof spellCheck === 'boolean' ? spellCheck : mode !== 'mock';

  // Dynamic Tiptap import (client-only). Cast to `string` so TypeScript
  // doesn't try to resolve the module at compile time — `@tiptap/react`
  // and `@tiptap/starter-kit` are listed in package.json and will resolve
  // once `npm install` runs.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        // The annotations extension is a regular module in this repo (no
        // `webpackIgnore`) so the bundler resolves it normally. The tiptap
        // peer modules are dynamically imported with `webpackIgnore` so the
        // editor remains optional — if `@tiptap/*` is not installed we stay
        // on the textarea fallback.
        const [react, starterKitImport, annotationsImport] = await Promise.all([
          import(/* webpackIgnore: true */ '@tiptap/react' as string) as Promise<TiptapModule>,
          import(/* webpackIgnore: true */ '@tiptap/starter-kit' as string) as Promise<StarterKitModule>,
          import('./tiptap-annotations') as Promise<AnnotationsModule>,
        ]);
        if (cancelled) return;
        setTiptap({
          react,
          starterKit: starterKitImport.default,
          annotations: annotationsImport.AnnotationsExtension,
        });
      } catch {
        // Tiptap not available — stay on the textarea fallback.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Fallback textarea — fires onChange immediately.
  useEffect(() => {
    if (tiptap) return;
    onChange?.(fallbackValue, countWords(fallbackValue));
  }, [fallbackValue, tiptap, onChange]);

  // Submit via Ctrl/Cmd+Enter
  useEffect(() => {
    if (disabled || !onSubmit) return;
    function handleKey(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        const current = tiptap ? fallbackValue /* TODO bridge once editor instance is exposed */ : fallbackValue;
        onSubmit?.(current);
      }
    }
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [disabled, onSubmit, tiptap, fallbackValue]);

  const annotationOverlay = useAnnotationOverlay(annotations);

  return (
    <div
      className={cn(
        'relative flex flex-col h-full min-h-[24rem] rounded-2xl border border-border bg-surface shadow-sm overflow-hidden',
        'focus-within:ring-2 focus-within:ring-[#156082]/30 focus-within:ring-offset-1',
        className,
      )}
    >
      <div className="flex items-center justify-between border-b border-border px-3 py-2">
        <span className="text-[10px] uppercase tracking-wider font-bold text-muted">
          {mode}
          {disabled ? ' · read-only' : ''}
        </span>
        <span className="text-[10px] uppercase tracking-wider font-bold text-muted">
          {effectiveSpellCheck ? 'Spell-check on' : 'Spell-check off'}
        </span>
      </div>

      <div className="flex-1 relative">
        {tiptap ? (
          <TiptapEditor
            tiptap={tiptap}
            initialContent={initialContent}
            disabled={disabled}
            spellCheck={effectiveSpellCheck}
            placeholder={placeholder}
            inputId={inputId}
            annotations={annotations}
            onChange={(content, wordCount) => {
              setFallbackValue(content);
              onChange?.(content, wordCount);
            }}
          />
        ) : (
          <textarea
            ref={textareaRef}
            id={inputId}
            className={cn(
              'w-full h-full min-h-[24rem] p-4 text-sm leading-relaxed font-sans resize-none',
              'bg-transparent text-navy dark:text-white',
              'focus:outline-none placeholder:text-muted',
              disabled && 'opacity-60 cursor-not-allowed',
            )}
            value={fallbackValue}
            placeholder={placeholder}
            spellCheck={effectiveSpellCheck}
            disabled={disabled}
            readOnly={disabled}
            onChange={(e) => setFallbackValue(e.target.value)}
            aria-label="Writing editor"
          />
        )}
        {annotationOverlay}
      </div>

      {children ? <div className="border-t border-border">{children}</div> : null}
    </div>
  );
}

/**
 * Tiptap shell. Hosted in a nested component so the outer file does
 * not need to type-import @tiptap/react (which only exists once the
 * dep is installed). All Tiptap props are passed via the dynamic
 * module reference.
 */
function TiptapEditor({
  tiptap,
  initialContent,
  disabled,
  spellCheck,
  placeholder,
  inputId,
  annotations,
  onChange,
}: {
  tiptap: {
    react: TiptapModule;
    starterKit: StarterKitModule['default'];
    annotations: AnnotationsModule['AnnotationsExtension'];
  };
  initialContent: string;
  disabled: boolean;
  spellCheck: boolean;
  placeholder: string;
  inputId?: string;
  annotations: WritingEditorAnnotation[];
  onChange: (content: string, wordCount: number) => void;
}) {
  const { useEditor, EditorContent } = tiptap.react;
  const StarterKit = tiptap.starterKit;
  const AnnotationsExtension = tiptap.annotations;

  // Decorations are recomputed every render — the parent only re-creates the
  // array when the server payload changes, so this is cheap in practice. The
  // ProseMirror plugin maps decorations through doc changes between renders.
  const decorations = useMemo(() => toDecorations(annotations), [annotations]);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ heading: { levels: [2, 3] } }),
      AnnotationsExtension.configure({ annotations: decorations }),
    ],
    content: initialContent,
    editable: !disabled,
    editorProps: {
      attributes: {
        id: inputId ?? 'writing-editor-v2',
        'aria-label': 'Writing editor',
        class: 'prose prose-sm dark:prose-invert max-w-none focus:outline-none px-4 py-4 min-h-[20rem]',
      },
    },
    onUpdate: ({ editor: editorInstance }: { editor: { getText(): string } }) => {
      const text = editorInstance.getText();
      onChange(text, countWords(text));
    },
  }) as {
    getText(): string;
    setEditable(v: boolean): void;
    setOptions(opts: { extensions?: unknown[] }): void;
    view?: { dispatch?: (tr: unknown) => void };
  } | null;

  // Sync editable state when `disabled` flips.
  useEffect(() => {
    if (!editor) return;
    editor.setEditable(!disabled);
  }, [editor, disabled]);

  // Reconfigure the annotation plugin when the upstream array changes. Tiptap
  // does not expose a first-class "update plugin options" API, so we lean on
  // `setOptions({ extensions })` to swap the configured extension. The
  // ProseMirror plugin state is re-initialised at that point with the new
  // decoration list. This re-runs the plugin's `init` over the *current*
  // document, which is exactly what we want — stale decorations from the
  // previous payload are discarded.
  useEffect(() => {
    if (!editor) return;
    try {
      editor.setOptions({
        extensions: [
          StarterKit.configure({ heading: { levels: [2, 3] } }),
          AnnotationsExtension.configure({ annotations: decorations }),
        ],
      });
    } catch {
      // setOptions is supported in modern Tiptap; ignore failures so the
      // editor remains functional even if the API surface shifts.
    }
  }, [editor, decorations, StarterKit, AnnotationsExtension]);

  if (!editor) {
    return (
      <div className="p-4 text-sm text-muted" aria-busy="true">
        Loading editor…
      </div>
    );
  }

  return (
    <EditorContent
      editor={editor}
      spellCheck={spellCheck}
      className={cn(disabled && 'opacity-60 cursor-not-allowed')}
    />
  );
}

/**
 * Annotation overlay — accessible summary list.
 *
 * The full inline highlighting happens via the ProseMirror decoration
 * plugin (`AnnotationsExtension` in `tiptap-annotations.ts`). This list
 * remains as the screen-reader fallback so AT users get the same data
 * the decorations expose visually. Wrapped in `aria-live="polite"` so
 * new feedback chunks (eg. after coach suggestions stream in) are
 * announced without interrupting the learner.
 */
function useAnnotationOverlay(annotations: WritingEditorAnnotation[]): ReactNode {
  return useMemo(() => {
    if (!annotations || annotations.length === 0) return null;
    return (
      <ul
        className="absolute bottom-0 left-0 right-0 max-h-32 overflow-y-auto bg-black/60 text-white text-[11px] px-3 py-2 space-y-1 backdrop-blur-sm"
        aria-label="Inline annotations"
        aria-live="polite"
        aria-atomic="false"
      >
        {annotations.map((a, idx) => (
          <li key={`${a.charStart}-${a.charEnd}-${idx}`} className="leading-snug">
            <span className="font-bold uppercase tracking-wider text-[9px] mr-1.5">{a.type}</span>
            {a.ruleId ? <span className="font-bold mr-1">[{a.ruleId}]</span> : null}
            <span>{a.note}</span>
            <span className="text-white/60 ml-2 tabular-nums">
              chars {a.charStart}-{a.charEnd}
            </span>
          </li>
        ))}
      </ul>
    );
  }, [annotations]);
}
