/**
 * Accepted-variant manager logic for the Reading question editor.
 *
 * The backend grades Short Answer / Sentence Completion items against the
 * plain string array stored in `acceptedSynonymsJson`. This module keeps that
 * wire contract intact (serialise → array of strings) while letting authors
 * tag each variant with a UI-only category hint (UK/US spelling, hyphenation,
 * abbreviation, unit-spacing). Category hints are presentation metadata and
 * are intentionally NOT persisted — grading only ever sees the values.
 */

export type VariantCategory =
  | 'uk-spelling'
  | 'us-spelling'
  | 'hyphenation'
  | 'abbreviation'
  | 'unit-spacing'
  | 'other';

export interface AcceptedVariant {
  id: string;
  value: string;
  category: VariantCategory;
}

export const VARIANT_CATEGORY_OPTIONS: ReadonlyArray<{ value: VariantCategory; label: string }> = [
  { value: 'other', label: 'No hint' },
  { value: 'uk-spelling', label: 'UK spelling' },
  { value: 'us-spelling', label: 'US spelling' },
  { value: 'hyphenation', label: 'Hyphenation' },
  { value: 'abbreviation', label: 'Abbreviation' },
  { value: 'unit-spacing', label: 'Unit spacing' },
];

let fallbackCounter = 0;

function makeId(): string {
  const cryptoObj = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (cryptoObj?.randomUUID) return cryptoObj.randomUUID();
  fallbackCounter += 1;
  return `variant-${Date.now()}-${fallbackCounter}`;
}

function isVariantCategory(value: unknown): value is VariantCategory {
  return (
    value === 'uk-spelling' ||
    value === 'us-spelling' ||
    value === 'hyphenation' ||
    value === 'abbreviation' ||
    value === 'unit-spacing' ||
    value === 'other'
  );
}

/**
 * Parse the stored `acceptedSynonymsJson`. Accepts the canonical array of
 * strings and, defensively, an array of `{ value, category }` objects so a
 * future richer payload would not crash the editor.
 */
export function parseAcceptedVariants(json: string | null | undefined): AcceptedVariant[] {
  if (!json) return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    return [];
  }
  if (!Array.isArray(parsed)) return [];

  const variants: AcceptedVariant[] = [];
  for (const entry of parsed) {
    if (typeof entry === 'string') {
      const value = entry.trim();
      if (value) variants.push({ id: makeId(), value, category: 'other' });
    } else if (entry && typeof entry === 'object' && 'value' in entry) {
      const raw = (entry as { value: unknown }).value;
      const value = typeof raw === 'string' ? raw.trim() : '';
      if (!value) continue;
      const cat = (entry as { category?: unknown }).category;
      variants.push({ id: makeId(), value, category: isVariantCategory(cat) ? cat : 'other' });
    }
  }
  return variants;
}

/**
 * Serialise to the existing `acceptedSynonymsJson` shape — an array of the
 * trimmed string values, or `null` when empty (matches the legacy contract).
 */
export function serializeAcceptedVariants(variants: AcceptedVariant[]): string | null {
  const values = variants
    .map((v) => v.value.trim())
    .filter((v) => v.length > 0);
  return values.length > 0 ? JSON.stringify(values) : null;
}

/**
 * Add a variant. Trims input, ignores empties, and de-duplicates
 * case-insensitively against existing values (returns the list unchanged when
 * the value is empty or already present).
 */
export function addVariant(
  variants: AcceptedVariant[],
  value: string,
  category: VariantCategory = 'other',
): AcceptedVariant[] {
  const trimmed = value.trim();
  if (!trimmed) return variants;
  const exists = variants.some((v) => v.value.toLowerCase() === trimmed.toLowerCase());
  if (exists) return variants;
  return [...variants, { id: makeId(), value: trimmed, category }];
}

/** Remove a variant by its client id. */
export function removeVariant(variants: AcceptedVariant[], id: string): AcceptedVariant[] {
  return variants.filter((v) => v.id !== id);
}
