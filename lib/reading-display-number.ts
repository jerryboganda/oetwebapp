import type { ReadingPartCode } from '@/lib/reading-authoring-api';

/**
 * Single source of truth for the PDF-facing ("public") Reading question numbers.
 *
 * Questions are stored with section-local internal display orders:
 *   - Part A: 1..20 (no sections)
 *   - Part B: each section (B1..B6) holds one question at internal order 1
 *   - Part C: C1 1..8, C2 9..16
 *
 * The official OET answer-sheet numbering is:
 *   - Part A: 1..20 (its own timed sheet)
 *   - Part B: 1..6, then Part C: 7..22 (B and C share one continuous sequence)
 *
 * So Part A and Part B render their internal order verbatim, and only Part C is
 * offset by +6 (C1 1..8 -> 7..14, C2 9..16 -> 15..22). Keeping this rule in one
 * function makes any future numbering change a single edit.
 */
export const READING_PART_C_PUBLIC_OFFSET = 6;

export function readingPublicDisplayNumber(
  partCode: ReadingPartCode,
  internalDisplayOrder: number,
): number {
  if (partCode === 'C') return internalDisplayOrder + READING_PART_C_PUBLIC_OFFSET;
  return internalDisplayOrder; // A and B unchanged
}
