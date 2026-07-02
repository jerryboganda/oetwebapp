'use client';

/**
 * Extras — chapters. Controlled editor: the parent step owns the chapter
 * list and persists it via `PUT …/chapters` in the step submit, so chapter
 * edits participate in the wizard's navigate-with-save contract.
 */

import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import type { AdminVideoChapter } from '@/lib/api/video-library';

export interface ChaptersEditorProps {
  chapters: AdminVideoChapter[];
  onChange: (chapters: AdminVideoChapter[]) => void;
  disabled?: boolean;
}

function formatTime(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(s / 60);
  return `${m}:${String(s % 60).padStart(2, '0')}`;
}

export function ChaptersEditor({ chapters, onChange, disabled }: ChaptersEditorProps) {
  function update(index: number, patch: Partial<AdminVideoChapter>) {
    onChange(chapters.map((chapter, i) => (i === index ? { ...chapter, ...patch } : chapter)));
  }

  function remove(index: number) {
    onChange(chapters.filter((_, i) => i !== index));
  }

  function add() {
    const last = chapters[chapters.length - 1];
    onChange([...chapters, { timeSeconds: last ? last.timeSeconds + 60 : 0, title: '' }]);
  }

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <p className="text-sm font-bold text-navy">Chapters</p>
      <p className="text-xs text-muted">
        Timestamped sections shown on the player timeline. Saved with this step.
      </p>

      {chapters.length > 0 ? (
        <ul className="mt-3 space-y-2">
          {chapters.map((chapter, index) => (
            <li key={index} className="grid gap-2 sm:grid-cols-[8rem_1fr_auto]">
              <Input
                label={index === 0 ? 'Time (seconds)' : undefined}
                aria-label={`Chapter ${index + 1} time in seconds`}
                type="number"
                min={0}
                value={String(chapter.timeSeconds)}
                onChange={(e) => update(index, { timeSeconds: Math.max(0, Number(e.target.value) || 0) })}
                hint={formatTime(chapter.timeSeconds)}
                disabled={disabled}
              />
              <Input
                label={index === 0 ? 'Chapter title' : undefined}
                aria-label={`Chapter ${index + 1} title`}
                value={chapter.title}
                onChange={(e) => update(index, { title: e.target.value })}
                placeholder='e.g. "Question walkthrough"'
                maxLength={160}
                disabled={disabled}
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className={index === 0 ? 'self-end mb-1' : 'self-start mt-1'}
                onClick={() => remove(index)}
                disabled={disabled}
                aria-label={`Remove chapter ${index + 1}`}
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-3 text-xs text-muted">No chapters yet.</p>
      )}

      <div className="mt-3">
        <Button type="button" variant="outline" size="sm" onClick={add} disabled={disabled}>
          <Plus className="mr-1 h-3.5 w-3.5" /> Add chapter
        </Button>
      </div>
    </div>
  );
}
