/**
 * Recall / vocabulary example-sentence guard (§3A).
 *
 * Authoring scripts, seeds and the AI-gloss fallback have all historically been
 * able to emit *template* sentences that merely name the target word instead of
 * demonstrating it, e.g.
 *
 *   "The term dyspnoea was reviewed as part of OET vocabulary practice."
 *   "The patient's notes referenced dyspnoea."   // VocabularyGlossService fallback
 *   "Example using dyspnoea."
 *
 * On a recall card the same sentence then repeats on every word, so the line
 * carries no information and reads as noise. Spec §3A: if a useful example
 * sentence exists, show it; otherwise render nothing at all — never fabricate
 * filler, and never call an LLM at render time to produce one.
 *
 * Fail-closed like `lib/listening-question-clean.ts`: only text that is provably
 * a placeholder/template, known junk, a bare echo of the word, or a sentence
 * shared verbatim by several different words is suppressed. Legitimate clinical
 * sentences pass through untouched.
 */

/** Sentence shapes that are templates rather than real examples. */
const FILLER_SENTENCE_RES: readonly RegExp[] = [
  // "The term <word> was reviewed as part of OET vocabulary practice."
  /\bwas reviewed as part of\b/i,
  /\bas part of\b[^.]*\bvocabulary practice\b/i,
  // VocabularyGlossService deterministic fallback: "The patient's notes referenced <word>."
  /\bnotes?\s+referenced\b/i,
  // "Example using <word>." / "Example sentence for <word>." / "Example of <word>."
  /^\s*example\s+(?:using|sentence|of|for)\b/i,
  /^\s*practice sentence\b/i,
  // "<word> is a word in the OET vocabulary list."
  /\bis\s+(?:a|an)\s+(?:word|term)\s+(?:in|from|on)\b/i,
];

/** Unsubstituted template tokens left behind by a generator. */
const PLACEHOLDER_TOKEN_RE =
  /\[(?:word|term|target|answer)\]|\{\{?\s*(?:word|term|target|answer)\s*\}?\}|<(?:word|term|target|answer)>|%\s*s\b/i;

/** Explicit "this is not real content" markers. */
const JUNK_MARKER_RE =
  /\b(?:lorem ipsum|to be (?:written|confirmed|added)|coming soon|not available|placeholder|sample sentence|dummy text)\b/i;

/** "No example sentence available yet." / "Example sentence coming soon." */
const MISSING_SENTENCE_RE =
  /\b(?:no\s+example\s+sentence|example\s+sentence\s+(?:available|coming|pending|missing|not\b))/i;

/** The whole string is a non-answer. */
const JUNK_EXACT_RE = /^(?:n\/?a|tbd|tba|todo|none|null|undefined|unknown|-+|_+|\.+)$/i;

function normalise(raw: string | null | undefined): string {
  return String(raw ?? '')
    .replace(/\s+/g, ' ')
    .trim();
}

/** Strips surrounding straight/curly quotes so quoted examples compare cleanly. */
function stripWrappingQuotes(text: string): string {
  return text.replace(/^["'\u201C\u2018]+/, '').replace(/["'\u201D\u2019]+$/, '').trim();
}

/**
 * True when `raw` is a usable example sentence for `term`.
 *
 * Returns false for blank text, unsubstituted template tokens, known filler
 * templates, junk markers, and for text that is nothing but the word itself.
 */
export function isUsefulExampleSentence(
  term: string | null | undefined,
  raw: string | null | undefined,
): boolean {
  const text = normalise(raw);
  if (!text) return false;

  // A real example is at least a few words long. "Dyspnoea." is not an example.
  const body = stripWrappingQuotes(text);
  if (!body) return false;
  if (body.split(' ').length < 3) return false;

  if (PLACEHOLDER_TOKEN_RE.test(body)) return false;
  if (JUNK_EXACT_RE.test(body)) return false;
  if (JUNK_MARKER_RE.test(body)) return false;
  if (MISSING_SENTENCE_RE.test(body)) return false;
  for (const re of FILLER_SENTENCE_RES) {
    if (re.test(body)) return false;
  }

  // Bare echo of the target word, optionally with punctuation or a trailing
  // article — e.g. "dyspnoea", "Dyspnoea.", "the term dyspnoea".
  const word = normalise(term).toLowerCase();
  if (word) {
    const stripped = body
      .toLowerCase()
      .replace(/^(?:the\s+)?(?:word|term)\s+/, '')
      .replace(/[.!?,;:]+$/, '')
      .trim();
    if (stripped === word) return false;
  }

  return true;
}

/**
 * The example sentence to render, or `''` when there is nothing worth showing.
 * Callers can therefore use `{clean && <p>…</p>}` exactly as before.
 */
export function cleanExampleSentence(
  term: string | null | undefined,
  raw: string | null | undefined,
): string {
  const text = normalise(raw);
  if (!isUsefulExampleSentence(term, text)) return '';
  return stripWrappingQuotes(text);
}

/** Minimal shape needed to detect sentences reused across different words. */
export type ExampleSentenceCarrier = {
  term?: string | null;
  exampleSentence?: string | null;
};

function repeatKey(raw: string | null | undefined): string {
  return stripWrappingQuotes(normalise(raw)).toLowerCase();
}

/**
 * Keys (see `repeatKey`) of example sentences that appear for more than one
 * distinct term in `items`.
 *
 * An authored example is specific to its word, so a sentence shared verbatim by
 * several words is platform filler — this is what makes the §3A complaint
 * ("the same sentence on every card") detectable without hard-coding the exact
 * wording, so any future filler of the same kind is caught too.
 */
export function repeatedExampleSentenceKeys(
  items: readonly ExampleSentenceCarrier[],
): Set<string> {
  const seen = new Map<string, { count: number; terms: Set<string> }>();
  for (const item of items) {
    const key = repeatKey(item?.exampleSentence);
    if (!key) continue;
    const bucket = seen.get(key) ?? { count: 0, terms: new Set<string>() };
    bucket.count += 1;
    bucket.terms.add(normalise(item?.term).toLowerCase());
    seen.set(key, bucket);
  }

  const repeated = new Set<string>();
  for (const [key, bucket] of seen) {
    // Two occurrences of the same sentence on the *same* word is duplication,
    // not filler; it takes two different words to prove the text is generic.
    if (bucket.count > 1 && bucket.terms.size > 1) repeated.add(key);
  }
  return repeated;
}

/**
 * Convenience wrapper: `cleanExampleSentence` plus suppression of sentences that
 * repeat across the supplied list. Use this when rendering a whole word list;
 * use `cleanExampleSentence` for a single card.
 */
export function cleanExampleSentencesForList(
  items: readonly ExampleSentenceCarrier[],
): string[] {
  const repeated = repeatedExampleSentenceKeys(items);
  return items.map((item) => {
    const text = cleanExampleSentence(item?.term, item?.exampleSentence);
    if (!text) return '';
    return repeated.has(repeatKey(text)) ? '' : text;
  });
}
