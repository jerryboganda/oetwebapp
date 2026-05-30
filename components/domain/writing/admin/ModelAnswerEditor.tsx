'use client';

import { useCallback } from 'react';

import { Button } from '@/components/admin/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { IconButton } from './IconButton';
import {
  countWords,
  makeEmptyParagraph,
  type ModelAnswerParagraphDraft,
} from './builder-state';

interface ModelAnswerEditorProps {
  modelAnswerText: string;
  onModelAnswerTextChange: (next: string) => void;
  paragraphs: ModelAnswerParagraphDraft[];
  onParagraphsChange: (next: ModelAnswerParagraphDraft[]) => void;
  wordGuideMin: number;
  wordGuideMax: number;
}

/**
 * Model answer editor (spec §5). The whole-letter text is the primary field;
 * the optional paragraph breakdown (text + marker rationale + language notes)
 * maps to `WritingModelAnswerParagraphDto`. `criteria`/`included`/`excluded`
 * arrays are preserved on round-trip but not edited here (kept minimal).
 */
export function ModelAnswerEditor({
  modelAnswerText,
  onModelAnswerTextChange,
  paragraphs,
  onParagraphsChange,
  wordGuideMin,
  wordGuideMax,
}: ModelAnswerEditorProps) {
  const words = countWords(modelAnswerText);
  const withinGuide = words === 0 || (words >= wordGuideMin && words <= wordGuideMax);

  const patchPara = useCallback(
    (id: string, p: Partial<ModelAnswerParagraphDraft>) => {
      onParagraphsChange(paragraphs.map((it) => (it.id === id ? { ...it, ...p } : it)));
    },
    [paragraphs, onParagraphsChange],
  );

  const addPara = useCallback(() => {
    onParagraphsChange([...paragraphs, makeEmptyParagraph()]);
  }, [paragraphs, onParagraphsChange]);

  const removePara = useCallback(
    (id: string) => {
      onParagraphsChange(paragraphs.filter((it) => it.id !== id));
    },
    [paragraphs, onParagraphsChange],
  );

  const movePara = useCallback(
    (id: string, dir: -1 | 1) => {
      const idx = paragraphs.findIndex((it) => it.id === id);
      if (idx < 0) return;
      const next = idx + dir;
      if (next < 0 || next >= paragraphs.length) return;
      const copy = [...paragraphs];
      const [item] = copy.splice(idx, 1);
      copy.splice(next, 0, item);
      onParagraphsChange(copy);
    },
    [paragraphs, onParagraphsChange],
  );

  return (
    <div className="space-y-5">
      <div>
        <Textarea
          label="Model answer (full letter)"
          hint="The reference letter graders compare against."
          value={modelAnswerText}
          onChange={(e) => onModelAnswerTextChange(e.target.value)}
          rows={12}
          placeholder={'Dear Dr Lin,\n\nI am writing to refer…'}
        />
        <p
          className={`mt-1.5 text-xs ${withinGuide ? 'text-muted' : 'text-amber-600'}`}
          aria-live="polite"
        >
          {words} word{words === 1 ? '' : 's'}
          {' · guide '}
          {wordGuideMin}–{wordGuideMax}
          {!withinGuide && ' (outside guide)'}
        </p>
      </div>

      <div>
        <div className="mb-2">
          <h3 className="text-sm font-semibold tracking-tight text-navy">
            Paragraph breakdown{' '}
            <span className="font-normal text-muted">(optional)</span>
          </h3>
          <p className="text-xs text-muted">
            Split the model answer into paragraphs with marker rationale and
            language notes.
          </p>
        </div>

        <div className="space-y-3">
          {paragraphs.map((para, idx) => (
            <div
              key={para.id}
              className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm"
            >
              <div className="flex items-start gap-3">
                <span className="mt-2.5 text-xs font-medium tabular-nums text-slate-400">
                  ¶{idx + 1}
                </span>
                <div className="flex-1 space-y-2">
                  <Textarea
                    label="Paragraph text"
                    value={para.text}
                    onChange={(e) => patchPara(para.id, { text: e.target.value })}
                    rows={3}
                  />
                  <Textarea
                    label="Marker rationale"
                    hint="Why this paragraph earns marks / what it covers."
                    value={para.rationale}
                    onChange={(e) => patchPara(para.id, { rationale: e.target.value })}
                    rows={2}
                  />
                  <Textarea
                    label="Language notes"
                    hint="Optional — phrasing, register, or grammar to highlight."
                    value={para.languageNotes}
                    onChange={(e) =>
                      patchPara(para.id, { languageNotes: e.target.value })
                    }
                    rows={2}
                  />
                </div>
                <div className="flex shrink-0 flex-col gap-0.5">
                  <IconButton
                    onClick={() => movePara(para.id, -1)}
                    disabled={idx === 0}
                    aria-label="Move paragraph up"
                  >
                    ↑
                  </IconButton>
                  <IconButton
                    onClick={() => movePara(para.id, 1)}
                    disabled={idx === paragraphs.length - 1}
                    aria-label="Move paragraph down"
                  >
                    ↓
                  </IconButton>
                  <IconButton
                    tone="danger"
                    onClick={() => removePara(para.id)}
                    aria-label="Remove paragraph"
                  >
                    ✕
                  </IconButton>
                </div>
              </div>
            </div>
          ))}
          <Button variant="secondary" size="sm" onClick={addPara}>
            + Add paragraph
          </Button>
        </div>
      </div>
    </div>
  );
}
