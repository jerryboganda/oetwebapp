/**
 * Semantic checks for Listening section audio (pure functions; input is ASR segments + ffmpeg silence map).
 * A segment is {start, end, text} in absolute seconds of the section file; a silence is {start, end}.
 * Levels: 'pass' | 'review' (uncertain, needs a human ear) | 'fail' (defect). Content is never modified.
 */

// ── text helpers ─────────────────────────────────────────────────────────────
export const norm = (t) => String(t ?? '').toLowerCase().replace(/[^a-z0-9\s']/g, ' ').replace(/\s+/g, ' ').trim();
export const tokens = (t) => norm(t).split(' ').filter(Boolean);
export const textIn = (segs, from = 0, to = Infinity) => segs.filter((s) => s.end > from && s.start < to).map((s) => s.text).join(' ');

function shingles(toks, n) {
  const set = new Set();
  for (let i = 0; i + n <= toks.length; i++) set.add(toks.slice(i, i + n).join(' '));
  return set;
}
/** Share of a's n-word shingles that also occur in b (0..1). Robust to ASR wording noise, catches re-encoded copies. */
export function containment(a, b, n = 4) {
  const A = shingles(tokens(a), n);
  if (A.size < 5) return 0;
  const B = shingles(tokens(b), n);
  let hit = 0;
  for (const s of A) if (B.has(s)) hit++;
  return hit / A.size;
}

// ── cues ─────────────────────────────────────────────────────────────────────
// ASR sometimes hears "Extract" as "Act" or "Track", so the question range ("questions 13 to 24") is an equal cue.
// Some sources (Atlas 12-15) never say "Extract Two": the second extract is introduced as
// "You will hear part of a consultation between ..." (still followed by its preparation pause).
export const CUE = {
  extractTwo: /\b(?:extract|act|track|extra)\s+(?:two|2|to|too|tu)\b/i,
  questionsA2: /\bquestions?\s+13\s*(?:to|-|–|through)\s*24\b/i,
  questionsC2: /\bquestions?\s+37\s*(?:to|-|–|through)\s*42\b/i,
  intro: /\byou (?:will )?hear (?:part of )?(?:a|an) (?:consultation|talk|interview|presentation|discussion|lecture|conversation)\b/i,
  endOfPartA: /\bend of part a\b/i,
};
export function findCue(segs, re, from = 0, to = Infinity) {
  return segs.find((s) => s.end > from && s.start < to && re.test(s.text)) ?? null;
}
const anyCue = (section) => new RegExp([CUE.extractTwo, section === 'C2' ? CUE.questionsC2 : CUE.questionsA2, CUE.intro].map((r) => r.source).join('|'), 'i');

// ── silence map helpers ──────────────────────────────────────────────────────
/** Merge silences separated by a click-sized gap (<= maxGap s): one preparation pause is often split by a tiny blip. */
export function mergeSilences(silences, maxGap = 0.3) {
  const out = [];
  for (const s of silences) {
    const last = out[out.length - 1];
    if (last && s.start - last.end <= maxGap) last.end = Math.max(last.end, s.end);
    else out.push({ start: s.start, end: s.end });
  }
  return out;
}
export function leadingSilence(silences) {
  const s = mergeSilences(silences)[0];
  return s && s.start <= 0.3 ? s.end : 0;
}
export function trailingSilence(silences, dur) {
  const m = mergeSilences(silences);
  const s = m[m.length - 1];
  return s && s.end >= dur - 0.3 ? +(dur - s.start).toFixed(2) : 0;
}
/** Longest silence overlapping [from, to] (clipped to the window). */
export function longestSilence(silences, from, to) {
  let best = 0;
  for (const s of mergeSilences(silences)) {
    const len = Math.min(s.end, to) - Math.max(s.start, from);
    if (len > best) best = len;
  }
  return +best.toFixed(2);
}

// ── checks ───────────────────────────────────────────────────────────────────
const res = (id, level, detail) => ({ id, level, detail });

/** Destination section head (A2/C2): starts at the Extract Two cue and keeps the full preparation window. */
export function checkDestinationHead(section, segs, silences, sourcePrep = null) {
  // Judge against the ORIGINAL recording's own prep pause when known (a few sources are shorter than the nominal 30 s / 90 s);
  // otherwise against the nominal window.
  const nominal = section === 'C2' ? 90 : 30;
  const prepPass = sourcePrep != null ? sourcePrep - 2.5 : section === 'C2' ? 75 : 24;
  const prepFail = sourcePrep != null ? sourcePrep * 0.75 : section === 'C2' ? 60 : 20;
  const out = [];
  const cue = findCue(segs, anyCue(section), 0, 75);
  if (!cue) {
    out.push(res('head_cue', 'fail', `no second-extract introduction in the first 75 s (leading silence ${leadingSilence(silences).toFixed(1)} s)`));
    return out;
  }
  out.push(res('head_cue', cue.start <= 12 ? 'pass' : 'review', `second-extract introduction at ${cue.start.toFixed(1)} s`));
  const prep = longestSilence(silences, cue.start, cue.start + (section === 'C2' ? 150 : 90));
  out.push(res('prep_window', prep >= prepPass ? 'pass' : prep >= prepFail ? 'review' : 'fail', `longest silence after the cue ${prep.toFixed(1)} s (${sourcePrep != null ? `original recording ${sourcePrep} s` : `nominal ${nominal} s`})`));
  return out;
}

/** Source section tail (A1/C1): must not still carry the next section's Extract Two introduction. */
export function checkSourceTail(segs, dur, section = 'A1') {
  const re = new RegExp([CUE.extractTwo, section === 'C1' ? CUE.questionsC2 : CUE.questionsA2, CUE.intro].map((r) => r.source).join('|'), 'i');
  const cue = findCue(segs, re, Math.max(0, dur - 120));
  return [cue
    ? res('next_intro_in_tail', 'fail', `next extract's introduction still inside this section at ${cue.start.toFixed(1)} s`)
    : res('next_intro_in_tail', 'pass', 'no next-section introduction in the last 120 s')];
}

/** Sibling pair (A1/A2 or C1/C2): same file, repeated previous-section speech, duplicate content in either direction. */
export function checkPair(src, dst) {
  const out = [];
  if (src.assetId && src.assetId === dst.assetId) out.push(res('same_asset', 'fail', 'both sections use the same media file'));
  else out.push(res('same_asset', 'pass', 'distinct media files'));
  const srcText = textIn(src.segs), dstText = textIn(dst.segs);
  if (!src.segs.length || !dst.segs.length) return [...out, res('duplicate_content', 'review', 'no transcript for one of the sections')];
  const dstInSrc = containment(dstText, srcText), srcInDst = containment(srcText, dstText);
  const worst = Math.max(dstInSrc, srcInDst);
  out.push(res('duplicate_content', worst >= 0.35 ? 'fail' : worst >= 0.15 ? 'review' : 'pass',
    `destination speech found in source ${(dstInSrc * 100).toFixed(0)}%, source speech found in destination ${(srcInDst * 100).toFixed(0)}%`));
  const headInTail = containment(textIn(dst.segs, 0, 90), textIn(src.segs, Math.max(0, src.dur - 150)));
  out.push(res('previous_speech_at_head', headInTail >= 0.3 ? 'fail' : 'pass', `destination first 90 s overlaps the source last 150 s by ${(headInTail * 100).toFixed(0)}%`));
  return out;
}

const HALLUCINATION = /^(you|thank you\.?|thanks\.?|bye\.?|\.+)$/i; // Whisper invents these over silence

/**
 * Trailing silence / abrupt end (clipping suspicion). Sources often end right after the last spoken word (no fade),
 * so a loud last 0.4 s only needs a human ear when the final transcribed sentence is incomplete.
 */
export function checkTail(silences, dur, tailMaxDb, segs = []) {
  const ts = trailingSilence(silences, dur);
  const out = [];
  if (ts > 12.5) out.push(res('tail_silence', 'review', `${ts.toFixed(1)} s of trailing silence (confirm it is intentional timing)`));
  else out.push(res('tail_silence', 'pass', `${ts.toFixed(1)} s trailing silence`));
  if (ts < 0.15 && tailMaxDb != null && tailMaxDb > -35) {
    const last = [...segs].reverse().find((s) => s.text && !HALLUCINATION.test(s.text.trim()));
    const text = last?.text.trim() ?? '';
    if (last && /[.?!]['")]*$/.test(text)) out.push(res('abrupt_end', 'pass', `ends right after a complete sentence (last 0.4 s peaks at ${tailMaxDb} dB)`));
    else out.push(res('abrupt_end', 'review', `ends abruptly and the last transcribed words are not a complete sentence (…"${text.slice(-45)}"); confirm against the source that nothing is clipped`));
  }
  return out;
}

/** Learner timer versus real audio length (the 00:00 auto-advance cuts the recording when timer < audio). */
export function checkTimer(timer, dur) {
  if (timer == null) return [res('timer_vs_audio', 'review', 'no timer found (player default 90 s would apply)')];
  const need = Math.ceil(dur);
  if (timer < need) return [res('timer_vs_audio', 'fail', `timer ${timer} s is shorter than the ${need} s audio (${need - timer} s cut off)`)];
  if (timer < need + 3) return [res('timer_vs_audio', 'review', `timer ${timer} s leaves under 3 s spare over ${need} s audio`)];
  return [res('timer_vs_audio', 'pass', `timer ${timer} s covers ${need} s audio`)];
}

export function overall(checks) {
  if (checks.some((c) => c.level === 'fail')) return 'fail';
  if (checks.some((c) => c.level === 'review')) return 'review';
  return 'pass';
}
