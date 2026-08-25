'use client';

/**
 * Video batch tag picker — admin UI for owner-directive 2026-08-26:
 * admins must be able to mark a video as "Full Course only",
 * "Crash Course / Fast Track only", or "Shared" without writing a
 * migration or a new code path. The entitlement service evaluates
 * the per-plan ContentOverridesJson "videos.excludeTags" array, and
 * the migration seeds every full-* plan with the four Crash Course
 * Writing batch tags. This component writes the matching tag into
 * the existing TagsCsv field that the rest of the system already
 * understands.
 *
 * Canonical batch strings (must match the migration):
 *   - batch:full-course-only
 *   - batch:crash-course-arabic-writing
 *   - batch:writing-sessions-crash-course-old
 *   - batch:fast-track-crash-course
 *   - batch:crash-course-workshops
 *   - batch:crash-course-only (admin convenience; anything the admin
 *     explicitly tags as Crash Course only, regardless of folder)
 *
 * Anything else the admin types into the custom-tags field is preserved
 * verbatim so the existing free-form tag system keeps working.
 */

import { useCallback, useMemo } from 'react';
import { Input } from '@/components/ui/form-controls';
import { Button } from '@/components/ui/button';
import { Tag as TagIcon } from 'lucide-react';

export const FULL_COURSE_ONLY_TAG = 'batch:full-course-only';
export const CRASH_COURSE_ONLY_TAG = 'batch:crash-course-only';
export const CRASH_COURSE_ARABIC_WRITING_TAG = 'batch:crash-course-arabic-writing';
export const CRASH_COURSE_WRITING_OLD_TAG = 'batch:writing-sessions-crash-course-old';
export const FAST_TRACK_CRASH_COURSE_TAG = 'batch:fast-track-crash-course';
export const CRASH_COURSE_WORKSHOPS_TAG = 'batch:crash-course-workshops';

export const VIDEO_BATCH_TAGS: ReadonlyArray<{ value: string; label: string; group: 'full' | 'crash' | 'shared' }> = [
  { value: FULL_COURSE_ONLY_TAG, label: 'Full Course only', group: 'full' },
  { value: CRASH_COURSE_ARABIC_WRITING_TAG, label: 'Crash Course — Arabic Writing', group: 'crash' },
  { value: CRASH_COURSE_WRITING_OLD_TAG, label: 'Crash Course — Writing Old', group: 'crash' },
  { value: FAST_TRACK_CRASH_COURSE_TAG, label: 'Fast-Track Crash Course', group: 'crash' },
  { value: CRASH_COURSE_WORKSHOPS_TAG, label: 'Crash Course — Workshops', group: 'crash' },
  { value: CRASH_COURSE_ONLY_TAG, label: 'Crash Course only (generic)', group: 'crash' },
];

export type VideoAccessMode = 'full' | 'crash' | 'shared' | 'custom';

export function detectAccessMode(tagsCsv: string): VideoAccessMode {
  const tags = parseTags(tagsCsv);
  const hasFull = tags.includes(FULL_COURSE_ONLY_TAG);
  const hasCrash =
    tags.includes(CRASH_COURSE_ONLY_TAG) ||
    tags.includes(CRASH_COURSE_ARABIC_WRITING_TAG) ||
    tags.includes(CRASH_COURSE_WRITING_OLD_TAG) ||
    tags.includes(FAST_TRACK_CRASH_COURSE_TAG) ||
    tags.includes(CRASH_COURSE_WORKSHOPS_TAG);
  if (hasFull && !hasCrash) return 'full';
  if (hasCrash && !hasFull) return 'crash';
  if (!hasFull && !hasCrash) return 'shared';
  return 'custom';
}

function parseTags(csv: string): string[] {
  return csv
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
}

function joinTags(tags: string[]): string {
  // Preserve insertion order, dedupe case-insensitively, trim, drop empties.
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of tags) {
    const t = raw.trim();
    if (!t) continue;
    const key = t.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(t);
  }
  return out.join(', ');
}

interface VideoBatchTagPickerProps {
  tagsCsv: string;
  onChange: (nextTagsCsv: string) => void;
}

export function VideoBatchTagPicker({ tagsCsv, onChange }: VideoBatchTagPickerProps) {
  const tags = useMemo(() => parseTags(tagsCsv), [tagsCsv]);
  const mode = useMemo(() => detectAccessMode(tagsCsv), [tagsCsv]);
  const otherTags = useMemo(
    () => tags.filter((t) => !VIDEO_BATCH_TAGS.some((b) => b.value.toLowerCase() === t.toLowerCase())),
    [tags],
  );

  const has = useCallback(
    (tag: string) => tags.some((t) => t.toLowerCase() === tag.toLowerCase()),
    [tags],
  );

  const toggle = useCallback(
    (tag: string) => {
      if (has(tag)) {
        onChange(joinTags(tags.filter((t) => t.toLowerCase() !== tag.toLowerCase())));
      } else {
        onChange(joinTags([...tags, tag]));
      }
    },
    [has, onChange, tags],
  );

  const setOtherTags = useCallback(
    (next: string) => {
      const nextTags = parseTags(next);
      // Preserve any VIDEO_BATCH_TAGS that the admin already chose.
      const batchTags = tags.filter((t) => VIDEO_BATCH_TAGS.some((b) => b.value.toLowerCase() === t.toLowerCase()));
      onChange(joinTags([...batchTags, ...nextTags]));
    },
    [onChange, tags],
  );

  return (
    <div className="space-y-3" data-testid="video-batch-tag-picker">
      <div className="flex items-center gap-2">
        <TagIcon className="h-4 w-4 text-muted" aria-hidden="true" />
        <p className="text-sm font-semibold tracking-tight text-navy">Video access</p>
        <span className="text-xs text-muted">
          {mode === 'full'
            ? 'Full Course only'
            : mode === 'crash'
              ? 'Crash Course / Fast Track only'
              : mode === 'shared'
                ? 'Shared — both plans can access'
                : 'Custom tags'}
        </span>
      </div>

      <div className="flex flex-wrap gap-2">
        {VIDEO_BATCH_TAGS.map((b) => {
          const selected = has(b.value);
          return (
            <Button
              key={b.value}
              type="button"
              size="sm"
              variant={selected ? 'primary' : 'outline'}
              onClick={() => toggle(b.value)}
              aria-pressed={selected}
              data-batch-tag={b.value}
            >
              {b.label}
            </Button>
          );
        })}
      </div>

      <Input
        label="Other tags (comma-separated)"
        value={otherTags.join(', ')}
        onChange={(e) => setOtherTags(e.target.value)}
        placeholder="e.g. skimming, part-b, time management"
        maxLength={500}
      />
    </div>
  );
}
