/**
 * Intra-series exam ordering for the Reading / Listening folder browsers.
 *
 * Learner-facing series folders must list their exams in ASCENDING order of
 * the number already written into the exam title ("Atlas Practice Series 09",
 * "Very Difficult Reading Exams 22", "... — Listening Sample Test 9"). The
 * backend returns one flat list across every series ordered by
 * `Priority DESC, PublishedAt DESC, Title ASC`, which is newest-first and
 * puts "10" before "9" — so the ordering has to happen where the papers are
 * grouped into folders, i.e. in `groupReadingExamPapers` /
 * `groupListeningExamPapers`.
 *
 * Titles are NEVER rewritten here — this module only decides order.
 *
 * NOTE — divergence from `paperHaystack()` in the two category modules:
 * `tagsCsv` is deliberately NOT consulted when extracting the number. Tags are
 * used for *category matching* only; a tag such as `2026` or `q37-42` would
 * poison the series number. Do not "fix" this by adding tagsCsv back in.
 *
 * Any FUTURE surface that renders an ungrouped flat paper list must call
 * `compareExamSeriesPapers` itself — the backend query is intentionally left
 * alone (see the plan / commit message for why).
 */

/**
 * Upper bound for a plausible series number. Real series run 1–22; the cap
 * exists so a year embedded in a slug (`...-2026-09-launch`) is skipped in
 * favour of the real number rather than sorting the paper to the end.
 */
const MAX_PLAUSIBLE_SERIES_NUMBER = 199;

export interface ExamSeriesOrderPaper {
  slug?: string | null;
  title?: string | null;
}

/** Mirrors the normalisation used by `paperHaystack` in the category modules. */
function normalise(value: string | null | undefined): string {
  return (value ?? '').toLowerCase().replace(/_+/g, '-');
}

/**
 * Removes the category's own matcher strings so the series name cannot be
 * mistaken for the exam number. Longest matcher first, so
 * `atlas-practice-series` is stripped before the shorter `atlas-practice`.
 * Literal `split`/`join` rather than a regex: matchers are arbitrary data
 * strings and would otherwise need escaping.
 */
function stripMatchers(text: string, matchers: readonly string[]): string {
  return [...matchers]
    .sort((left, right) => right.length - left.length)
    .reduce((acc, matcher) => (matcher ? acc.split(matcher).join(' ') : acc), text);
}

/**
 * The exam number written into the title (falling back to the slug), or null
 * when the paper carries no number at all.
 *
 * Works for both real title shapes because it only strips the series name and
 * then takes the first remaining integer:
 *   "Atlas Practice Series 09 — Head injuries"        -> 9
 *   "Atlas Practice Series — Listening Sample Test 9" -> 9
 */
export function extractSeriesNumber(
  paper: ExamSeriesOrderPaper,
  matchers: readonly string[],
): number | null {
  for (const source of [paper.title, paper.slug]) {
    const normalised = normalise(source);
    if (!normalised) continue;
    for (const match of stripMatchers(normalised, matchers).matchAll(/\d+/g)) {
      const parsed = Number.parseInt(match[0], 10);
      if (Number.isFinite(parsed) && parsed >= 0 && parsed <= MAX_PLAUSIBLE_SERIES_NUMBER) {
        return parsed;
      }
    }
  }
  return null;
}

/**
 * Comparator for the papers inside ONE series folder. Ascending by exam
 * number; unnumbered papers last; ties broken by numeric-aware title then slug
 * so the result is deterministic. `Array.prototype.sort` is stable, so exact
 * ties keep the order the API returned.
 */
export function compareExamSeriesPapers<T extends ExamSeriesOrderPaper>(
  matchers: readonly string[],
): (left: T, right: T) => number {
  return (left, right) => {
    const leftNumber = extractSeriesNumber(left, matchers) ?? Number.POSITIVE_INFINITY;
    const rightNumber = extractSeriesNumber(right, matchers) ?? Number.POSITIVE_INFINITY;
    if (leftNumber !== rightNumber) return leftNumber - rightNumber;

    const byTitle = (left.title ?? '').localeCompare(right.title ?? '', undefined, { numeric: true });
    if (byTitle !== 0) return byTitle;

    return (left.slug ?? '').localeCompare(right.slug ?? '', undefined, { numeric: true });
  };
}
