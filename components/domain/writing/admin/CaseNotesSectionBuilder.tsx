'use client';

import { useCallback } from 'react';

import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/ui/form-controls';
import { IconButton } from './IconButton';
import { StringLineListEditor } from './StringLineListEditor';
import { makeEmptySection, type CaseNoteSectionDraft } from './builder-state';

interface CaseNotesSectionBuilderProps {
  sections: CaseNoteSectionDraft[];
  onChange: (next: CaseNoteSectionDraft[]) => void;
}

/**
 * The centrepiece structured case-notes builder (spec §4).
 *
 * A reorderable list of sections; each section has a heading and a reorderable
 * list of note items. Section reorder/remove via keyboard-accessible buttons;
 * item editing delegates to StringLineListEditor. The contract section DTO is
 * `{ heading, items }`; we carry a transient `key` for stable React lists and
 * recompute array order on serialise.
 */
export function CaseNotesSectionBuilder({ sections, onChange }: CaseNotesSectionBuilderProps) {
  const patchSection = useCallback(
    (key: string, patch: Partial<CaseNoteSectionDraft>) => {
      onChange(sections.map((s) => (s.key === key ? { ...s, ...patch } : s)));
    },
    [sections, onChange],
  );

  const addSection = useCallback(() => {
    onChange([...sections, makeEmptySection()]);
  }, [sections, onChange]);

  const removeSection = useCallback(
    (key: string) => {
      onChange(sections.filter((s) => s.key !== key));
    },
    [sections, onChange],
  );

  const moveSection = useCallback(
    (key: string, dir: -1 | 1) => {
      const idx = sections.findIndex((s) => s.key === key);
      if (idx < 0) return;
      const next = idx + dir;
      if (next < 0 || next >= sections.length) return;
      const copy = [...sections];
      const [item] = copy.splice(idx, 1);
      copy.splice(next, 0, item);
      onChange(copy);
    },
    [sections, onChange],
  );

  return (
    <div className="space-y-4">
      {sections.length === 0 && (
        <p className="rounded-lg border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500">
          No case-note sections yet. Add a section (e.g. &ldquo;Patient
          details&rdquo;, &ldquo;Presenting complaint&rdquo;, &ldquo;Medical
          history&rdquo;) to begin.
        </p>
      )}

      {sections.map((section, idx) => (
        <div
          key={section.key}
          className="rounded-xl border border-slate-200 bg-white shadow-sm"
        >
          <div className="flex items-start gap-3 border-b border-slate-100 px-4 py-3">
            <span className="mt-9 text-xs font-semibold tabular-nums text-slate-400">
              {idx + 1}
            </span>
            <div className="flex-1">
              <Input
                label="Section heading"
                value={section.heading}
                onChange={(e) => patchSection(section.key, { heading: e.target.value })}
                placeholder="e.g. Presenting complaint"
              />
            </div>
            <div className="flex shrink-0 items-center gap-0.5 pt-8">
              <IconButton
                onClick={() => moveSection(section.key, -1)}
                disabled={idx === 0}
                aria-label="Move section up"
              >
                ↑
              </IconButton>
              <IconButton
                onClick={() => moveSection(section.key, 1)}
                disabled={idx === sections.length - 1}
                aria-label="Move section down"
              >
                ↓
              </IconButton>
              <IconButton
                tone="danger"
                onClick={() => removeSection(section.key)}
                aria-label="Remove section"
              >
                ✕
              </IconButton>
            </div>
          </div>
          <div className="px-4 py-3">
            <p className="mb-2 text-xs font-medium text-slate-500">Notes</p>
            <StringLineListEditor
              lines={section.items}
              onChange={(items) => patchSection(section.key, { items })}
              addLabel="Add note"
              itemNoun="note"
              multiline
              placeholder="e.g. 68yo male, retired teacher"
              emptyHint="No notes in this section yet."
            />
          </div>
        </div>
      ))}

      <Button variant="secondary" size="sm" onClick={addSection}>
        + Add section
      </Button>
    </div>
  );
}
