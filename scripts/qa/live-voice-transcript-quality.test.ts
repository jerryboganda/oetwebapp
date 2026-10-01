import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  expectedLines,
  repeatedPatientSegments,
  scriptLines,
  speakerMatch,
  tokens,
  transcriptQuality,
  transcriptVerdict,
  wireText,
  wordErrorRate,
} from './live-voice-transcript-quality.mjs';

type Who = 'c' | 'p';
const seg = (who: Who, startMs: number, endMs: number, text: string) => ({
  speaker: who === 'c' ? 'candidate' : 'patient', startMs, endMs, text, confidence: null,
});
const scriptText = (name: string) => readFileSync(resolve(`scripts/qa/speaking-candidate-scripts/${name}.txt`), 'utf8');
const failing = (result: { checks: Record<string, boolean | null> }) => Object.entries(result.checks).filter(([, ok]) => ok === false).map(([name]) => name);

describe('live voice E2E: words and script lines', () => {
  it('lower-cases words, keeps a contraction whole and drops quotes and dashes', () => {
    expect(tokens('Doctor’s ‘quote’ — 5 mg.')).toEqual(["doctor's", 'quote', '5', 'mg']);
    expect(tokens("I'm sorry, didn't catch that")).toEqual(["i'm", 'sorry', "didn't", 'catch', 'that']);
    expect(tokens(null)).toEqual([]);
  });

  it('reads a script the way the workflow plays it: one sentence per line, [after=N] stripped, blanks skipped', () => {
    expect(scriptLines('[after=4] One.\n\n  Two  \r\n[after=10.5]Three\n')).toEqual(['One.', 'Two', 'Three']);
    const smoke = scriptLines(scriptText('smoke'));
    expect(smoke).toHaveLength(14);
    expect(smoke[0]).toBe('Good morning. My name is Doctor Smith, one of the doctors here. Please have a seat. How can I help you today?');
    expect(smoke[1]).toMatch(/^I'm sorry to hear that\. Could you tell me everything/);
    expect(smoke[13]).toBe('Thank you for coming in today. Please book a follow-up appointment, and take care.');
  });

  it('keeps the two per-card sentinel scripts identical to smoke except for the closing line, which differs by card', () => {
    const smoke = scriptLines(scriptText('smoke'));
    const a = scriptLines(scriptText('smoke-A'));
    const b = scriptLines(scriptText('smoke-B'));
    expect(a).toHaveLength(14);
    expect(b).toHaveLength(14);
    expect(a.slice(0, 13)).toEqual(smoke.slice(0, 13));
    expect(b.slice(0, 13)).toEqual(smoke.slice(0, 13));
    expect(tokens(a[13]).slice(-6)).toEqual(['the', 'end', 'of', 'the', 'first', 'consultation']);
    expect(tokens(b[13]).slice(-6)).toEqual(['the', 'end', 'of', 'the', 'second', 'consultation']);
  });

  it('word error rate counts substitutions, insertions and deletions over the reference length', () => {
    expect(wordErrorRate(['a', 'b', 'c'], ['a', 'x', 'c'])).toBeCloseTo(1 / 3);
    expect(wordErrorRate(['a', 'b'], ['a', 'b', 'c'])).toBe(0.5);
    expect(wordErrorRate(['a', 'b', 'c', 'd'], ['a', 'd'])).toBe(0.5);
    expect(wordErrorRate(['a'], [])).toBe(1);
    expect(wordErrorRate([], [])).toBe(0);
    expect(wordErrorRate([], ['a'])).toBe(1);
  });

  describe('expectedLines', () => {
    const script = ['one', 'two', 'three'];
    const timeline = [{ start: 6, end: 10 }, { start: 20, end: 25 }, { start: 40, end: 45 }];

    it('pairs each line with its time on the tape', () => {
      expect(expectedLines({ script, timeline })).toEqual([
        { text: 'one', start: 6, end: 10 }, { text: 'two', start: 20, end: 25 }, { text: 'three', start: 40, end: 45 },
      ]);
    });

    const texts = (lines: any[]) => lines.map((line) => line.text);

    it('keeps only lines played to the end', () => {
      expect(texts(expectedLines({ script, timeline, playedUntilS: 30 }))).toEqual(['one', 'two']);
    });

    it('drops a line that overlaps a window the run hid, but not one that only touches it', () => {
      expect(texts(expectedLines({ script, timeline, excludeS: [[22, 41]] }))).toEqual(['one']);
      expect(texts(expectedLines({ script, timeline, excludeS: [[10, 20]] }))).toEqual(['one', 'two', 'three']);
    });

    it('compares nothing when the script and the tape no longer match, or there is no script', () => {
      expect(expectedLines({ script, timeline: timeline.slice(0, 2) })).toEqual([]);
      expect(expectedLines({ script: [], timeline: [] })).toEqual([]);
      expect(expectedLines({ script, timeline: undefined })).toEqual([]);
    });
  });
});

describe('live voice E2E: transcript quality (Q1-Q11)', () => {
  const LINES = [
    { text: 'Good morning doctor', start: 6, end: 9 },
    { text: 'When did it start', start: 20, end: 23 },
    { text: 'Thank you take care', start: 40, end: 43 },
  ];
  const CLEAN = [
    seg('c', 1000, 4000, 'Good morning doctor'),
    seg('p', 4200, 9000, 'Hello there'),
    seg('c', 15000, 18000, 'When did it start'),
    seg('p', 18200, 25000, 'Yesterday morning'),
    seg('c', 35000, 38000, 'Thank you take care'),
    seg('p', 38200, 40000, 'Goodbye'),
  ];
  const run = (segments: unknown[], extra: Record<string, unknown> = {}): any => transcriptQuality({ segments, ...extra });

  it('passes a clean transcript on every check', () => {
    const r = run(CLEAN, { lines: LINES, closing: 'Thank you take care' });
    expect(r.problems).toEqual([]);
    expect(r.ok).toBe(true);
    expect(r.checks).toEqual({ q1: true, q2: true, q3: true, q4: true, q5: true, q6: true, q7: true, q8: true, q9: true, q10: true, q11: true });
    expect(r.stats.candidateSegments).toBe(3);
    expect(r.stats.alignedLines).toBe(3);
    expect(r.stats.alignmentSpreadS).toBe(0);
    expect(r.stats.wer).toBe(0);
  });

  it('fails a transcript with no segments', () => {
    expect(run([])).toEqual({ ok: false, checks: {}, problems: ['No segments were saved.'], stats: { segments: 0 } });
  });

  it('Q1: one speaker twice in a row less than 10 s apart is a merged or lost turn; a longer silence is not', () => {
    expect(run([seg('c', 1000, 4000, 'one'), seg('c', 5000, 6000, 'two'), seg('p', 6500, 7000, 'three')]).checks.q1).toBe(false);
    expect(run([seg('c', 1000, 4000, 'one'), seg('c', 20000, 22000, 'two'), seg('p', 22500, 24000, 'three')]).checks.q1).toBe(true);
  });

  it('Q2: a segment may start up to 1.5 s before the one ahead of it, not more', () => {
    expect(run([seg('c', 10000, 13000, 'one'), seg('p', 4000, 6000, 'two')]).checks.q2).toBe(false);
    expect(run([seg('c', 10000, 13000, 'one'), seg('p', 9000, 12000, 'two')]).checks.q2).toBe(true);
  });

  it('Q3: speakers may overlap by up to 1.5 s', () => {
    expect(run([seg('c', 1000, 9000, 'one'), seg('p', 4000, 6000, 'two')]).checks.q3).toBe(false);
    expect(run([seg('c', 1000, 5500, 'one'), seg('p', 4000, 6000, 'two')]).checks.q3).toBe(true);
  });

  it('Q4: fewer candidate segments than 0.75 x the played lines means lines were fused', () => {
    const r = run([seg('c', 1000, 4000, 'Good morning doctor'), seg('p', 4200, 9000, 'Hello there')], { lines: LINES });
    expect(r.checks.q4).toBe(false);
    expect(r.problems[0]).toBe('Q4: only 1 candidate segments for 3 played lines: lines were fused into one segment.');
  });

  it('Q5: lines that start a segment must sit at one constant offset from the tape', () => {
    const jumpy = [
      seg('c', 1000, 4000, 'Good morning doctor'),
      seg('p', 4200, 9000, 'Hello there'),
      seg('c', 30000, 33000, 'When did it start'),
      seg('p', 33200, 36000, 'Yesterday morning'),
      seg('c', 45000, 48000, 'Thank you take care'),
    ];
    const r = run(jumpy, { lines: LINES });
    expect(r.checks.q5).toBe(false);
    expect(r.stats.alignmentSpreadS).toBe(15);
  });

  it('Q6: one zero-length candidate segment in ten is tolerated, one in three is not', () => {
    expect(run([seg('c', 5000, 5000, 'one'), seg('p', 6000, 7000, 'two')]).checks.q6).toBe(false);
    const ten = Array.from({ length: 10 }, (_, i) => [seg('c', i * 10000, i * 10000 + 2000, `word${i}`), seg('p', i * 10000 + 3000, i * 10000 + 4000, `reply${i}`)]).flat();
    ten[0].endMs = ten[0].startMs;
    expect(run(ten).checks.q6).toBe(true);
  });

  it('Q7: every time must lie within 0..maxMs', () => {
    expect(run([seg('c', 1000, 340000, 'one')]).checks.q7).toBe(false);
    expect(run([seg('c', -1, 5, 'one')]).checks.q7).toBe(false);
    expect(run([seg('c', 0, 500000, 'one')], { maxMs: 600000 }).checks.q7).toBe(true);
  });

  it('Q8: words fused by a lost space are caught', () => {
    expect(run([seg('p', 0, 1000, 'Thanks, Doctor.Well, lately')]).checks.q8).toBe(false);
    expect(run([seg('p', 0, 1000, "but I don't think butI got over")]).checks.q8).toBe(false);
    expect(run([seg('p', 0, 1000, 'Doctor. Well, I am fine')]).checks.q8).toBe(true);
  });

  it('Q9: a segment longer than 30 s hides a silence; exactly 30 s is fine', () => {
    const r = run([seg('c', 0, 31000, 'one')]);
    expect(r.checks.q9).toBe(false);
    expect(r.problems).toContain('Q9: a segment lasts 31 s: a long silence is hidden inside it.');
    expect(run([seg('c', 0, 30000, 'one')]).checks.q9).toBe(true);
  });

  it('Q10: the candidate text must be the played script (word error rate <= 10%)', () => {
    const lost = CLEAN.map((s) => (s.startMs === 35000 ? { ...s, text: 'Thank you' } : s));
    const r = run(lost, { lines: LINES });
    expect(r.checks.q10).toBe(false);
    expect(r.stats.wer).toBeCloseTo(2 / 11);
  });

  it('Q11: the closing line must be said exactly once', () => {
    const closing = 'Please take care';
    expect(run([seg('c', 0, 1000, 'bye now see you again take care'), seg('p', 1100, 2000, 'ok')], { closing }).checks.q11).toBe(false);
    expect(run([seg('c', 0, 1000, 'Please take care'), seg('p', 1100, 2000, 'ok')], { closing }).checks.q11).toBe(true);
    const twice = [seg('c', 0, 1000, 'Please take care'), seg('p', 1100, 2000, 'ok'), seg('c', 20000, 21000, 'Please take care')];
    expect(run(twice, { closing }).checks.q11).toBe(false);
  });

  it('does not judge the script comparisons after a reload restarted the tape, or without script data', () => {
    const reloaded = run(CLEAN, { lines: LINES, closing: 'Thank you take care', tapeAligned: false });
    expect(reloaded.checks).toMatchObject({ q4: null, q5: null, q10: null, q11: null, q1: true, q9: true });
    expect(reloaded.ok).toBe(true);
    const unscripted = run(CLEAN);
    expect(unscripted.checks).toMatchObject({ q4: null, q5: null, q10: null, q11: null });
    expect(unscripted.ok).toBe(true);
  });

  // The two production transcripts below are real (candidate scripts smoke and compare, 30 Sep / 1 Oct 2026 runs).
  describe('golden: a clean OpenAI exam card (E1 Card A, saved before the long-pause split)', () => {
    const E1_TAPE = [
      { start: 6, end: 13.824 }, { start: 21.824, end: 29.376 }, { start: 33.376, end: 38.13 }, { start: 46.13, end: 48.954 },
      { start: 56.954, end: 59.836 }, { start: 84.836, end: 89.334 }, { start: 97.334, end: 102.657 }, { start: 106.657, end: 110.61 },
      { start: 118.61, end: 122.479 }, { start: 167.479, end: 173.069 }, { start: 181.069, end: 185.867 }, { start: 193.867, end: 199.431 },
      { start: 207.431, end: 210.557 }, { start: 218.557, end: 224.414 },
    ];
    const E1_A = [
      seg('c', 1000, 8400, 'Good morning. My name is Doctor Smith. One of the doctors here. Please have a seat. How can I help you today'),
      seg('p', 8400, 15800, "Hi, um, So, my five year old daughter, she was bitten on the face by our friend's dog about two hours ago."),
      seg('c', 16800, 18200, "I'm sorry to hear that"),
      seg('p', 17600, 18200, 'Thank you.'),
      seg('c', 18600, 24000, 'Could you tell me everything that has happened from the very beginning in as much detail as you can'),
      seg('p', 23800, 27400, 'Okay. Um. So, we were staying with friends, and'),
      seg('c', 28400, 32600, 'Sorry to interrupt you there When exactly did this first start'),
      seg('p', 32400, 40400, 'It started, uh, about two hours ago. Um, they have a dog and, um, it bit her on the side of her face.'),
      seg('c', 41000, 43400, 'Is there anything that makes it better or worse'),
      seg('p', 43600, 51400, "Um, I mean, it's a small cut, but, you know, I washed it under cold water and put a sterile dressing on it and it seems"),
      seg('c', 52000, 54400, 'Let me just think about that for a moment'),
      seg('p', 54200, 54400, 'Sure.'),
      seg('c', 79800, 83800, 'Thank you for waiting. Have you had any other health problems recently'),
      seg('p', 83800, 84400, 'No, not really.'),
      seg('c', 92200, 97200, 'Is there anything in particular that is worrying you? Please tell me all about it'),
      seg('p', 97000, 101400, "Well, I'm really worried it's going to get infected. Dogs eat all sorts of filthy things, yeah."),
      seg('c', 101800, 105200, "Sorry, I didn't mean to cut you off. Please go on"),
      seg('p', 105200, 112200, "Oh sure. Um, I was just gonna say that our friends said their dog loves children, and I'm frustrated this even happened."),
      seg('c', 113600, 167600, 'Um, let me think about the best way to explain this Sorry for the long pause. Is it all right if I explain what I think may be going on'),
      seg('p', 167400, 167800, 'Yeah, please.'),
      seg('c', 176200, 180400, 'Based on what you have told me, I would like us to agree on a plan together'),
      seg('p', 180600, 182800, 'All right, uh what are you gonna do for the wound?'),
      seg('c', 188800, 194000, 'Just to check I explained clearly, could you tell me in your own words what you understand so far'),
      seg('p', 194000, 195400, "Uh, you haven't told me what it is yet,"),
      seg('c', 202200, 205000, 'Is there anything else you would like to ask me today'),
      seg('p', 205200, 209400, 'Yeah, uh how do I look after it, and what should I watch for in case it gets worse?'),
      seg('c', 213600, 219000, 'Thank you for coming in today. Please book a follow up appointment, and take care'),
      seg('p', 219400, 220400, 'Alright. Thank you, Doctor.'),
    ];

    it('passes everything except Q9: its 54 s candidate segment hides the 45 s silence the script plays', () => {
      const lines: any[] = expectedLines({ script: scriptLines(scriptText('smoke')), timeline: E1_TAPE });
      expect(lines).toHaveLength(14);
      const r = run(E1_A, { lines, closing: lines[13].text });
      expect(failing(r)).toEqual(['q9']);
      expect(r.ok).toBe(false);
      expect(r.problems).toEqual(['Q9: a segment lasts 54 s: a long silence is hidden inside it.']);
      expect(r.stats).toMatchObject({ segments: 28, candidateSegments: 14, patientSegments: 14, alignedLines: 12, zeroLengthCandidate: 0, longestSegmentS: 54 });
      expect(r.stats.alignmentSpreadS).toBeCloseTo(0.374, 3);
      expect(r.stats.wer).toBeLessThan(0.01);
    });
  });

  describe('golden: a corrupted recovery transcript (F1, OpenAI dropped at 60 s, saved before the time-base fix)', () => {
    const F1_TAPE = [
      { start: 6, end: 13.789 }, { start: 23.789, end: 28.357 }, { start: 38.357, end: 42.96 }, { start: 52.96, end: 58.64 },
      { start: 68.64, end: 74.552 }, { start: 84.552, end: 88.444 }, { start: 98.444, end: 101.616 }, { start: 111.616, end: 117.482 },
      { start: 127.482, end: 133.835 }, { start: 143.835, end: 154.487 }, { start: 164.487, end: 176.209 }, { start: 186.209, end: 196.376 },
      { start: 206.376, end: 212.509 }, { start: 230.509, end: 240.615 }, { start: 250.615, end: 256.332 },
    ];
    const F1 = [
      seg('c', 4600, 12000, 'Good morning. My name is Doctor Smith, one of the doctors here. Please have a seat. What brings you in today'),
      seg('p', 12200, 21800, "Thanks, Doctor.Well, lately I've been getting really bad stomach cramps and feeling sick after having milk or yogurt. And, honestly, I've also had a lot of"),
      seg('c', 22400, 26600, "I'm sorry to hear that. When did you first notice these symptoms"),
      seg('p', 26600, 31200, "Oh, it's been happening for a few weeks now, but it feels worse every time I eat something with dairy."),
      seg('c', 37000, 41200, 'Could you describe the stomach cramps for me? Where exactly do you feel them'),
      seg('p', 41600, 51000, "It's mostly around the middle of my belly, kind of a squeezing pain, and, yeah, I've also had a lot of gas and, some diarrhea after those"),
      seg('c', 51600, 56800, 'Have you also noticed any wind, bloating, or diarrhea after eating dairy foods Have you had any stomach problems recently, for example a stomach bug or gastroenteritis How long did that illness last, and have you recovered from it now Is it all right if I explain what I think is happening Just to check I explained clearly, could you tell me in your own words what you understand so far'),
      seg('p', 56600, 58200, "Yeah, quite a bit Yes actually. I had a really bad stomach bug not long ago. It lasted about three days. It was about three days and yeah, I got over it after that. Sure, please do. Well, you haven't told me what it is yet."),
      seg('c', 66400, 72200, 'Based on what you have told me, I think you have a condition called secondary lactose intolerance'),
      seg('p', 72400, 78400, "But I can't be lactose intolerant. No one in my family has that, and I've never had any problems with milk until now."),
      seg('c', 82600, 93000, 'When you had gastroenteritis, the infection irritated the lining of your gut. That lining makes an enzyme called lactase, which digests the sugar in milk'),
      seg('p', 94400, 102400, "Okay. So, you're saying my gut's kind of irritated from that bug, and that's why milk is bothering me right now, even though it never did before."),
      seg('c', 103400, 114800, 'Some people think it cannot be lactose intolerance if nobody in their family has it. Actually, this type is not inherited. It is caused by the recent infection'),
      seg('p', 115000, 120400, 'Okay, that makes a bit more sense. So, how long is this gonna last? I really miss my morning yogurt.'),
      seg('c', 125000, 134800, 'The good news is that it is usually temporary. It should settle within several weeks. In the meantime, it would help to cut down on milk and dairy'),
      seg('p', 135400, 139400, "Alright. That's a relief, honestly. Several weeks I can handle."),
    ];

    it('fails Q4 (five lines fused into one segment), Q5 (the timeline jumped by 60 s) and Q8 (Doctor.Well)', () => {
      const lines: any[] = expectedLines({ script: scriptLines(scriptText('compare')), timeline: F1_TAPE, playedUntilS: 200 });
      expect(lines).toHaveLength(12);
      const r = run(F1, { lines });
      expect(failing(r)).toEqual(['q4', 'q5', 'q8']);
      expect(r.ok).toBe(false);
      expect(r.problems[0]).toBe('Q4: only 8 candidate segments for 12 played lines: lines were fused into one segment.');
      expect(r.problems[1]).toContain('8 of 12 lines start a segment, at an offset spread of 59.9 s');
      expect(r.stats.alignmentSpreadS).toBeCloseTo(59.878, 3);
      expect(r.stats.wer).toBeLessThan(0.01);
    });
  });
});

describe('live voice E2E: the saved transcript against the provider wire', () => {
  const w = (at: number, who: 'candidate' | 'patient', text: string, provider = 'openai') => ({ at, who, text, provider });
  const WIRE = [
    w(1000, 'candidate', ' Good'), w(1100, 'candidate', ' morning'), w(1200, 'candidate', ' doctor'), w(1300, 'candidate', '.'),
    w(2000, 'patient', ' Hello'), w(2100, 'patient', ' there'), w(2200, 'patient', ' my'), w(2300, 'patient', ' friend'),
    w(2400, 'patient', ' how'), w(2500, 'patient', ' are'), w(2600, 'patient', ' you'),
  ];
  const SAVED = [seg('c', 0, 1000, 'Good morning doctor.'), seg('p', 1000, 3000, 'Hello there my friend how are you')];
  const TAPE = tokens('Good morning doctor when did it start thank you take care');
  const verdict = (over: Record<string, unknown> = {}): any => transcriptVerdict({ segments: SAVED, wire: WIRE, window: { from: 500, to: 5000 }, tape: TAPE, ...over });

  it('joins OpenAI deltas as they are and Gemini fragments with a space, inside the window, skipping excluded ranges', () => {
    expect(wireText(WIRE, { who: 'candidate' })).toBe(' Good morning doctor.');
    expect(wireText(WIRE, { who: 'candidate', from: 1100, to: 1300 })).toBe(' morning doctor');
    expect(wireText(WIRE, { who: 'candidate', exclude: [[1050, 1150]] })).toBe(' Good doctor.');
    expect(wireText([w(1, 'candidate', ' Hel'), w(2, 'candidate', 'lo')], { who: 'candidate' })).toBe(' Hello');
    expect(wireText([w(1, 'patient', 'Hi there.', 'gemini'), w(2, 'patient', 'How are you?', 'gemini')], { who: 'patient' })).toBe(' Hi there. How are you?');
  });

  it('measures how much of one speaker the saved text holds (recall) and how much of it the wire explains (precision)', () => {
    expect(speakerMatch({ segments: SAVED, wire: WIRE, who: 'patient', from: 0, to: 9999, exclude: [] })).toEqual({ wire: 7, saved: 7, recall: 1, precision: 1 });
    const half = [seg('p', 1000, 3000, 'Hello there my friend')];
    expect(speakerMatch({ segments: half, wire: WIRE, who: 'patient', from: 0, to: 9999, exclude: [] })).toEqual({ wire: 7, saved: 4, recall: 4 / 7, precision: 1 });
  });

  it('accepts a transcript that is what the wire sent, with the right labels and no foreign patient text', () => {
    const v = verdict();
    expect(v.problems).toEqual([]);
    expect(v.matchesWire).toBe(true);
    expect(v.labelsAreTape).toBe(true);
    expect(v.patientContained).toBe(true);
    expect(v.ok).toBe(true);
    expect(v.candidateInTape).toBe(1);
    expect(v.patientInTape).toBeCloseTo(1 / 7);
    expect(v.worstPatientContainment).toBe(1);
  });

  it('fails a patient segment that is not in this card (the other card, or a replay): precision halves and containment is 0', () => {
    const v = verdict({ segments: [...SAVED, seg('p', 3000, 5000, 'The cat sat on the mat today')] });
    expect(v.ok).toBe(false);
    expect(v.matchesWire).toBe(false);
    expect(v.patientContained).toBe(false);
    expect(v.worstPatientContainment).toBe(0);
    expect(v.problems.join('\n')).toContain('patient words: the saved transcript holds 100% of what the provider sent and 50% of it was sent.');
    expect(v.problems.join('\n')).toContain("a saved patient segment is only 0% the patient's words in this card's window");
  });

  it('fails a candidate text saved twice, which a words-were-seen check would miss', () => {
    const v = verdict({ segments: [...SAVED, seg('c', 5000, 6000, 'Good morning doctor.')] });
    expect(v.matchesWire).toBe(false);
    expect(v.speakers.candidate).toMatchObject({ wire: 3, saved: 6, recall: 1, precision: 0.5 });
  });

  it('fails swapped speaker labels', () => {
    const v = verdict({ segments: [seg('c', 0, 3000, 'Hello there my friend how are you'), seg('p', 3000, 4000, 'Good morning doctor.')] });
    expect(v.ok).toBe(false);
    expect(v.labelsAreTape).toBe(false);
    expect(v.matchesWire).toBe(false);
    expect(v.problems.join('\n')).toContain('speaker labels: 14% of the candidate text and 100% of the patient text is the tape');
  });

  it('does not count what the run itself hid from the app (a stalled provider), but does without the exclusion', () => {
    const wire = [...WIRE, w(5000, 'patient', ' eleven'), w(5100, 'patient', ' twelve'), w(5200, 'patient', ' thirteen'), w(5300, 'patient', ' fourteen')];
    expect(verdict({ wire, window: { from: 500, to: 9000 } }).matchesWire).toBe(false);
    expect(verdict({ wire, window: { from: 500, to: 9000 }, exclude: [[4900, 5400]] }).matchesWire).toBe(true);
  });

  it('only counts the words inside the card window', () => {
    const wire = [w(100, 'candidate', ' stale'), ...WIRE];
    expect(verdict({ wire }).matchesWire).toBe(true);
    expect(verdict({ wire, window: {} }).matchesWire).toBe(false);
  });

  it('fails a transcript with words but nothing on the wire, and judges nothing when both are empty', () => {
    expect(verdict({ wire: [] }).matchesWire).toBe(false);
    const nothing = transcriptVerdict({ segments: [], wire: [] });
    expect(nothing.ok).toBeNull();
    expect(nothing.matchesWire).toBeNull();
  });

  it('finds long patient sentences saved identically in both cards, but not short replies that legitimately repeat', () => {
    const cardA = [
      seg('p', 0, 1, 'Okay, thank you so much, doctor.'),
      seg('p', 2, 3, 'Well, I am really worried it is going to get infected.'),
      seg('c', 4, 5, 'Well I am really worried it is going to get infected'),
    ];
    const cardB = [seg('p', 0, 1, 'Okay thank you so much doctor'), seg('p', 2, 3, 'well i am really worried it is going to get infected')];
    expect(repeatedPatientSegments(cardA, cardB)).toEqual(['well i am really worried it is going to get infected']);
    expect(repeatedPatientSegments(cardA, cardB, 6)).toEqual(['okay thank you so much doctor', 'well i am really worried it is going to get infected']);
    expect(repeatedPatientSegments(cardA, [])).toEqual([]);
  });
});
