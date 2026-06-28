// Case Notes PDF highlight (de)serialisation helpers.
//
// Highlights are stored as fractional rectangles per page number and persisted
// as a JSON string of `Record<pageNumber, PdfHighlight[]>`. These helpers are
// the single, defensive boundary between that wire string and the in-memory map
// the WritingStimulus viewer consumes — malformed/empty input always degrades to
// an empty map rather than throwing in the exam UI.

/** Structural match for the viewer's `Highlight` type (kept dependency-free). */
export interface PdfHighlight {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

export type PdfHighlightMap = Record<number, PdfHighlight[]>;

const isFiniteNumber = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);

function isHighlight(v: unknown): v is PdfHighlight {
  if (!v || typeof v !== 'object') return false;
  const h = v as Record<string, unknown>;
  return (
    typeof h.id === 'string' &&
    isFiniteNumber(h.x) &&
    isFiniteNumber(h.y) &&
    isFiniteNumber(h.w) &&
    isFiniteNumber(h.h)
  );
}

/** Parse a stored highlights JSON string into a validated map. Never throws. */
export function parseHighlights(json: string | null | undefined): PdfHighlightMap {
  if (!json) return {};
  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch {
    return {};
  }
  if (!raw || typeof raw !== 'object') return {};
  const out: PdfHighlightMap = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    const page = Number(key);
    if (!Number.isInteger(page) || page < 1) continue;
    if (!Array.isArray(value)) continue;
    const marks = value.filter(isHighlight);
    if (marks.length > 0) out[page] = marks;
  }
  return out;
}

/** Serialise a highlights map to the wire JSON string. */
export function serializeHighlights(map: PdfHighlightMap): string {
  return JSON.stringify(map ?? {});
}

/** True when two highlight maps are value-equal (avoids redundant autosaves). */
export function highlightsEqual(a: PdfHighlightMap, b: PdfHighlightMap): boolean {
  return serializeHighlights(a) === serializeHighlights(b);
}
