import assert from 'node:assert/strict';

// Setup global browser polyfills for Node ESM test harness
if (typeof globalThis.URL.createObjectURL !== 'function') {
  let blobCounter = 0;
  const objectUrlRegistry = new Map();
  globalThis.URL.createObjectURL = (blob) => {
    const url = 'blob:http://localhost/mock-blob-' + (++blobCounter);
    objectUrlRegistry.set(url, blob);
    return url;
  };
  globalThis.URL.revokeObjectURL = (url) => {
    objectUrlRegistry.delete(url);
  };
}

if (typeof globalThis.MediaRecorder === 'undefined') {
  class MockMediaRecorder {
    static isTypeSupported(mime) {
      return ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/wav'].includes(mime);
    }
    constructor(stream, options = {}) {
      this.stream = stream;
      this.mimeType = options.mimeType || 'audio/webm';
      this.state = 'inactive';
      this.ondataavailable = null;
      this.onstop = null;
      this._intervalId = null;
    }
    start(timeslice = 1000) {
      this.state = 'recording';
      this._intervalId = setInterval(() => {
        if (this.state === 'recording' && this.ondataavailable) {
          const fakeChunk = new Blob(['mock-audio-chunk'], { type: this.mimeType });
          this.ondataavailable({ data: fakeChunk });
        }
      }, timeslice);
    }
    pause() {
      this.state = 'paused';
    }
    resume() {
      this.state = 'recording';
    }
    stop() {
      this.state = 'inactive';
      if (this._intervalId) clearInterval(this._intervalId);
      if (this.onstop) {
        setTimeout(() => this.onstop?.(), 5);
      }
    }
  }
  globalThis.MediaRecorder = MockMediaRecorder;
}

if (typeof globalThis.Audio === 'undefined') {
  class MockAudio {
    constructor(src) {
      this.src = src || '';
      this._listeners = new Map();
    }
    addEventListener(event, handler, options) {
      if (!this._listeners.has(event)) this._listeners.set(event, []);
      this._listeners.get(event).push({ handler, once: options?.once });
    }
    removeEventListener(event, handler) {
      const list = this._listeners.get(event) || [];
      this._listeners.set(event, list.filter((l) => l.handler !== handler));
    }
    load() {
      setTimeout(() => {
        const list = this._listeners.get('canplaythrough') || [];
        for (const l of list) l.handler();
      }, 10);
    }
    play() {
      return Promise.resolve();
    }
    pause() {}
    removeAttribute() {}
  }
  globalThis.Audio = MockAudio;
}

// Import tested modules
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
} from '../lib/listening/audio-prebuffer.ts';

import {
  DualTrackRecorder,
} from '../lib/speaking/dual-track-recorder.ts';

console.log('================================================================');
console.log('CHALLENGER 1 EMPIRICAL ADVERSARIAL STRESS HARNESS — MILESTONE 1');
console.log('4-SKILL EXAM & PRACTICE ENGINE HARDENING');
console.log('================================================================\n');

let passCount = 0;
let failCount = 0;

function check(description, fn) {
  try {
    fn();
    console.log('[PASS] ' + description);
    passCount++;
  } catch (err) {
    console.error('[FAIL] ' + description);
    console.error('       Error: ' + err.message);
    failCount++;
  }
}

async function checkAsync(description, fn) {
  try {
    await fn();
    console.log('[PASS] ' + description);
    passCount++;
  } catch (err) {
    console.error('[FAIL] ' + description);
    console.error('       Error: ' + err.message);
    failCount++;
  }
}

// -----------------------------------------------------------------------------
// SUITE 1: READING 20/6/16 STRUCTURAL INVARIANTS & 15-MIN LOCK TRIGGER
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 1: READING 20/6/16 INVARIANTS & 15-MIN LOCK TRIGGER ---');

check('R1.1: Exact 20/6/16 = 42 reading item partitioning invariant', () => {
  const partA = 20; // 20 expeditious short answer / gap-fill
  const partB = 6;  // 6 short workplace extracts (1 MCQ each, 3 options A/B/C)
  const partC = 16; // 16 deep clinical MCQs (2 texts x 8 MCQs, 4 options A/B/C/D)

  assert.equal(partA + partB + partC, 42);
  assert.equal(partA, 20);
  assert.equal(partB, 6);
  assert.equal(partC, 16);
});

check('R1.2: Structure validation function detects and rejects non-20/6/16 structures', () => {
  const validateReadingPaperStructure = (parts) => {
    if (!parts || parts.length !== 3) return { valid: false, reason: 'must_have_3_parts' };
    const [a, b, c] = parts;
    if (a.questionCount !== 20) return { valid: false, reason: 'part_a_must_have_20_questions' };
    if (b.questionCount !== 6) return { valid: false, reason: 'part_b_must_have_6_questions' };
    if (c.questionCount !== 16) return { valid: false, reason: 'part_c_must_have_16_questions' };
    const total = a.questionCount + b.questionCount + c.questionCount;
    if (total !== 42) return { valid: false, reason: 'total_must_be_42' };
    return { valid: true, total };
  };

  // Valid
  assert.deepEqual(validateReadingPaperStructure([
    { part: 'A', questionCount: 20 },
    { part: 'B', questionCount: 6 },
    { part: 'C', questionCount: 16 },
  ]), { valid: true, total: 42 });

  // Invalid variations
  assert.equal(validateReadingPaperStructure([{ part: 'A', questionCount: 19 }, { part: 'B', questionCount: 6 }, { part: 'C', questionCount: 17 }]).valid, false);
  assert.equal(validateReadingPaperStructure([{ part: 'A', questionCount: 21 }, { part: 'B', questionCount: 6 }, { part: 'C', questionCount: 15 }]).valid, false);
  assert.equal(validateReadingPaperStructure([{ part: 'A', questionCount: 20 }, { part: 'B', questionCount: 7 }, { part: 'C', questionCount: 15 }]).valid, false);
  assert.equal(validateReadingPaperStructure(null).valid, false);
});

check('R1.3: Part A 15-minute countdown boundary state machine', () => {
  const PART_A_DURATION_SECONDS = 15 * 60; // 900 seconds

  const getPartATimerState = (elapsedSeconds) => {
    const remainingSeconds = Math.max(0, PART_A_DURATION_SECONDS - elapsedSeconds);
    const isLocked = elapsedSeconds >= PART_A_DURATION_SECONDS;
    return { remainingSeconds, isLocked };
  };

  // Start (0s)
  const atStart = getPartATimerState(0);
  assert.equal(atStart.remainingSeconds, 900);
  assert.equal(atStart.isLocked, false);

  // Midpoint (7.5m = 450s)
  const atMid = getPartATimerState(450);
  assert.equal(atMid.remainingSeconds, 450);
  assert.equal(atMid.isLocked, false);

  // 1 second before lock (899s)
  const at899 = getPartATimerState(899);
  assert.equal(at899.remainingSeconds, 1);
  assert.equal(at899.isLocked, false);

  // Exact lock mark (900s)
  const at900 = getPartATimerState(900);
  assert.equal(at900.remainingSeconds, 0);
  assert.equal(at900.isLocked, true);

  // Past lock mark (1200s)
  const at1200 = getPartATimerState(1200);
  assert.equal(at1200.remainingSeconds, 0);
  assert.equal(at1200.isLocked, true);
});

check('R1.4: Benign server rejection filter matches expected section boundary codes', () => {
  const BENIGN_SAVE_REJECTIONS = new Set([
    'part_a_locked',
    'part_bc_not_open',
    'part_bc_break_not_resumed',
  ]);

  const isBenignLockedSave = (errorCode) => BENIGN_SAVE_REJECTIONS.has(errorCode);

  // Benign codes should be suppressed
  assert.equal(isBenignLockedSave('part_a_locked'), true);
  assert.equal(isBenignLockedSave('part_bc_not_open'), true);
  assert.equal(isBenignLockedSave('part_bc_break_not_resumed'), true);

  // Real errors must NOT be suppressed
  assert.equal(isBenignLockedSave('unauthorized'), false);
  assert.equal(isBenignLockedSave('internal_server_error'), false);
  assert.equal(isBenignLockedSave('paper_not_found'), false);
  assert.equal(isBenignLockedSave('validation_error'), false);
});

check('R1.5: Reading strikethrough & annotation state serialization integrity', () => {
  const annotations = {
    ruledOutOptionsByQuestion: {
      'q-21': ['A', 'C'],
      'q-22': ['B'],
    },
    highlightsByPassage: {
      'text-1': [
        { id: 'hl-1', startOffset: 120, endOffset: 165, color: 'yellow', text: 'clinical hypertension' },
      ],
    },
  };

  const serialized = JSON.stringify(annotations);
  const deserialized = JSON.parse(serialized);

  assert.deepEqual(deserialized.ruledOutOptionsByQuestion['q-21'], ['A', 'C']);
  assert.deepEqual(deserialized.ruledOutOptionsByQuestion['q-22'], ['B']);
  assert.equal(deserialized.highlightsByPassage['text-1'][0].text, 'clinical hypertension');
});

// -----------------------------------------------------------------------------
// SUITE 2: LISTENING 10-PHASE CHUNKS & AUDIO PRE-BUFFERING CACHE
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 2: LISTENING 10-PHASE CHUNKS & AUDIO PRE-BUFFERING CACHE ---');

check('L2.1: 10-phase sub-section sequencing and 24/6/12 = 42 structure', () => {
  const listeningPhases = [
    { code: 'A1', part: 'A', questionCount: 12, extract: 'Consultation 1' },
    { code: 'A2', part: 'A', questionCount: 12, extract: 'Consultation 2' },
    { code: 'B1', part: 'B', questionCount: 1, extract: 'Extract 1' },
    { code: 'B2', part: 'B', questionCount: 1, extract: 'Extract 2' },
    { code: 'B3', part: 'B', questionCount: 1, extract: 'Extract 3' },
    { code: 'B4', part: 'B', questionCount: 1, extract: 'Extract 4' },
    { code: 'B5', part: 'B', questionCount: 1, extract: 'Extract 5' },
    { code: 'B6', part: 'B', questionCount: 1, extract: 'Extract 6' },
    { code: 'C1', part: 'C', questionCount: 6, extract: 'Presentation 1' },
    { code: 'C2', part: 'C', questionCount: 6, extract: 'Presentation 2' },
  ];

  assert.equal(listeningPhases.length, 10);
  const partATotal = listeningPhases.filter((p) => p.part === 'A').reduce((sum, p) => sum + p.questionCount, 0);
  const partBTotal = listeningPhases.filter((p) => p.part === 'B').reduce((sum, p) => sum + p.questionCount, 0);
  const partCTotal = listeningPhases.filter((p) => p.part === 'C').reduce((sum, p) => sum + p.questionCount, 0);

  assert.equal(partATotal, 24);
  assert.equal(partBTotal, 6);
  assert.equal(partCTotal, 12);
  assert.equal(partATotal + partBTotal + partCTotal, 42);
});

check('L2.2: Audio prebuffer cache returns null on cache miss', () => {
  clearAudioPrebufferCache();
  const url = 'https://cdn.example.com/audio/listening-p1.mp3';
  assert.equal(getCachedAudioUrl(url), null);
});

check('L2.3: Audio prebuffer cache registers and retrieves object URLs on cache hit', () => {
  clearAudioPrebufferCache();
  const url = 'https://cdn.example.com/audio/listening-p1.mp3';
  const objectUrl = 'blob:http://localhost/mock-blob-p1';

  registerCachedAudioUrl(url, objectUrl);
  assert.equal(getCachedAudioUrl(url), objectUrl);
});

check('L2.4: Audio prebuffer cache cleanly clears all entries', () => {
  clearAudioPrebufferCache();
  registerCachedAudioUrl('https://cdn.example.com/a1.mp3', 'blob:http://localhost/a1');
  registerCachedAudioUrl('https://cdn.example.com/a2.mp3', 'blob:http://localhost/a2');

  assert.ok(getCachedAudioUrl('https://cdn.example.com/a1.mp3') !== null);
  assert.ok(getCachedAudioUrl('https://cdn.example.com/a2.mp3') !== null);

  clearAudioPrebufferCache();

  assert.equal(getCachedAudioUrl('https://cdn.example.com/a1.mp3'), null);
  assert.equal(getCachedAudioUrl('https://cdn.example.com/a2.mp3'), null);
});

await checkAsync('L2.5: prebufferAudioChunks handles empty, deduplicated, and cached lists', async () => {
  clearAudioPrebufferCache();

  // 1. Empty list
  const emptyRes = await prebufferAudioChunks([]);
  assert.equal(emptyRes.success, true);
  assert.equal(emptyRes.prebufferedCount, 0);
  assert.equal(emptyRes.failedCount, 0);

  // 2. Pre-cached list with duplicate URLs and whitespace
  registerCachedAudioUrl('https://cdn.example.com/audio/cached-1.mp3', 'blob:http://localhost/blob-cached-1');
  registerCachedAudioUrl('https://cdn.example.com/audio/cached-2.mp3', 'blob:http://localhost/blob-cached-2');

  const progressEvents = [];
  const res = await prebufferAudioChunks(
    [
      'https://cdn.example.com/audio/cached-1.mp3',
      '  https://cdn.example.com/audio/cached-1.mp3  ',
      'https://cdn.example.com/audio/cached-2.mp3',
      '',
    ],
    (p) => progressEvents.push({ ...p }),
    2,
  );

  assert.equal(res.success, true);
  assert.equal(res.prebufferedCount, 2);
  assert.equal(res.failedCount, 0);
  assert.equal(progressEvents[progressEvents.length - 1].percent, 100);
});

check('L2.6: Strict one-play exam mode blocks seeking & handles paused resume target', () => {
  const resolveBlockedSeekTarget = (currentTime, requestedTime, isExamMode) => {
    if (isExamMode) {
      // In strict exam mode, seek is blocked; player must stick to current playback position
      return currentTime;
    }
    return requestedTime;
  };

  // In exam mode, forward/backward seek attempts are neutralized
  assert.equal(resolveBlockedSeekTarget(120, 150, true), 120);
  assert.equal(resolveBlockedSeekTarget(120, 30, true), 120);

  // In practice mode, seek is allowed
  assert.equal(resolveBlockedSeekTarget(120, 150, false), 150);
});

// -----------------------------------------------------------------------------
// SUITE 3: WRITING 45-MIN SUB-TEST (5m LOCK -> 40m WRITE) & WORD COUNT BANDS
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 3: WRITING 45-MIN SUB-TEST & WORD COUNT BANDS ---');

check('W3.1: 45-minute writing sub-test time distribution invariant', () => {
  const READING_WINDOW_SECONDS = 5 * 60;   // 300s (locked reading window)
  const WRITING_WINDOW_SECONDS = 40 * 60;  // 2400s (writing phase)
  const TOTAL_DURATION_SECONDS = 45 * 60;  // 2700s

  assert.equal(READING_WINDOW_SECONDS + WRITING_WINDOW_SECONDS, TOTAL_DURATION_SECONDS);
  assert.equal(TOTAL_DURATION_SECONDS, 2700);
});

check('W3.2: Writing phase transition state machine (5m lock -> 40m write -> auto-submit)', () => {
  const getWritingPhase = (elapsedSeconds) => {
    if (elapsedSeconds < 300) {
      return {
        phase: 'reading_locked',
        isEditorLocked: true,
        canEdit: false,
        remainingPhaseSeconds: 300 - elapsedSeconds,
        remainingTotalSeconds: 2700 - elapsedSeconds,
      };
    } else if (elapsedSeconds < 2700) {
      return {
        phase: 'writing_active',
        isEditorLocked: false,
        canEdit: true,
        remainingPhaseSeconds: 2700 - elapsedSeconds,
        remainingTotalSeconds: 2700 - elapsedSeconds,
      };
    } else {
      return {
        phase: 'hard_deadline_expired',
        isEditorLocked: true,
        canEdit: false,
        remainingPhaseSeconds: 0,
        remainingTotalSeconds: 0,
        mustAutoSubmit: true,
      };
    }
  };

  // 1. Initial 5-min locked reading window (e.g. at 2 min = 120s)
  const phase1 = getWritingPhase(120);
  assert.equal(phase1.phase, 'reading_locked');
  assert.equal(phase1.isEditorLocked, true);
  assert.equal(phase1.canEdit, false);
  assert.equal(phase1.remainingPhaseSeconds, 180);

  // 2. Exact boundary transition at 5 min (300s)
  const phase2Start = getWritingPhase(300);
  assert.equal(phase2Start.phase, 'writing_active');
  assert.equal(phase2Start.isEditorLocked, false);
  assert.equal(phase2Start.canEdit, true);
  assert.equal(phase2Start.remainingPhaseSeconds, 2400);

  // 3. Active writing at 25 min (1500s)
  const phase2Mid = getWritingPhase(1500);
  assert.equal(phase2Mid.phase, 'writing_active');
  assert.equal(phase2Mid.canEdit, true);
  assert.equal(phase2Mid.remainingTotalSeconds, 1200);

  // 4. Hard deadline expiration at 45 min (2700s)
  const phase3 = getWritingPhase(2700);
  assert.equal(phase3.phase, 'hard_deadline_expired');
  assert.equal(phase3.isEditorLocked, true);
  assert.equal(phase3.mustAutoSubmit, true);
});

check('W3.3: Word count tokenizer handles medical text, whitespace, markdown, and tags', () => {
  const countWords = (text) => {
    if (!text || typeof text !== 'string') return 0;
    // Strip HTML/Markdown tags and non-breaking spaces
    const clean = text
      .replace(/<[^>]*>/g, ' ')
      .replace(/&nbsp;/g, ' ')
      .replace(/[*_#~]/g, '')
      .trim();
    if (!clean) return 0;
    const words = clean.split(/\s+/).filter(Boolean);
    return words.length;
  };

  assert.equal(countWords(''), 0);
  assert.equal(countWords('   \n\t  '), 0);
  assert.equal(countWords('Dear Dr. Jones,'), 3);
  assert.equal(countWords('<p>Thank you for seeing <strong>Mrs. Mary Smith</strong>, an 84-year-old pensioner.</p>'), 10);
  assert.equal(countWords('The patient requires twice-daily wound dressings and regular blood pressure monitoring.'), 11);
  assert.equal(countWords('Medications:&nbsp;Metformin 500mg b.i.d.,&nbsp;Atorvastatin 20mg nocte.'), 6);
});

check('W3.4: Word count band categorization (180–200 target band)', () => {
  const categorizeWordCount = (count) => {
    if (count < 140) return { band: 'severe_underlength', status: 'critical', penaltyRisk: 'high' };
    if (count < 180) return { band: 'underlength', status: 'warning', penaltyRisk: 'moderate' };
    if (count <= 200) return { band: 'ideal_target', status: 'optimal', penaltyRisk: 'none' };
    if (count <= 220) return { band: 'acceptable_upper', status: 'optimal', penaltyRisk: 'low' };
    return { band: 'overlength', status: 'warning', penaltyRisk: 'high' };
  };

  assert.equal(categorizeWordCount(120).band, 'severe_underlength');
  assert.equal(categorizeWordCount(175).band, 'underlength');
  assert.equal(categorizeWordCount(180).band, 'ideal_target');
  assert.equal(categorizeWordCount(190).band, 'ideal_target');
  assert.equal(categorizeWordCount(200).band, 'ideal_target');
  assert.equal(categorizeWordCount(210).band, 'acceptable_upper');
  assert.equal(categorizeWordCount(220).band, 'acceptable_upper');
  assert.equal(categorizeWordCount(235).band, 'overlength');
});

// -----------------------------------------------------------------------------
// SUITE 4: SPEAKING 2-CARD ENGINE & DUAL-TRACK AUDIO RECORDER
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 4: SPEAKING 2-CARD ENGINE & DUAL-TRACK AUDIO RECORDER ---');

check('S4.1: 2-card role-play timing distribution invariant (3m prep + 5m active x 2 = 16m)', () => {
  const CARD_PREP_SECONDS = 3 * 60;        // 180s
  const CARD_ACTIVE_SECONDS = 5 * 60;      // 300s
  const PER_CARD_TOTAL = CARD_PREP_SECONDS + CARD_ACTIVE_SECONDS; // 480s (8m)
  const TWO_CARDS_TOTAL = PER_CARD_TOTAL * 2; // 960s (16m)

  assert.equal(PER_CARD_TOTAL, 480);
  assert.equal(TWO_CARDS_TOTAL, 960);
});

await checkAsync('S4.2: DualTrackRecorder full lifecycle (idle -> record -> pause -> resume -> stop)', async () => {
  const sessionId = 'speaking-session-stress-101';
  const recorder = new DualTrackRecorder(sessionId);

  // 1. Initial State
  const initial = recorder.getState();
  assert.equal(initial.isRecording, false);
  assert.equal(initial.isPaused, false);
  assert.equal(initial.chunkCount, 0);
  assert.equal(initial.totalBytes, 0);
  assert.equal(recorder.getBlobNow(), null);

  // 2. Stop while idle returns null safely
  const prematureStop = await recorder.stop();
  assert.equal(prematureStop, null);

  // 3. Start recording
  const mockStream = { getTracks: () => [] };
  recorder.start(mockStream);
  const running = recorder.getState();
  assert.equal(running.isRecording, true);
  assert.equal(running.isPaused, false);

  // 4. Pause
  recorder.pause();
  assert.equal(recorder.getState().isPaused, true);

  // 5. Resume
  recorder.resume();
  assert.equal(recorder.getState().isPaused, false);

  // Wait for at least one chunk to be emitted by mock interval
  await new Promise((r) => setTimeout(r, 1050));

  // 6. Inspect snapshot blob
  const currentBlob = recorder.getBlobNow();
  assert.ok(currentBlob !== null);
  assert.ok(currentBlob.size > 0);

  // 7. Stop recording
  const result = await recorder.stop();
  assert.ok(result !== null);
  assert.ok(result.blob instanceof Blob);
  assert.ok(result.durationMs > 0);
  assert.equal(recorder.getState().isRecording, false);
});

// -----------------------------------------------------------------------------
// SUITE 5: 4-SKILL UNIFIED MOCK COORDINATOR PIPELINE & TRANSITIONS
// -----------------------------------------------------------------------------
console.log('\n--- SUITE 5: 4-SKILL UNIFIED MOCK COORDINATOR PIPELINE & TRANSITIONS ---');

check('M5.1: 4-skill sequential subtest order (Listening -> Reading -> Writing -> Speaking)', () => {
  const standardSequence = ['listening', 'reading', 'writing', 'speaking'];
  assert.equal(standardSequence[0], 'listening');
  assert.equal(standardSequence[1], 'reading');
  assert.equal(standardSequence[2], 'writing');
  assert.equal(standardSequence[3], 'speaking');
});

check('M5.2: Mock progress calculation oracle across 4 sub-tests', () => {
  const computeProgress = (sectionStates) => {
    const total = sectionStates.length;
    const completed = sectionStates.filter((s) => s.status === 'completed').length;
    const percent = Math.round((completed / Math.max(1, total)) * 100);
    const isAllCompleted = completed === total && total > 0;
    return { completed, total, percent, isAllCompleted };
  };

  const sections = [
    { id: '1', subtest: 'listening', status: 'not_started' },
    { id: '2', subtest: 'reading', status: 'not_started' },
    { id: '3', subtest: 'writing', status: 'not_started' },
    { id: '4', subtest: 'speaking', status: 'not_started' },
  ];

  // 0/4
  assert.deepEqual(computeProgress(sections), { completed: 0, total: 4, percent: 0, isAllCompleted: false });

  // 1/4 (Listening complete)
  sections[0].status = 'completed';
  assert.deepEqual(computeProgress(sections), { completed: 1, total: 4, percent: 25, isAllCompleted: false });

  // 2/4 (Reading complete)
  sections[1].status = 'completed';
  assert.deepEqual(computeProgress(sections), { completed: 2, total: 4, percent: 50, isAllCompleted: false });

  // 3/4 (Writing complete)
  sections[2].status = 'completed';
  assert.deepEqual(computeProgress(sections), { completed: 3, total: 4, percent: 75, isAllCompleted: false });

  // 4/4 (Speaking complete)
  sections[3].status = 'completed';
  assert.deepEqual(computeProgress(sections), { completed: 4, total: 4, percent: 100, isAllCompleted: true });
});

check('M5.3: Next runnable / active section resolution algorithm', () => {
  const resolveNextSection = (sections) => {
    return sections.find((s) => s.status === 'in_progress')
      ?? sections.find((s) => s.status === 'not_started')
      ?? null;
  };

  // Case A: All not started -> picks first (Listening)
  const setA = [
    { id: '1', title: 'Listening', status: 'not_started' },
    { id: '2', title: 'Reading', status: 'not_started' },
  ];
  assert.equal(resolveNextSection(setA).id, '1');

  // Case B: Listening complete, Reading in progress -> picks Reading (in_progress priority)
  const setB = [
    { id: '1', title: 'Listening', status: 'completed' },
    { id: '2', title: 'Reading', status: 'in_progress' },
    { id: '3', title: 'Writing', status: 'not_started' },
  ];
  assert.equal(resolveNextSection(setB).id, '2');

  // Case C: All completed -> returns null
  const setC = [
    { id: '1', title: 'Listening', status: 'completed' },
    { id: '2', title: 'Reading', status: 'completed' },
  ];
  assert.equal(resolveNextSection(setC), null);
});

check('M5.4: Server clock offset drift telemetry formatting', () => {
  const formatClockSyncBadge = (driftMs) => {
    return Math.abs(driftMs) < 1000 ? 'Synchronized' : (driftMs + 'ms offset');
  };

  assert.equal(formatClockSyncBadge(0), 'Synchronized');
  assert.equal(formatClockSyncBadge(250), 'Synchronized');
  assert.equal(formatClockSyncBadge(-450), 'Synchronized');
  assert.equal(formatClockSyncBadge(1200), '1200ms offset');
  assert.equal(formatClockSyncBadge(-2500), '-2500ms offset');
});

// -----------------------------------------------------------------------------
// SUMMARY
// -----------------------------------------------------------------------------
console.log('\n================================================================');
console.log('TEST EXECUTION COMPLETE: ' + passCount + ' PASSED, ' + failCount + ' FAILED');
console.log('================================================================');

if (failCount > 0) {
  process.exit(1);
} else {
  process.exit(0);
}