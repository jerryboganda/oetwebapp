'use client';

import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';

export interface LabeledBox {
  key: string;
  title: string;
  answer: string;
  variants: string[];
  explanation: string;
}

interface LabeledBoxesManagerProps {
  boxes: LabeledBox[];
  onChange: (boxes: LabeledBox[]) => void;
}

function nextKey(boxes: LabeledBox[]): string {
  const existing = new Set(boxes.map((b) => b.key));
  let n = 1;
  while (existing.has(`box${n}`)) n++;
  return `box${n}`;
}

export function LabeledBoxesManager({ boxes, onChange }: LabeledBoxesManagerProps) {
  function addBox() {
    onChange([...boxes, { key: nextKey(boxes), title: '', answer: '', variants: [], explanation: '' }]);
  }

  function removeBox(idx: number) {
    onChange(boxes.filter((_, i) => i !== idx));
  }

  function moveUp(idx: number) {
    if (idx === 0) return;
    const next = [...boxes];
    [next[idx - 1], next[idx]] = [next[idx], next[idx - 1]];
    onChange(next);
  }

  function moveDown(idx: number) {
    if (idx >= boxes.length - 1) return;
    const next = [...boxes];
    [next[idx], next[idx + 1]] = [next[idx + 1], next[idx]];
    onChange(next);
  }

  function updateBox(idx: number, patch: Partial<LabeledBox>) {
    onChange(boxes.map((b, i) => (i === idx ? { ...b, ...patch } : b)));
  }

  function updateVariants(idx: number, raw: string) {
    const variants = raw
      .split('\n')
      .map((v) => v.trim())
      .filter(Boolean);
    updateBox(idx, { variants });
  }

  return (
    <div className="space-y-3">
      <p className="text-xs font-medium text-admin-fg-muted uppercase tracking-wide">Answer boxes</p>

      {boxes.length === 0 && (
        <p className="text-sm text-admin-fg-muted py-2">No boxes yet. Add one below.</p>
      )}

      {boxes.map((box, idx) => (
        <div
          key={box.key}
          className="rounded-lg border border-admin-border bg-admin-bg-subtle/50 p-3 space-y-3"
        >
          <div className="flex items-center justify-between gap-2">
            <span className="text-xs font-mono text-admin-fg-muted">{box.key}</span>
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={() => moveUp(idx)}
                disabled={idx === 0}
                className="p-0.5 text-admin-fg-muted hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                aria-label="Move box up"
              >
                <ArrowUp className="h-3 w-3" />
              </button>
              <button
                type="button"
                onClick={() => moveDown(idx)}
                disabled={idx >= boxes.length - 1}
                className="p-0.5 text-admin-fg-muted hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                aria-label="Move box down"
              >
                <ArrowDown className="h-3 w-3" />
              </button>
              <button
                type="button"
                onClick={() => removeBox(idx)}
                className="p-0.5 text-red-400 hover:text-red-600"
                aria-label="Remove box"
              >
                <Trash2 className="h-3 w-3" />
              </button>
            </div>
          </div>

          <Input
            label="Box title (shown to student)"
            value={box.title}
            onChange={(e) => updateBox(idx, { title: e.target.value })}
            placeholder="e.g. Patient name"
          />
          <Input
            label="Correct answer"
            value={box.answer}
            onChange={(e) => updateBox(idx, { answer: e.target.value })}
            placeholder="Exact correct answer…"
          />
          <Textarea
            label="Accepted variants (one per line, optional)"
            value={box.variants.join('\n')}
            onChange={(e) => updateVariants(idx, e.target.value)}
            rows={2}
            placeholder="Alternate spellings or synonyms…"
          />
          <Textarea
            label="Explanation (optional, shown after submitting)"
            value={box.explanation}
            onChange={(e) => updateBox(idx, { explanation: e.target.value })}
            rows={2}
            placeholder="Why this is the correct answer for this box…"
          />
        </div>
      ))}

      <Button variant="ghost" size="sm" onClick={addBox}>
        <Plus className="h-4 w-4 mr-1" />
        Add box
      </Button>
    </div>
  );
}

// ── Serialization helpers ───────────────────────────────────────────────

export function serializeLabeledBoxes(boxes: LabeledBox[]): {
  optionsJson: string;
  correctAnswerJson: string;
  acceptedSynonymsJson: string | null;
  boxExplanationsJson: string | null;
} {
  const options = boxes.map((b) => ({ value: b.key, label: b.title }));
  const correct: Record<string, string> = {};
  const synonyms: Record<string, string[]> = {};
  const explanations: Record<string, string> = {};
  let hasSynonyms = false;
  let hasExplanations = false;
  for (const b of boxes) {
    correct[b.key] = b.answer;
    if (b.variants.length > 0) {
      synonyms[b.key] = b.variants;
      hasSynonyms = true;
    }
    if (b.explanation.trim()) {
      explanations[b.key] = b.explanation.trim();
      hasExplanations = true;
    }
  }
  return {
    optionsJson: JSON.stringify(options),
    correctAnswerJson: JSON.stringify(correct),
    acceptedSynonymsJson: hasSynonyms ? JSON.stringify(synonyms) : null,
    boxExplanationsJson: hasExplanations ? JSON.stringify(explanations) : null,
  };
}

export function parseLabeledBoxes(
  optionsJson: string,
  correctAnswerJson: string,
  acceptedSynonymsJson: string | null | undefined,
  boxExplanationsJson?: string | null,
): LabeledBox[] {
  try {
    const options = JSON.parse(optionsJson);
    const correct = JSON.parse(correctAnswerJson);
    const synonyms = acceptedSynonymsJson ? JSON.parse(acceptedSynonymsJson) : {};
    const explanations = boxExplanationsJson ? JSON.parse(boxExplanationsJson) : {};

    if (!Array.isArray(options)) return [];

    return options.map((opt: { value: string; label: string }) => ({
      key: opt.value,
      title: opt.label ?? '',
      answer: typeof correct[opt.value] === 'string' ? correct[opt.value] : '',
      variants: Array.isArray(synonyms[opt.value]) ? (synonyms[opt.value] as string[]) : [],
      explanation: typeof explanations[opt.value] === 'string' ? explanations[opt.value] : '',
    }));
  } catch {
    return [];
  }
}
