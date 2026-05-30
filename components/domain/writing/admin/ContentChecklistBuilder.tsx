'use client';

import { useCallback } from 'react';

import { Button } from '@/components/admin/ui/button';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import type { WritingSeverity, WritingChecklistRequiredStatus } from '@/lib/writing/types';
import { IconButton } from './IconButton';
import {
  makeEmptyChecklistItem,
  type ChecklistItemDraft,
  type CaseNoteSectionDraft,
} from './builder-state';

type ChecklistVariant = 'key' | 'irrelevant';

interface ContentChecklistBuilderProps {
  variant: ChecklistVariant;
  items: ChecklistItemDraft[];
  onChange: (next: ChecklistItemDraft[]) => void;
  /** For the "linked case-note section" dropdown (key variant only). */
  sections?: CaseNoteSectionDraft[];
}

const IMPORTANCE_OPTIONS: { value: WritingSeverity; label: string }[] = [
  { value: 'high', label: 'High' },
  { value: 'medium', label: 'Medium' },
  { value: 'low', label: 'Low' },
];

const REQUIRED_OPTIONS: { value: WritingChecklistRequiredStatus; label: string }[] = [
  { value: 'required', label: 'Required' },
  { value: 'optional', label: 'Optional' },
];

/**
 * Builder for the key-content and irrelevant-content checklists (spec §6).
 *
 * - `key`: full row — itemText, category, importance, requiredStatus
 *   (required/optional), linkedCaseNoteSection, expectedRepresentation,
 *   commonError.
 * - `irrelevant`: distractor row — itemText, category, commonError;
 *   requiredStatus is fixed to "irrelevant".
 *
 * `ordinal` is recomputed from array position on serialise (see builder-state).
 */
export function ContentChecklistBuilder({
  variant,
  items,
  onChange,
  sections = [],
}: ContentChecklistBuilderProps) {
  const isKey = variant === 'key';

  const patch = useCallback(
    (id: string, p: Partial<ChecklistItemDraft>) => {
      onChange(items.map((it) => (it.id === id ? { ...it, ...p } : it)));
    },
    [items, onChange],
  );

  const add = useCallback(() => {
    onChange([
      ...items,
      makeEmptyChecklistItem(isKey ? 'required' : 'irrelevant', items.length),
    ]);
  }, [items, onChange, isKey]);

  const remove = useCallback(
    (id: string) => {
      onChange(items.filter((it) => it.id !== id));
    },
    [items, onChange],
  );

  const move = useCallback(
    (id: string, dir: -1 | 1) => {
      const idx = items.findIndex((it) => it.id === id);
      if (idx < 0) return;
      const next = idx + dir;
      if (next < 0 || next >= items.length) return;
      const copy = [...items];
      const [item] = copy.splice(idx, 1);
      copy.splice(next, 0, item);
      onChange(copy);
    },
    [items, onChange],
  );

  const sectionOptions = [
    { value: '', label: '— None —' },
    ...sections.map((s) => ({
      value: s.heading,
      label: s.heading || '(untitled section)',
    })),
  ];

  return (
    <div className="space-y-3">
      {items.length === 0 && (
        <p className="rounded-lg border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          {isKey
            ? 'No key-content points yet. These are the facts a strong letter must include.'
            : 'No distractors yet. These are notes a candidate should deliberately leave out.'}
        </p>
      )}

      {items.map((item, idx) => (
        <div
          key={item.id}
          className="rounded-lg border border-slate-200 bg-white p-3 shadow-sm"
        >
          <div className="flex items-start gap-3">
            <span className="mt-2.5 text-xs font-medium tabular-nums text-slate-400">
              {idx + 1}
            </span>
            <div className="flex-1 space-y-3">
              <Textarea
                label="Content point"
                value={item.itemText}
                onChange={(e) => patch(item.id, { itemText: e.target.value })}
                rows={2}
                placeholder={
                  isKey
                    ? 'e.g. Patient has a documented penicillin allergy'
                    : 'e.g. Patient supports a local football club'
                }
              />

              <div className="grid gap-3 sm:grid-cols-2">
                <Input
                  label="Category"
                  value={item.category}
                  onChange={(e) => patch(item.id, { category: e.target.value })}
                  placeholder="e.g. Medical history"
                />
                {isKey ? (
                  <Select
                    label="Importance"
                    value={item.importance}
                    onChange={(e) =>
                      patch(item.id, { importance: e.target.value as WritingSeverity })
                    }
                    options={IMPORTANCE_OPTIONS}
                  />
                ) : (
                  <Textarea
                    label="Common error"
                    hint="Why a candidate might wrongly include this."
                    value={item.commonError ?? ''}
                    onChange={(e) =>
                      patch(item.id, { commonError: e.target.value })
                    }
                    rows={2}
                    placeholder="e.g. Mistaken for clinically relevant social history"
                  />
                )}
              </div>

              {isKey && (
                <>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <Select
                      label="Requirement"
                      value={item.requiredStatus}
                      onChange={(e) =>
                        patch(item.id, {
                          requiredStatus: e.target
                            .value as WritingChecklistRequiredStatus,
                        })
                      }
                      options={REQUIRED_OPTIONS}
                    />
                    <Select
                      label="Linked case-note section"
                      hint="Where this fact appears in the notes."
                      value={item.linkedCaseNoteSection ?? ''}
                      onChange={(e) =>
                        patch(item.id, {
                          linkedCaseNoteSection: e.target.value || null,
                        })
                      }
                      options={sectionOptions}
                    />
                  </div>
                  <Input
                    label="Expected representation"
                    hint="How the model letter phrases this."
                    value={item.expectedRepresentation ?? ''}
                    onChange={(e) =>
                      patch(item.id, { expectedRepresentation: e.target.value })
                    }
                    placeholder="e.g. States the penicillin allergy explicitly"
                  />
                  <Textarea
                    label="Common error"
                    hint="Optional — what candidates get wrong here."
                    value={item.commonError ?? ''}
                    onChange={(e) => patch(item.id, { commonError: e.target.value })}
                    rows={2}
                    placeholder="e.g. Omits the allergy or buries it mid-paragraph"
                  />
                </>
              )}
            </div>

            <div className="flex shrink-0 flex-col gap-0.5">
              <IconButton
                onClick={() => move(item.id, -1)}
                disabled={idx === 0}
                aria-label="Move up"
              >
                ↑
              </IconButton>
              <IconButton
                onClick={() => move(item.id, 1)}
                disabled={idx === items.length - 1}
                aria-label="Move down"
              >
                ↓
              </IconButton>
              <IconButton
                tone="danger"
                onClick={() => remove(item.id)}
                aria-label="Remove item"
              >
                ✕
              </IconButton>
            </div>
          </div>
        </div>
      ))}

      <Button variant="secondary" size="sm" onClick={add}>
        + {isKey ? 'Add key-content point' : 'Add distractor'}
      </Button>
    </div>
  );
}
