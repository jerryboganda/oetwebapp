// Learner behaviour for the fleet harness. A "session" is one learner (or expert) living for the
// duration the timeline assigns: sign in, page-load burst, then a loop of think time (spent polling
// the SignalR hub like an idle browser tab) and one action per tick, with the learner's exam /
// speaking / room activity woven in at randomised moments.
//
// Flow outcomes feed the coverage counters; correctness checks (lost acknowledged saves, duplicate
// charges, work done while queued) feed the counters that must stay at zero.

import http from 'k6/http';
import { sleep } from 'k6';
import exec from 'k6/execution';
import { CFG, PARAMS, TIMELINE, roomByOrdinal } from './config.js';
import { ACTIONS, HUBS, fill } from './contract.js';
import { retryAfterSeconds } from './classify.mjs';
import { extractQuestionIds } from './extract.mjs';
import {
  call, elapsedSeconds, newSession, signInSession, sleepUntil, beginIteration,
} from './http.js';
import * as M from './metrics.js';
import { globalIndex, personaOf, roleOf, roomOrdinalOf } from './profiles.mjs';
import { hubClose, hubInvoke, hubAwaitCompletion, hubConnect, hubHold, hubReconnect } from './signalr.js';

// ---- small helpers ----------------------------------------------------------------------------------

const uniform = (min, max) => min + Math.random() * (max - min);
const pickAt = (list, index) => (list && list.length > 0 ? list[index % list.length] : null);

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16);
    return (c === 'x' ? r : (r % 4) + 8).toString(16);
  });
}

function weighted(items) {
  let total = 0;
  for (const item of items) total += item.w;
  let roll = Math.random() * total;
  for (const item of items) {
    roll -= item.w;
    if (roll <= 0) return item;
  }
  return items[items.length - 1];
}

const alive = (sess) => elapsedSeconds() < TIMELINE.endS(sess.g);

function begin(flow) { M.flowStarted.add(1, { flow }); }
function finish(flow) { M.flowCompleted.add(1, { flow }); }
function skip(flow, reason) {
  M.flowSkipped.add(1, { flow, reason });
  console.warn(`flow ${flow} skipped: ${reason}`);
}

const SENTENCES = [
  'The patient presented with a three day history of productive cough and fever.',
  'On examination the temperature was elevated and chest auscultation revealed coarse crackles.',
  'She has a background of well controlled type two diabetes mellitus and hypertension.',
  'Her regular medications include metformin and ramipril, and she has no known drug allergies.',
  'A chest radiograph was arranged and showed right lower zone consolidation.',
  'Oral antibiotics were commenced and she was advised to maintain adequate hydration.',
  'I would be grateful if you could review her in clinic within two weeks.',
  'Please do not hesitate to contact me should you require any further information.',
];
const WORDS = ['reading', 'listening', 'writing', 'speaking', 'vocabulary', 'grammar', 'mock', 'letter', 'practice', 'strategy'];

const wordCount = (text) => (text.trim() === '' ? 0 : text.trim().split(/\s+/).length);

/** Think time. While a hub connection is open the time is spent polling it (see signalr.js). */
export function think(sess, minS, maxS) {
  const seconds = uniform(minS, maxS) * CFG.thinkScale;
  if (sess.hub && !sess.hub.closed) {
    hubHold(sess, sess.hub, seconds);
    if (sess.hub.closed) reconnectHub(sess);
  } else {
    sleep(seconds);
    if (CFG.hubMode !== 'off' && sess.hubPath && !sess.hub) reconnectHub(sess);
  }
}

function reconnectHub(sess) {
  const now = Date.now();
  if (sess.nextHubTryMs !== undefined && now < sess.nextHubTryMs) {
    sess.hub = null;
    return;
  }
  sess.nextHubTryMs = now + 30000;
  sess.hub = hubReconnect(sess, sess.hubPath);
}

function openNotificationHub(sess) {
  if (CFG.hubMode === 'off') return;
  sess.hubPath = HUBS.notifications;
  sess.hub = hubConnect(sess, HUBS.notifications);
}

function closeHub(sess) {
  if (sess.hub) hubClose(sess, sess.hub);
  sess.hub = null;
}

// ---- shared mix -------------------------------------------------------------------------------------

const BROWSE_MIX = [
  { spec: ACTIONS.dashboard, w: 20 },
  { spec: ACTIONS.entitlement, w: 12 },
  { spec: ACTIONS.studyPlan, w: 10 },
  { spec: ACTIONS.readiness, w: 10 },
  { spec: ACTIONS.engagement, w: 10 },
  { spec: ACTIONS.notifications, w: 14, query: '?page=1&pageSize=20' },
  { spec: ACTIONS.search, w: 14, search: true },
  { spec: ACTIONS.progress, w: 5 },
  { spec: ACTIONS.subscription, w: 5 },
];

function browseTick(sess) {
  const pick = weighted(BROWSE_MIX);
  const options = {};
  if (pick.query) options.path = `${pick.spec.path}${pick.query}`;
  if (pick.search) options.path = `${pick.spec.path}?q=${encodeURIComponent(WORDS[Math.floor(Math.random() * WORDS.length)])}&page=1&pageSize=20`;
  call(sess, pick.spec, options);
}

/** What a browser does when the app opens: bootstrap first, then the landing-page reads. */
function pageLoad(sess) {
  const bootstrap = call(sess, ACTIONS.bootstrap);
  if (bootstrap.ok) sess.established = true;
  for (const spec of [ACTIONS.dashboard, ACTIONS.entitlement, ACTIONS.subscription, ACTIONS.notifications]) {
    sleep(uniform(0.1, 0.4));
    call(sess, spec, spec === ACTIONS.notifications ? { path: `${spec.path}?page=1&pageSize=20` } : {});
  }
  return bootstrap.ok;
}

// ---- credits ----------------------------------------------------------------------------------------

const CREDITS_PER_ACTIVITY = 2; // one Writing letter / one Speaking card (docs: AI credits, FINAL 2026-09-06)

function creditSnapshot(sess) {
  const r = call(sess, ACTIONS.credits, { path: `${ACTIONS.credits.path}?pageSize=50` });
  return r.ok ? r.json() : null;
}

/** Writing / Speaking pools combined (dedicated + flexible + shared); null for unlimited / unknown. */
function writingPool(snapshot, kind) {
  if (!snapshot || snapshot.writingUnlimited === true || snapshot.speakingUnlimited === true) return null;
  const dedicated = kind === 'writing' ? snapshot.writingOnlyCredits : snapshot.speakingOnlyCredits;
  const parts = [dedicated, snapshot.flexibleCredits, snapshot.sharedCredits];
  return parts.every((p) => typeof p === 'number') ? parts[0] + parts[1] + parts[2] : null;
}

function idempotencyViolation(flow, kind) {
  M.idempotencyViolation.add(1, { flow, kind });
  console.error(`idempotency violation: ${flow} ${kind}`);
}

// ---- Reading ----------------------------------------------------------------------------------------

function readingFlow(sess, content) {
  const paper = pickAt(content.reading, sess.g);
  if (paper === null || paper.partA.length === 0) return skip('reading', 'no_content');
  begin('reading');
  const startPath = fill(ACTIONS.readingStart.path, { id: paper.paperId });
  const start = call(sess, ACTIONS.readingStart, { path: startPath });
  if (!start.ok) return skip('reading', start.domain ? 'start_refused' : 'start_failed');
  const attemptId = (start.json() || {}).attemptId;
  if (!attemptId) return skip('reading', 'no_attempt_id');

  const acked = {};
  const questions = paper.partA.slice(0, CFG.readingSaves);
  for (let i = 0; i < questions.length && alive(sess); i += 1) {
    think(sess, 12, 30);
    const qid = questions[i];
    const value = JSON.stringify('ABCD'.charAt(Math.floor(Math.random() * 4)));
    const saved = call(sess, ACTIONS.readingSave, {
      path: fill(ACTIONS.readingSave.path, { id: attemptId, qid }),
      body: { userAnswerJson: value, elapsedMs: Math.floor(uniform(3000, 40000)) },
    });
    if (saved.ok) {
      acked[qid] = value;
      M.savesAcked.add(1, { flow: 'reading' });
    } else if (saved.domain) {
      break; // the answer window closed: the product answered correctly, stop saving
    }
    if (i > 2 && Math.random() < 0.1) {
      // change an earlier answer, as learners do; the latest acknowledged value is the truth
      const earlier = questions[Math.floor(Math.random() * i)];
      const changed = JSON.stringify('ABCD'.charAt(Math.floor(Math.random() * 4)));
      const again = call(sess, ACTIONS.readingSave, {
        path: fill(ACTIONS.readingSave.path, { id: attemptId, qid: earlier }),
        body: { userAnswerJson: changed, elapsedMs: Math.floor(uniform(1000, 8000)) },
      });
      if (again.ok) {
        acked[earlier] = changed;
        M.savesAcked.add(1, { flow: 'reading' });
      }
    }
  }

  const attemptPath = fill(ACTIONS.readingGetAttempt.path, { id: attemptId });
  const got = call(sess, ACTIONS.readingGetAttempt, { path: attemptPath });
  if (got.ok) {
    const stored = {};
    const answers = (got.json() || {}).answers || [];
    for (const answer of answers) stored[answer.readingQuestionId] = answer.userAnswerJson;
    for (const qid of Object.keys(acked)) {
      M.savesVerified.add(1, { flow: 'reading' });
      if (stored[qid] !== acked[qid]) {
        M.lostAckSave.add(1, { flow: 'reading' });
        console.error(`lost acknowledged Reading save: attempt ${attemptId} question ${qid}`);
      }
    }
  }

  if (!alive(sess)) return finish('reading');
  think(sess, 5, 15);
  const key = `rk-${uuid()}`;
  const submitOptions = { path: fill(ACTIONS.readingSubmit.path, { id: attemptId }), headers: { 'Idempotency-Key': key } };
  const first = call(sess, ACTIONS.readingSubmit, submitOptions);
  if (first.ok) {
    const replay = call(sess, ACTIONS.readingSubmit, submitOptions);
    if (replay.ok) {
      const a = first.json() || {};
      const b = replay.json() || {};
      if (a.rawScore !== b.rawScore || a.maxRawScore !== b.maxRawScore) idempotencyViolation('reading', 'submit_replay_differs');
    }
  }
  return finish('reading');
}

// ---- Listening --------------------------------------------------------------------------------------

function listeningFlow(sess, content) {
  const paperId = pickAt(content.listening, sess.g);
  if (paperId === null) return skip('listening', 'no_content');
  begin('listening');
  const session = call(sess, ACTIONS.listeningSession, { path: fill(ACTIONS.listeningSession.path, { id: paperId }) });
  if (!session.ok) return skip('listening', 'session_unavailable');
  const questionIds = extractQuestionIds(session.json());
  const start = call(sess, ACTIONS.listeningStart, {
    path: fill(ACTIONS.listeningStart.path, { id: paperId }),
    body: { mode: 'practice' },
  });
  if (!start.ok) return skip('listening', start.domain ? 'start_refused' : 'start_failed');
  const attemptId = (start.json() || {}).attemptId;
  if (!attemptId || questionIds.length === 0) return skip('listening', 'no_attempt_or_questions');
  for (let i = 0; i < Math.min(questionIds.length, CFG.listeningSaves) && alive(sess); i += 1) {
    think(sess, 10, 25);
    const saved = call(sess, ACTIONS.listeningSave, {
      path: fill(ACTIONS.listeningSave.path, { id: attemptId, qid: questionIds[i] }),
      body: { userAnswer: 'ABCD'.charAt(Math.floor(Math.random() * 4)) },
    });
    if (saved.ok) M.savesAcked.add(1, { flow: 'listening' });
    else if (saved.domain) break;
  }
  return finish('listening');
}

// ---- Writing ----------------------------------------------------------------------------------------

function writingFlow(sess, content) {
  const scenarioId = pickAt(content.writing, sess.g);
  if (scenarioId === null) return skip('writing', 'no_content');
  begin('writing');
  const before = writingPool(creditSnapshot(sess), 'writing');
  const eligibilityPath = fill(ACTIONS.writingEligibility.path, { id: scenarioId });
  const eligible = call(sess, ACTIONS.writingEligibility, { path: eligibilityPath });
  if (!eligible.ok) return skip('writing', eligible.domain ? 'not_eligible' : 'eligibility_failed');
  // A page refresh asks again: the open is idempotent on the scenario, so it must never charge twice.
  call(sess, ACTIONS.writingEligibility, { path: eligibilityPath });

  const draftPath = fill(ACTIONS.writingDraftSave.path, { id: scenarioId, mode: 'practice' });
  let letter = '';
  let version = null;
  let ackedContent = null;
  for (let i = 0; i < CFG.writingSaves && alive(sess); i += 1) {
    think(sess, 15, 35);
    letter += `${letter === '' ? '' : ' '}${SENTENCES[i % SENTENCES.length]}`;
    const saved = call(sess, ACTIONS.writingDraftSave, {
      path: draftPath,
      body: {
        content: letter,
        wordCount: wordCount(letter),
        timeSpentSeconds: Math.min(7200, Math.floor(elapsedSeconds() % 3000)),
        expectedVersion: version,
      },
    });
    if (saved.ok) {
      const body = saved.json() || {};
      if (typeof body.version === 'number') version = body.version;
      ackedContent = letter;
      M.savesAcked.add(1, { flow: 'writing' });
    } else if (saved.status === 409) {
      const current = call(sess, ACTIONS.writingDraftGet, { path: draftPath });
      if (current.ok) version = (current.json() || {}).version;
    }
  }

  if (ackedContent !== null) {
    const stored = call(sess, ACTIONS.writingDraftGet, { path: draftPath });
    if (stored.ok) {
      M.savesVerified.add(1, { flow: 'writing' });
      if ((stored.json() || {}).content !== ackedContent) {
        M.lostAckSave.add(1, { flow: 'writing' });
        console.error(`lost acknowledged Writing draft save: scenario ${scenarioId}`);
      }
    }
  }

  if (!alive(sess) || ackedContent === null) return finish('writing');
  think(sess, 5, 15);
  const body = {
    scenarioId,
    mode: 'practice',
    letterContent: letter,
    wordCount: wordCount(letter),
    timeSpentSeconds: Math.min(7200, Math.floor(elapsedSeconds() % 3000)),
    inputSource: 'editor',
    simulationMode: 'computer',
    idempotencyKey: `wk-${uuid()}`,
  };
  const first = call(sess, ACTIONS.writingSubmit, { body });
  if (first.ok) {
    const replay = call(sess, ACTIONS.writingSubmit, { body });
    if (replay.ok && (replay.json() || {}).id !== (first.json() || {}).id) {
      idempotencyViolation('writing', 'submit_replay_new_submission');
    }
    const submissionId = (first.json() || {}).id;
    if (submissionId) call(sess, ACTIONS.writingSubmissionGet, { path: fill(ACTIONS.writingSubmissionGet.path, { id: submissionId }) });
  }
  const after = writingPool(creditSnapshot(sess), 'writing');
  if (before !== null && after !== null && before - after > CREDITS_PER_ACTIVITY) {
    idempotencyViolation('writing', 'charged_more_than_once');
  }
  return finish('writing');
}

// ---- AI speaking ------------------------------------------------------------------------------------

const FAKE_SDP = 'v=0\r\no=- 4611731400430051336 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\nc=IN IP4 0.0.0.0\r\na=rtpmap:111 opus/48000/2\r\n';
const CONSENT_TYPES = ['recording', 'ai_processing', 'tutor_review', 'retention'];

function recordConsents(sess) {
  if (sess.consentsRecorded) return;
  for (const consentType of CONSENT_TYPES) call(sess, ACTIONS.speakingConsentRecord, { body: { consentType } });
  sess.consentsRecorded = true;
}

/** A tiny valid WAV (16 kHz mono PCM silence); the API accepts audio/wav for candidate audio. */
function wavBytes(seconds) {
  const samples = Math.floor(16000 * seconds);
  const data = samples * 2;
  const buffer = new ArrayBuffer(44 + data);
  const view = new DataView(buffer);
  const text = (offset, s) => { for (let i = 0; i < s.length; i += 1) view.setUint8(offset + i, s.charCodeAt(i)); };
  text(0, 'RIFF'); view.setUint32(4, 36 + data, true); text(8, 'WAVE'); text(12, 'fmt ');
  view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
  view.setUint32(24, 16000, true); view.setUint32(28, 32000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
  text(36, 'data'); view.setUint32(40, data, true);
  return buffer;
}
let cachedWav = null;

function looksQueued(r) {
  if (r.shed) return true;
  return r.status === 202 && (r.retryAfter !== null || /queue|wait/i.test(r.text));
}

function checkQueuedInvariants(sess, sessionId) {
  // While queued the learner must hold no credit for this session and the exam clock must not run.
  const snapshot = creditSnapshot(sess);
  const rows = snapshot && Array.isArray(snapshot.transactions) ? snapshot.transactions : [];
  for (const row of rows) {
    if (row && typeof row.referenceId === 'string' && row.referenceId.indexOf(sessionId) !== -1) {
      M.creditConsumedWhileQueued.add(1);
      console.error(`credit movement for session ${sessionId} while it was queued`);
      break;
    }
  }
  const current = call(sess, ACTIONS.speakingGet, { path: fill(ACTIONS.speakingGet.path, { id: sessionId }) });
  if (current.ok) {
    const state = String((current.json() || {}).state || '').toLowerCase().replace(/[^a-z]/g, '');
    if (state !== '' && state !== 'warmup') {
      M.timerStartedWhileQueued.add(1);
      console.error(`session ${sessionId} left warm-up (${state}) while queued`);
    }
  }
}

/** Wait without letting the open hub connection time out (not scaled: a queue wait is real time). */
function wait(sess, seconds) {
  if (sess.hub && !sess.hub.closed) hubHold(sess, sess.hub, seconds);
  else sleep(seconds);
}

/** Call an admission-gated route; while the platform queues the learner (429/503/202 + Retry-After) wait and retry. */
function admit(sess, spec, options, sessionId) {
  const startedMs = Date.now();
  let queued = false;
  for (;;) {
    const r = call(sess, spec, options);
    if (looksQueued(r)) {
      if (!queued) {
        queued = true;
        M.liveQueuedSessions.add(1);
        checkQueuedInvariants(sess, sessionId);
      }
      if (Date.now() - startedMs > CFG.speakingMaxWaitS * 1000) return null;
      wait(sess, retryAfterSeconds(r.retryAfter, 5) + Math.random());
      continue;
    }
    if (queued) M.liveQueueWaitMs.add(Date.now() - startedMs);
    return r;
  }
}

function speakingCycle(sess, content, cycle) {
  const card = pickAt(content.cards, sess.g + cycle);
  if (card === null) return skip('speaking', 'no_live_voice_card');
  begin('speaking');
  recordConsents(sess);
  const created = call(sess, ACTIONS.speakingCreate, { body: { rolePlayCardId: card, mode: 'ai_self_practice' } });
  if (!created.ok) return skip('speaking', created.domain ? 'create_refused' : 'create_failed');
  const createdBody = created.json() || {};
  const sessionId = createdBody.sessionId;
  if (!sessionId) return skip('speaking', 'no_session_id');
  const idPath = (spec) => fill(spec.path, { id: sessionId });

  call(sess, ACTIONS.speakingConsent, { path: idPath(ACTIONS.speakingConsent), body: { consentVersion: createdBody.consentVersion } });
  call(sess, ACTIONS.speakingStartWarmup, { path: idPath(ACTIONS.speakingStartWarmup) });
  think(sess, 2, 6);

  // The admission gate sits before any credit hold or timer: finish-warmup is where a learner queues.
  const finishWarmup = admit(sess, ACTIONS.speakingFinishWarmup, { path: idPath(ACTIONS.speakingFinishWarmup) }, sessionId);
  if (finishWarmup === null) return skip('speaking', 'queue_timeout');
  if (!finishWarmup.ok) return skip('speaking', 'finish_warmup_refused');
  think(sess, 1, 3);
  const roleplay = admit(sess, ACTIONS.speakingStartRoleplay, { path: idPath(ACTIONS.speakingStartRoleplay) }, sessionId);
  if (roleplay === null || !roleplay.ok) return skip('speaking', 'start_roleplay_refused');

  const preflight = admit(sess, ACTIONS.livePreflight, { path: idPath(ACTIONS.livePreflight) }, sessionId);
  if (preflight === null || !preflight.ok) return skip('speaking', 'preflight_refused');
  const candidates = (preflight.json() || {}).candidates || ['openai'];

  let provider = null;
  let providerSessionId = null;
  for (const candidate of candidates) {
    if (candidate === 'openai') {
      const offer = admit(sess, ACTIONS.liveOpenAiOffer, {
        path: idPath(ACTIONS.liveOpenAiOffer), body: { sdp: FAKE_SDP, clientSessionId: uuid() },
      }, sessionId);
      if (offer !== null && offer.ok) {
        provider = 'openai';
        providerSessionId = (offer.json() || {}).providerSessionId;
        break;
      }
    } else if (candidate === 'gemini') {
      const token = admit(sess, ACTIONS.liveGeminiToken, { path: idPath(ACTIONS.liveGeminiToken), body: {} }, sessionId);
      if (token !== null && token.ok) {
        provider = 'gemini';
        providerSessionId = (token.json() || {}).providerSessionId;
        break;
      }
    }
  }
  if (!providerSessionId) return skip('speaking', 'no_provider_session');

  if (cachedWav === null) cachedWav = wavBytes(1);
  const segments = [];
  for (let i = 0; i < CFG.speakingTurns && alive(sess); i += 1) {
    think(sess, 4, 9);
    const candidateText = SENTENCES[i % SENTENCES.length];
    const patientText = SENTENCES[(i + 3) % SENTENCES.length];
    call(sess, ACTIONS.liveTurn, {
      path: idPath(ACTIONS.liveTurn),
      body: { provider, providerSessionId, candidateText, patientText, clientTurnId: `t-${i}-${uuid()}`.slice(0, 64), turnIndex: i },
    });
    segments.push({ speaker: 'candidate', startMs: i * 8000, endMs: i * 8000 + 3500, text: candidateText });
    segments.push({ speaker: 'patient', startMs: i * 8000 + 3600, endMs: i * 8000 + 7500, text: patientText });
    if (i % 3 === 2) {
      call(sess, ACTIONS.liveAudioTurn, {
        path: idPath(ACTIONS.liveAudioTurn),
        multipart: true,
        body: {
          audio: http.file(cachedWav, 'turn.wav', 'audio/wav'),
          durationMs: '1000',
          providerSessionId,
        },
      });
    }
  }
  call(sess, ACTIONS.liveTranscript, { path: idPath(ACTIONS.liveTranscript), body: { provider, providerSessionId, segments } });
  call(sess, ACTIONS.speakingEnd, { path: idPath(ACTIONS.speakingEnd) });

  if (CFG.speakingAssessEvery > 0 && cycle % CFG.speakingAssessEvery === 0 && alive(sess)) {
    const startedMs = Date.now();
    const assessed = call(sess, ACTIONS.speakingAssess, { path: idPath(ACTIONS.speakingAssess) });
    M.aiAssessMs.add(Date.now() - startedMs, { phase: elapsedPhase() });
    if (assessed.ok) call(sess, ACTIONS.speakingResults, { path: idPath(ACTIONS.speakingResults) });
  }
  return finish('speaking');
}

// ---- tutor rooms ------------------------------------------------------------------------------------

function roomLearnerCycle(sess, content, ordinal, cycle) {
  begin('room');
  recordConsents(sess);
  const manifest = roomByOrdinal(ordinal);
  let roomId = manifest === null ? null : manifest.liveRoomId;
  let selfProvisioned = false;
  if (roomId === null) {
    const card = pickAt(content.cards, sess.g + cycle);
    if (card === null) return skip('room', 'no_card');
    const created = call(sess, ACTIONS.speakingCreate, { body: { rolePlayCardId: card, mode: 'live_tutor' } });
    if (!created.ok) return skip('room', created.domain ? 'session_refused' : 'session_failed');
    const sessionId = (created.json() || {}).sessionId;
    const room = call(sess, ACTIONS.roomCreate, { body: { speakingSessionId: sessionId } });
    if (!room.ok) return skip('room', room.domain ? 'room_refused' : 'room_failed');
    roomId = (room.json() || {}).liveRoomId;
    selfProvisioned = true;
  }
  if (!roomId) return skip('room', 'no_room_id');
  const token = call(sess, ACTIONS.roomToken, { path: fill(ACTIONS.roomToken.path, { id: roomId }) });
  if (!token.ok) return skip('room', token.domain ? 'token_refused' : 'token_failed');

  if (CFG.hubMode !== 'off') {
    closeHub(sess);
    const conn = hubConnect(sess, HUBS.speakingLiveRoom);
    if (conn === null) {
      openNotificationHub(sess);
      return skip('room', 'hub_connect_failed');
    }
    const join = hubInvoke(sess, conn, 'JoinRoom', [roomId]);
    const joined = join === null ? { done: false, error: 'send_failed' } : hubAwaitCompletion(sess, conn, join, 30000);
    if (!joined.done || joined.error) {
      hubClose(sess, conn);
      openNotificationHub(sess);
      return skip('room', 'join_failed');
    }
    sess.hub = conn;
    sess.hubPath = HUBS.speakingLiveRoom;
  }
  const heldUntil = Date.now() + CFG.roomSeconds * 1000 * CFG.thinkScale;
  while (Date.now() < heldUntil && alive(sess)) think(sess, 15, 25);
  if (sess.hub) {
    hubInvoke(sess, sess.hub, 'LeaveRoom', [roomId]);
    closeHub(sess);
  }
  if (selfProvisioned) call(sess, ACTIONS.roomEnd, { path: fill(ACTIONS.roomEnd.path, { id: roomId }) });
  openNotificationHub(sess);
  return finish('room');
}

function roomExpertCycle(sess, roomId) {
  begin('room');
  recordConsents(sess);
  const token = call(sess, ACTIONS.roomToken, { path: fill(ACTIONS.roomToken.path, { id: roomId }) });
  if (!token.ok) return skip('room', token.domain ? 'expert_token_refused' : 'expert_token_failed');
  if (CFG.hubMode === 'off') return finish('room');
  const conn = hubConnect(sess, HUBS.speakingLiveRoom);
  if (conn === null) return skip('room', 'expert_hub_connect_failed');
  const join = hubInvoke(sess, conn, 'JoinRoom', [roomId]);
  const joined = join === null ? { done: false, error: 'send_failed' } : hubAwaitCompletion(sess, conn, join, 30000);
  if (!joined.done || joined.error) {
    hubClose(sess, conn);
    return skip('room', 'expert_join_failed');
  }
  sess.hub = conn;
  sess.hubPath = HUBS.speakingLiveRoom;
  const heldUntil = Date.now() + CFG.roomSeconds * 1000 * CFG.thinkScale;
  let cue = 0;
  while (Date.now() < heldUntil && alive(sess) && !conn.closed) {
    think(sess, 15, 25);
    const sentAt = Date.now();
    const id = hubInvoke(sess, conn, 'BroadcastCue', [roomId, String(cue)]);
    if (id !== null) {
      const done = hubAwaitCompletion(sess, conn, id, 20000);
      if (done.done && !done.error) {
        M.cueSent.add(1);
        M.cueRoundtripMs.add(Date.now() - sentAt);
      }
    }
    cue += 1;
  }
  hubInvoke(sess, conn, 'LeaveRoom', [roomId]);
  closeHub(sess);
  return finish('room');
}

// ---- scenario entry points --------------------------------------------------------------------------

function elapsedPhase() {
  return TIMELINE.phaseAt(elapsedSeconds()).phase;
}

/** One learner's whole stay. k6 iteration i of this leg is global learner i * legs + leg. */
export function runLearner(content) {
  beginIteration();
  const g = globalIndex(exec.scenario.iterationInInstance, PARAMS.legCount, PARAMS.legIndex);
  const role = roleOf(g);
  const persona = role === 'learner' ? personaOf(g) : role;
  const sess = newSession('learner', g, g);
  sleepUntil(TIMELINE.startS(g));
  if (!alive(sess)) return;

  M.sessionsStarted.add(1, { role });
  begin('browse');
  if (!signInSession(sess)) {
    skip('browse', 'signin_failed');
    return;
  }
  if (!pageLoad(sess)) {
    skip('browse', 'bootstrap_failed');
    return;
  }
  openNotificationHub(sess);

  const examDueMs = Date.now() + uniform(120, 900) * 1000 * CFG.thinkScale;
  let examDone = false;
  let cycle = 0;
  while (alive(sess)) {
    think(sess, 8, 25);
    if (!alive(sess)) break;
    if (!examDone && Date.now() >= examDueMs && persona !== 'browser') {
      examDone = true;
      if (persona === 'reader') readingFlow(sess, content);
      else if (persona === 'listener') listeningFlow(sess, content);
      else if (persona === 'writer') writingFlow(sess, content);
    }
    if (role === 'speaker') {
      speakingCycle(sess, content, cycle);
      cycle += 1;
      continue;
    }
    if (role === 'room') {
      roomLearnerCycle(sess, content, roomOrdinalOf(g), cycle);
      cycle += 1;
      continue;
    }
    browseTick(sess);
  }
  closeHub(sess);
  M.sessionsEnded.add(1, { role });
  finish('browse');
}

/** A tutor in a pre-provisioned room: joins before its learner and raises cues. */
export function runExpert() {
  beginIteration();
  const room = globalIndex(exec.scenario.iterationInInstance, PARAMS.legCount, PARAMS.legIndex);
  const manifest = roomByOrdinal(room);
  if (manifest === null) return;
  const sess = newSession('expert', room, room);
  // Its learner is the room learner g = 20 * room + 19; arrive 30 s earlier.
  sleepUntil(Math.max(0, TIMELINE.startS(room * 20 + 19) - 30));
  if (!alive(sess)) return;
  if (!signInSession(sess)) {
    skip('room', 'expert_signin_failed');
    return;
  }
  while (alive(sess)) {
    roomExpertCycle(sess, manifest.liveRoomId);
    if (alive(sess)) think(sess, 20, 40);
  }
}
