import { useEffect } from 'react';
import { ApiError } from '@/lib/api';
import type { ReadingAttemptStarted, ReadingAttemptStatus, ReadingLearnerStructureDto, ReadingPartCode, ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';

export { type SaveState, isNetworkInterruption } from '@/lib/save-state';

export type PendingReadingAnswer = {
  attemptId: string;
  valueJson: string;
  baseValueJson: string | null;
  inFlight: boolean;
};

/**
 * Server rejections that mean "that section had already closed", not "the save
 * failed". They are expected whenever a debounced or keepalive autosave lands
 * just after a section boundary — most visibly right after the candidate
 * presses Submit Part A. Treat them as settled so the learner never sees a red
 * "Autosave failed." banner at the exact moment the break screen opens.
 */
export const BENIGN_SAVE_REJECTIONS = new Set([
  'part_a_locked',
  'part_bc_not_open',
  'part_bc_break_not_resumed',
]);

export function isBenignLockedSave(error: unknown): boolean {
  return error instanceof ApiError && BENIGN_SAVE_REJECTIONS.has(error.code ?? '');
}

export type ReadingSectionCode = 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2';

export const SECTION_LABELS: Record<ReadingSectionCode, string> = {
  B1: 'B1',
  B2: 'B2',
  B3: 'B3',
  B4: 'B4',
  B5: 'B5',
  B6: 'B6',
  C1: 'C1',
  C2: 'C2',
};

export interface ActiveAttempt {
  attemptId: string;
  startedAt: string;
  deadlineAt: string;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  paperTitle: string;
  partATimerMinutes: number;
  partBCTimerMinutes: number;
  answeredCount: number;
  canResume: boolean;
  partABreakAvailable: boolean;
  partABreakResumed: boolean;
  partBCTimerPausedAt: string | null;
  partBCPausedSeconds: number;
  partABreakMaxSeconds: number;
  serverNow: string;
  status: ReadingAttemptStatus;
  /** Phase 3: which practice mode this attempt is running under. */
  mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank';
  /** Subset modes only — in-scope question IDs (filter the structure to these). */
  scopeQuestionIds: string[] | null;
  /** Untimed Practice (Final Developer Brief item 7) — no timer widget. */
  isUntimed?: boolean;
}

export function getSectionsForPart(part: ReadingLearnerStructureDto['parts'][number]) {
  if (part.partCode === 'A') {
    return [] as Array<{ code: ReadingSectionCode; label: string; questions: ReadingQuestionLearnerDto[] }>;
  }

  const persisted = (part.sections ?? [])
    .map((section) => ({
      code: section.sectionCode,
      label: SECTION_LABELS[section.sectionCode],
      questions: section.questions,
    }))
    .filter((section) => section.questions.length > 0);
  if (persisted.length > 0) return persisted;

  const sorted = [...part.questions].sort((left, right) => left.displayOrder - right.displayOrder);
  if (part.partCode === 'B') {
    return sorted.slice(0, 6).map((question, index) => {
      const code = (`B${index + 1}` as ReadingSectionCode);
      return { code, label: SECTION_LABELS[code], questions: [question] };
    });
  }

  return [
    { code: 'C1' as const, label: SECTION_LABELS.C1, questions: sorted.slice(0, 8) },
    { code: 'C2' as const, label: SECTION_LABELS.C2, questions: sorted.slice(8, 16) },
  ].filter((section) => section.questions.length > 0);
}

export function fromStartedAttempt(started: ReadingAttemptStarted): ActiveAttempt {
  return {
    attemptId: started.attemptId,
    startedAt: started.startedAt,
    deadlineAt: started.deadlineAt,
    partADeadlineAt: started.partADeadlineAt,
    partBCDeadlineAt: started.partBCDeadlineAt,
    paperTitle: started.paperTitle,
    partATimerMinutes: started.partATimerMinutes,
    partBCTimerMinutes: started.partBCTimerMinutes,
    answeredCount: started.answeredCount,
    canResume: started.canResume,
    partABreakAvailable: started.partABreakAvailable,
    partABreakResumed: started.partABreakResumed,
    partBCTimerPausedAt: started.partBCTimerPausedAt,
    partBCPausedSeconds: started.partBCPausedSeconds,
    partABreakMaxSeconds: started.partABreakMaxSeconds,
    serverNow: started.serverNow,
    status: 'InProgress',
    // The /attempts/{id} POST returns the full canonical attempt; mode is
    // always Exam at this entry point. Practice modes are launched via the
    // dedicated practice endpoints which redirect to this page with
    // ?attemptId=… so the resume path picks up `mode` from the GET.
    mode: 'Exam',
    scopeQuestionIds: null,
  };
}

export function isQuestionLocked(activePart: ReadingPartCode, partALocked: boolean, paperExpired: boolean, breakPending: boolean) {
  return paperExpired || breakPending || (activePart === 'A' && partALocked);
}

export function practiceModeLabel(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank'): string {
  switch (mode) {
    case 'Learning': return 'Learning Mode';
    case 'Drill': return 'Skill Drill';
    case 'MiniTest': return 'Mini-Test';
    case 'ErrorBank': return 'Error Bank Retest';
    default: return 'Practice';
  }
}

export function isSubsetPracticeMode(mode: 'Exam' | 'Learning' | 'Drill' | 'MiniTest' | 'ErrorBank') {
  return mode === 'Drill' || mode === 'MiniTest' || mode === 'ErrorBank';
}

export function useReadingBrowserZoomGuard() {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey)) return;
      if (event.key === '+' || event.key === '=' || event.key === '-' || event.key === '0') {
        event.preventDefault();
      }
    };
    const onWheel = (event: WheelEvent) => {
      if (event.ctrlKey || event.metaKey) {
        event.preventDefault();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('wheel', onWheel, { passive: false });
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('wheel', onWheel);
    };
  }, []);
}

export function toOptionList(options: unknown): Array<{ value: string; label: string }> {
  if (!Array.isArray(options)) return [];
  return options.map((option, index) => {
    if (typeof option === 'string') return { value: String.fromCharCode(65 + index), label: option };
    if (typeof option === 'number') return { value: String.fromCharCode(65 + index), label: String(option) };
    if (option && typeof option === 'object') {
      const record = option as Record<string, unknown>;
      return {
        value: String(record.value ?? record.key ?? record.letter ?? String.fromCharCode(65 + index)),
        label: String(record.label ?? record.text ?? record.title ?? record.value ?? ''),
      };
    }
    return { value: String.fromCharCode(65 + index), label: String(option ?? '') };
  });
}

export function toMatchingOptions(
  options: unknown,
  texts: ReadingLearnerStructureDto['parts'][number]['texts'],
): Array<{ value: string; label: string }> {
  const parsed = toOptionList(options).map((option, index) => ({
    value: option.value || String.fromCharCode(65 + index),
    label: option.label,
  }));
  if (parsed.length > 0) return parsed;
  return texts
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((text, index) => {
      const value = String.fromCharCode(65 + index);
      return { value, label: `Text ${value}: ${text.title}` };
    });
}

export function parseAnswer(valueJson: string): unknown {
  if (!valueJson) return null;
  try {
    return JSON.parse(valueJson);
  } catch {
    return valueJson;
  }
}

export function isAnsweredJson(valueJson: string | undefined): boolean {
  const value = parseAnswer(valueJson ?? '');
  if (Array.isArray(value)) return value.length > 0;
  if (value && typeof value === 'object') {
    return Object.values(value as Record<string, unknown>).some((entry) => {
      if (typeof entry === 'string') return entry.trim().length > 0;
      return entry !== null && entry !== undefined && String(entry).trim().length > 0;
    });
  }
  return typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined;
}

export function toggleSetValue(source: Set<string>, value: string): Set<string> {
  const next = new Set(source);
  if (next.has(value)) next.delete(value);
  else next.add(value);
  return next;
}

export function formatCountdown(totalSec: number): string {
  const minutes = Math.floor(totalSec / 60);
  const seconds = totalSec % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function minutesBetween(start: string, end: string): number {
  return Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60000));
}
