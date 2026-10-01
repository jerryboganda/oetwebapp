// Pure judgement of a SAVED live-voice transcript (what the grader reads), for speaking-live-voice-browser-e2e.mjs.
// No I/O, no browser: the workflow copies this file next to the E2E script, so keep it self-contained.
//
// Two questions, both answered from data the run already holds:
//   transcriptQuality  is the saved transcript well formed, ordered, timed and complete against the scripted candidate tape?
//                      (Q1-Q11 of the 1 Oct 2026 transcript audit, calibrated on 21 real production transcripts)
//   transcriptVerdict  does it equal what the provider actually sent on the wire, and are the speaker labels right, and
//                      does it hold nothing from the other card? (Q12 and the cross-card checks)
//
// Words are compared as multisets of lower-case tokens, so a sentence saved twice (a leak, a replayed card) lowers precision
// instead of hiding behind a "word was seen" test.

// Mirrors MAX_SAME_SPEAKER_GAP_MS in hooks/useSpeakingRealtimeVoice.ts: one speaker's fragments extend their last segment only
// when the gap is at most this long, so two segments of one speaker closer than this were wrongly left apart.
export const MAX_SAME_SPEAKER_GAP_MS = 10_000;
// A lower-case word glued to a capital ("butI") or a sentence end glued to the next sentence ("Doctor.Well"): a delta lost its space.
const FUSED_WORD = /[a-z]{2,}[A-Z]\w*|[a-z][.?!][A-Z][a-z]+/;

// Lower-case words; a contraction stays one word ("didn't"), a quote around a word is not part of it.
export const tokens = (text) => String(text ?? '').toLowerCase().replace(/’/g, "'").match(/[a-z0-9]+(?:'[a-z0-9]+)*/g) ?? [];

// The candidate script as the workflow plays it: one line per spoken sentence, "[after=N]" pauses stripped, blanks skipped.
export function scriptLines(text) {
  return String(text ?? '').split(/\r?\n/).map((line) => line.trim().replace(/^\[after=[0-9.]+\]\s*/, '')).filter(Boolean);
}

// The scripted lines a transcript should contain: played to the end (end <= playedUntilS) and not even partly inside a window
// where the run itself hid the conversation (a stalled or dropped provider link). script and timeline are parallel; any
// mismatch (a script edited after the tape was built) leaves nothing to compare instead of a wrong comparison.
//   timeline  [{ start, end }] seconds on the tape clock (the workflow's timeline.json)
//   excludeS  [[fromS, toS]] on the same clock
/** @param {any} input { script, timeline, playedUntilS, excludeS } */
export function expectedLines({ script, timeline, playedUntilS = Infinity, excludeS = [] }) {
  if (!script?.length || script.length !== timeline?.length) return [];
  return script
    .map((text, i) => ({ text, start: timeline[i].start, end: timeline[i].end }))
    .filter((line) => line.end <= playedUntilS && !excludeS.some(([from, to]) => line.end > from && line.start < to));
}

// Word-level Levenshtein distance over the reference length.
export function wordErrorRate(ref, hyp) {
  if (!ref.length) return hyp.length ? 1 : 0;
  let prev = Array.from({ length: hyp.length + 1 }, (_, j) => j);
  for (let i = 1; i <= ref.length; i += 1) {
    const cur = [i];
    for (let j = 1; j <= hyp.length; j += 1) {
      cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ref[i - 1] === hyp[j - 1] ? 0 : 1));
    }
    prev = cur;
  }
  return prev[hyp.length] / ref.length;
}

// Size of the multiset intersection: how many tokens of a the tokens of b can account for, repeats included.
function overlap(a, b) {
  const left = new Map();
  for (const token of b) left.set(token, (left.get(token) ?? 0) + 1);
  let hit = 0;
  for (const token of a) {
    const n = left.get(token) ?? 0;
    if (n > 0) { hit += 1; left.set(token, n - 1); }
  }
  return hit;
}

// How many times the token run `needle` occurs in `haystack`.
function countRun(haystack, needle) {
  let count = 0;
  for (let i = 0; i + needle.length <= haystack.length; i += 1) {
    if (needle.every((token, k) => haystack[i + k] === token)) count += 1;
  }
  return count;
}

const speakerOf = (segment) => String(segment?.speaker ?? segment?.role ?? '').toLowerCase();
const isCandidate = (segment) => speakerOf(segment) === 'candidate';
const isPatient = (segment) => speakerOf(segment) === 'patient';

/**
 * Well-formedness, ordering, timing and completeness of one saved transcript.
 * @param {any} input
 *   segments      the saved segments [{ speaker, startMs, endMs, text }]
 *   lines         scripted lines expected in this transcript, [{ text, start, end }] (see expectedLines)
 *   closing       the script's last line when it is expected here (the sentinel: said once, so a card that saved twice or
 *                 lost its end shows), else null
 *   maxMs         the card plus its hard stop; no time may exceed it
 *   tapeAligned   false when the tape restarted (a page reload): the comparisons against the script do not apply
 * Returns { ok, checks, problems, stats }. checks:
 *   Q1 turns kept (no two segments of one speaker closer than 10 s)   Q2 starts non-decreasing (1.5 s tolerance)
 *   Q3 opposite-speaker overlap <= 1.5 s   Q4 candidate segments >= 0.75 x lines   Q5 every line that starts a segment sits
 *   at one constant offset from the tape (spread <= 2 s) and >= 70% of lines start one   Q6 at most 10% zero-length
 *   candidate segments   Q7 all times within 0..maxMs   Q8 no fused words   Q9 no segment longer than 30 s   Q10 candidate
 *   word error rate vs the played script <= 0.10   Q11 the closing line appears exactly once
 *   A check is null when it is not judged (no script data, or the tape restarted).
 */
export function transcriptQuality(input) {
  const { segments, lines = [], closing = null, maxMs = 330_000, tapeAligned = true } = input;
  const segs = Array.isArray(segments) ? segments : [];
  if (!segs.length) return { ok: false, checks: {}, problems: ['No segments were saved.'], stats: { segments: 0 } };

  const start = (s) => Number(s.startMs) || 0;
  const end = (s) => Number(s.endMs) || 0;
  const cand = segs.filter(isCandidate);
  const pairs = segs.slice(1).map((s, i) => [segs[i], s]);
  const firstBad = (isBad) => pairs.findIndex(isBad);
  const q1At = firstBad(([a, b]) => speakerOf(a) === speakerOf(b) && start(b) - end(a) <= MAX_SAME_SPEAKER_GAP_MS);
  const q2At = firstBad(([a, b]) => start(b) < start(a) - 1_500);
  const q3At = firstBad(([a, b]) => speakerOf(a) !== speakerOf(b) && end(a) - start(b) > 1_500);

  const scripted = tapeAligned && lines.length > 0;
  const candTokens = tokens(cand.map((s) => s.text).join(' '));
  const wer = scripted ? wordErrorRate(lines.flatMap((l) => tokens(l.text)), candTokens) : null;
  const keyOf = (text) => tokens(text).slice(0, 4).join(' ');
  const residualsS = scripted
    ? lines.flatMap((line) => {
      const seg = cand.find((s) => keyOf(s.text) === keyOf(line.text));
      return seg ? [start(seg) / 1000 - line.start] : [];
    })
    : [];
  const spreadS = residualsS.length ? Math.max(...residualsS) - Math.min(...residualsS) : null;
  const zeroLength = cand.filter((s) => end(s) <= start(s)).length;
  const longestS = Math.max(...segs.map((s) => end(s) - start(s))) / 1000;
  const needle = closing ? tokens(closing).slice(-6) : [];

  const rows = [
    ['q1', q1At === -1, `segments ${q1At} and ${q1At + 1} are by the same speaker less than 10 s apart: a turn was merged or lost between them.`],
    ['q2', q2At === -1, `segment ${q2At + 1} starts before the one ahead of it: the transcript is out of order.`],
    ['q3', q3At === -1, `segments ${q3At} and ${q3At + 1} overlap by more than 1.5 s.`],
    ['q4', scripted ? cand.length >= 0.75 * lines.length : null, `only ${cand.length} candidate segments for ${lines.length} played lines: lines were fused into one segment.`],
    ['q5', scripted ? residualsS.length >= 0.7 * lines.length && spreadS <= 2 : null, `${residualsS.length} of ${lines.length} lines start a segment, at an offset spread of ${spreadS === null ? 'n/a' : spreadS.toFixed(1)} s from the tape (max 2 s): the timeline jumped.`],
    ['q6', cand.length ? zeroLength <= 0.1 * cand.length : null, `${zeroLength} of ${cand.length} candidate segments have no duration.`],
    ['q7', segs.every((s) => start(s) >= 0 && end(s) <= maxMs), `a segment lies outside 0..${maxMs} ms.`],
    ['q8', segs.every((s) => !FUSED_WORD.test(String(s.text ?? ''))), 'a segment holds words fused together (a delta lost its space).'],
    ['q9', segs.every((s) => end(s) - start(s) <= 30_000), `a segment lasts ${longestS.toFixed(0)} s: a long silence is hidden inside it.`],
    ['q10', wer === null ? null : wer <= 0.1, `the candidate text differs from the played script by ${wer === null ? 'n/a' : (wer * 100).toFixed(1)}% of its words (max 10%).`],
    ['q11', tapeAligned && needle.length ? countRun(candTokens, needle) === 1 : null, 'the closing line is not said exactly once.'],
  ];
  return {
    ok: !rows.some(([, ok]) => ok === false),
    checks: Object.fromEntries(rows.map(([name, ok]) => [name, ok])),
    problems: rows.filter(([, ok]) => ok === false).map(([name, , text]) => `${name.toUpperCase()}: ${text}`),
    stats: {
      segments: segs.length, candidateSegments: cand.length, patientSegments: segs.filter(isPatient).length,
      wer, alignedLines: residualsS.length, alignmentSpreadS: spreadS, zeroLengthCandidate: zeroLength, longestSegmentS: longestS,
    },
  };
}

// One speaker's words on the wire in a time window, as the hook builds a segment: OpenAI deltas join as they are, Gemini
// fragments are separated by a space. Words inside an exclude window (epoch ms ranges) are skipped.
//   words  [{ at, who, text, provider }] at = epoch ms, provider 'openai' | 'gemini'
/**
 * @param {any} words
 * @param {any} options { who, from, to, exclude }
 */
export function wireText(words, { who, from = -Infinity, to = Infinity, exclude = [] }) {
  let text = '';
  for (const w of words ?? []) {
    if (w.who !== who || w.at < from || w.at >= to || exclude.some(([a, b]) => w.at >= a && w.at <= b)) continue;
    text += (w.provider === 'gemini' ? ' ' : '') + (w.text ?? '');
  }
  return text;
}

/**
 * How much of one speaker's wire words (in a window) the saved transcript holds, and how much of the saved text the wire explains.
 * @param {any} input { segments, wire, who, from, to, exclude }: see transcriptVerdict
 */
export function speakerMatch({ segments, wire, who, from, to, exclude }) {
  const savedTokens = tokens((segments ?? []).filter((s) => speakerOf(s) === who).map((s) => s.text).join(' '));
  const wireTokens = tokens(wireText(wire, { who, from, to, exclude }));
  const hit = overlap(wireTokens, savedTokens);
  return {
    wire: wireTokens.length,
    saved: savedTokens.length,
    recall: wireTokens.length ? hit / wireTokens.length : null,
    precision: savedTokens.length ? hit / savedTokens.length : null,
  };
}

/**
 * The saved transcript against what the provider sent, for one card (or the one role-play).
 * @param {any} input
 *   segments  the saved segments
 *   wire      every provider word of the run, [{ at, who, text, provider }]
 *   window    { from, to } epoch ms of this card's live conversation
 *   exclude   [[from, to]] epoch ms where the run itself hid the conversation from the app (a stall): the wire saw it, the app did not
 *   tape      tokens of the candidate script, to tell whose words carry which label
 *   minMatch  recall and precision floor per speaker (default 0.95)
 * Returns { ok, matchesWire, labelsAreTape, patientContained, speakers, candidateInTape, patientInTape,
 * worstPatientContainment, problems }; ok is null when nothing could be judged. A part is null when not judged.
 *   matchesWire       every speaker with words recalls and explains >= minMatch of them (Q12)
 *   labelsAreTape     saved candidate words are the tape's (>= 0.9) and saved patient words are not (<= 0.6), so no label swap
 *   patientContained  every saved patient segment of >= 4 words has >= 0.9 of its words said by the patient on the wire in this
 *                     card's window: a segment from the other card would not (about 0.3-0.4 on real data)
 */
export function transcriptVerdict(input) {
  const { segments, wire, window = {}, exclude = [], tape = null, minMatch = 0.95 } = input;
  const segs = Array.isArray(segments) ? segments : [];
  const where = { segments: segs, wire, from: window.from ?? -Infinity, to: window.to ?? Infinity, exclude };
  const speakers = { candidate: speakerMatch({ ...where, who: 'candidate' }), patient: speakerMatch({ ...where, who: 'patient' }) };
  const judged = Object.entries(speakers).filter(([, s]) => s.wire > 0 || s.saved > 0);
  const matchesWire = judged.length ? judged.every(([, s]) => (s.recall ?? 0) >= minMatch && (s.precision ?? 0) >= minMatch) : null;

  const tapeSet = new Set(tape ?? []);
  const shareInTape = (pick) => {
    const own = tokens(segs.filter(pick).map((s) => s.text).join(' '));
    return own.length ? own.filter((t) => tapeSet.has(t)).length / own.length : null;
  };
  const candidateInTape = tape ? shareInTape(isCandidate) : null;
  const patientInTape = tape ? shareInTape(isPatient) : null;
  const labelsAreTape = tape ? candidateInTape !== null && candidateInTape >= 0.9 && (patientInTape === null || patientInTape <= 0.6) : null;

  const patientWords = new Set(tokens(wireText(wire, { who: 'patient', from: where.from, to: where.to, exclude })));
  const containment = segs.filter(isPatient)
    .map((s) => tokens(s.text))
    .filter((t) => t.length >= 4)
    .map((t) => t.filter((token) => patientWords.has(token)).length / t.length);
  const worstPatientContainment = containment.length ? Math.min(...containment) : null;
  const patientContained = worstPatientContainment === null ? null : worstPatientContainment >= 0.9;

  const parts = [['matchesWire', matchesWire], ['labelsAreTape', labelsAreTape], ['patientContained', patientContained]];
  const pct = (v) => (v === null || v === undefined ? 'n/a' : `${(v * 100).toFixed(0)}%`);
  const problems = [
    ...(matchesWire === false ? Object.entries(speakers).filter(([, s]) => (s.recall ?? 0) < minMatch || (s.precision ?? 0) < minMatch)
      .map(([who, s]) => `${who} words: the saved transcript holds ${pct(s.recall)} of what the provider sent and ${pct(s.precision)} of it was sent.`) : []),
    ...(labelsAreTape === false ? [`speaker labels: ${pct(candidateInTape)} of the candidate text and ${pct(patientInTape)} of the patient text is the tape's (want >= 90% and <= 60%).`] : []),
    ...(patientContained === false ? [`a saved patient segment is only ${pct(worstPatientContainment)} the patient's words in this card's window (want >= 90%): text from another card or session.`] : []),
  ];
  const decided = parts.filter(([, v]) => v !== null);
  return {
    ok: decided.length ? decided.every(([, v]) => v === true) : null,
    matchesWire, labelsAreTape, patientContained, speakers, candidateInTape, patientInTape, worstPatientContainment, problems,
  };
}

// Patient sentences (>= minTokens words) saved identically in both cards of one exam: the same conversation was saved twice.
// Short replies ("Okay, thank you.") legitimately repeat, so only long ones count.
export function repeatedPatientSegments(segmentsA, segmentsB, minTokens = 8) {
  const normal = (s) => tokens(s.text).join(' ');
  const longEnough = (text) => text.split(' ').length >= minTokens;
  const seen = new Set((segmentsA ?? []).filter(isPatient).map(normal).filter(longEnough));
  return (segmentsB ?? []).filter(isPatient).map(normal).filter((text) => seen.has(text));
}
