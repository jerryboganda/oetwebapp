/**
 * Shared answer canonicalisation policy for grammar grading. MUST stay
 * in lock-step with `GrammarCanonicaliser.cs`. The client uses this for
 * optimistic "your answer" preview and for determining which MCQ option
 * should render as selected; the authoritative grade comes from the
 * server.
 */

const EDGE_PUNCT = /^[.,;:!?'"`()[\]{}]+|[.,;:!?'"`()[\]{}]+$/g;

export function canonicalise(input: string | null | undefined): string {
  if (!input) return '';
  return input.trim().replace(/\s+/g, ' ').replace(EDGE_PUNCT, '').trim().toLowerCase();
}

export function matches(user: string, expected: string): boolean {
  return canonicalise(user) === canonicalise(expected);
}

export function matchesAny(user: string, expecteds: ReadonlyArray<string>): boolean {
  const c = canonicalise(user);
  return expecteds.some((e) => canonicalise(e) === c);
}
