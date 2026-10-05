// Pure schedule math for the fleet load harness. NO k6 imports: this file is loaded by k6 (init
// context) and by `node --test` (profiles.test.mjs), so every rule here is unit-tested without k6.
//
// Model
//   * A "learner session" is ONE k6 iteration that lives for the learner's whole stay. The
//     `shared-iterations` executor numbers iterations 0..N-1 (`exec.scenario.iterationInInstance`),
//     so iteration i of leg L is global learner g = i * legCount + L: dense, collision-free, and
//     interleaved so every leg ramps in parallel (each leg is its own k6 process and its own source
//     IP, which is what keeps the per-IP sign-in limiter of 100/min honest).
//   * g decides the account (learner-<g>), the role (g % 20) and the start / end time, all from the
//     profile timeline below. Nothing is random, so two legs can never pick the same account.
//   * Time is seconds from the scenario start. Phases are what the thresholds are keyed on.

export const PROFILE_NAMES = Object.freeze(['smoke', 'capacity', 'steady', 'overload']);

/** 20-learner role cycle: 17 browsing learners, 2 AI-speaking learners, 1 tutor-room learner
 * (85 % / 10 % / 5 %, i.e. 850 / 100 / 50 of 1,000). */
export const ROLE_CYCLE_LENGTH = 20;
const ROLE_CYCLE = Object.freeze([...Array(17).fill('learner'), 'speaker', 'speaker', 'room']);

/** What a browsing learner does besides the shared dashboard/bootstrap/search mix (g % 20 < 17). */
const PERSONAS = Object.freeze([
  'reader', 'reader', 'reader', 'reader',
  'listener', 'listener', 'listener',
  'writer', 'writer', 'writer', 'writer',
  'browser', 'browser', 'browser', 'browser', 'browser', 'browser',
]);

export function roleOf(g, roomsAvailable = Number.POSITIVE_INFINITY) {
  const role = ROLE_CYCLE[g % ROLE_CYCLE_LENGTH];
  // A room learner needs a pre-provisioned room; without one the account just browses.
  if (role === 'room' && Math.floor(g / ROLE_CYCLE_LENGTH) >= roomsAvailable) return 'learner';
  return role;
}

export function personaOf(g) {
  const index = g % ROLE_CYCLE_LENGTH;
  return index < PERSONAS.length ? PERSONAS[index] : 'browser';
}

/** Room ordinal of a room learner (g % 20 === 19): rooms are numbered 0.. in learner order. */
export function roomOrdinalOf(g) {
  return Math.floor(g / ROLE_CYCLE_LENGTH);
}

/** How many learners g in [0, total) have the given role (exact, by counting). */
export function countRole(total, role, roomsAvailable = Number.POSITIVE_INFINITY) {
  let count = 0;
  for (let g = 0; g < total; g += 1) if (roleOf(g, roomsAvailable) === role) count += 1;
  return count;
}

/** Number of g in [0, total) with g % legCount === legIndex (iterations this leg must run). */
export function legShare(total, legCount, legIndex) {
  if (!Number.isInteger(total) || total < 0) throw new RangeError('total must be a non-negative integer');
  if (!Number.isInteger(legCount) || legCount < 1) throw new RangeError('legCount must be >= 1');
  if (!Number.isInteger(legIndex) || legIndex < 0 || legIndex >= legCount) throw new RangeError('legIndex out of range');
  return total > legIndex ? Math.floor((total - 1 - legIndex) / legCount) + 1 : 0;
}

/** Global learner index of leg-local iteration `local`. */
export function globalIndex(local, legCount, legIndex) {
  return local * legCount + legIndex;
}

const COMMON_DEFAULTS = Object.freeze({
  legCount: 1, legIndex: 0, signinPerMinPerLeg: 60, cooldownSeconds: 180,
});

const DEFAULTS = Object.freeze({
  smoke: { learners: 20, holdSeconds: 120 },
  capacity: { learners: 1000, stageFractions: [0.1, 0.3, 0.6, 1.0], stageHoldMinutes: 10, settleSeconds: 60 },
  steady: { learners: 1000, steadyMinutes: 60, settleSeconds: 120 },
  overload: {
    learners: 1000, surgeLearners: 500, steadyMinutes: 10, surgeHoldMinutes: 20,
    recoveryMinutes: 10, settleSeconds: 120, subsideSeconds: 120,
  },
});

/** Merge env-derived overrides with the profile defaults and validate. Pure: `overrides` is a plain
 * object (config.js builds it from `__ENV`). */
export function normalizeParams(profile, overrides = {}) {
  if (!PROFILE_NAMES.includes(profile)) {
    throw new RangeError(`unknown profile '${profile}' (expected one of ${PROFILE_NAMES.join(', ')})`);
  }
  const merged = { ...COMMON_DEFAULTS, ...DEFAULTS[profile] };
  for (const [key, value] of Object.entries(overrides)) {
    if (value !== undefined && value !== null && value !== '') merged[key] = value;
  }
  const num = (key, min) => {
    const value = Number(merged[key]);
    if (!Number.isFinite(value) || value < min) throw new RangeError(`${key} must be a number >= ${min} (got ${merged[key]})`);
    return value;
  };
  const params = {
    profile,
    legCount: Math.floor(num('legCount', 1)),
    legIndex: Math.floor(num('legIndex', 0)),
    signinPerMinPerLeg: num('signinPerMinPerLeg', 1),
    cooldownSeconds: Math.floor(num('cooldownSeconds', 10)),
    learners: Math.floor(num('learners', 1)),
  };
  if (params.legIndex >= params.legCount) throw new RangeError('legIndex must be < legCount');
  if (profile === 'smoke') params.holdSeconds = num('holdSeconds', 30);
  if (profile === 'capacity') {
    const fractions = merged.stageFractions;
    if (!Array.isArray(fractions) || fractions.length === 0 || fractions.some((f) => !(f > 0 && f <= 1))) {
      throw new RangeError('stageFractions must be a non-empty array of fractions in (0, 1]');
    }
    params.stageFractions = fractions.map(Number);
    params.stageHoldMinutes = num('stageHoldMinutes', 1);
    params.settleSeconds = num('settleSeconds', 0);
  }
  if (profile === 'steady' || profile === 'overload') {
    params.steadyMinutes = num('steadyMinutes', 1);
    params.settleSeconds = num('settleSeconds', 0);
  }
  if (profile === 'overload') {
    params.surgeLearners = Math.floor(num('surgeLearners', 1));
    params.surgeHoldMinutes = num('surgeHoldMinutes', 1);
    params.recoveryMinutes = num('recoveryMinutes', 1);
    params.subsideSeconds = num('subsideSeconds', 10);
  }
  return params;
}

/** Seconds between two consecutive GLOBAL sign-ins: every leg signs in at signinPerMinPerLeg per
 * minute, so the fleet as a whole signs in at legCount times that. */
export function signinSpacingSeconds(params) {
  return 60 / (params.signinPerMinPerLeg * params.legCount);
}

/**
 * Build the timeline. Returns plain data plus closures:
 *   totalLearners, baseLearners, durationS, phases[], stages[]
 *   startS(g), endS(g), phaseAt(tS) -> { phase, stage }, isSurge(g)
 * `phases[].name` is one of: warmup | ramp | steady | overload | subside | recovery | cooldown.
 */
export function buildTimeline(params) {
  const spacing = signinSpacingSeconds(params);
  const base = params.learners;
  const phases = [];
  const stages = [];
  const push = (name, fromS, toS, stage = null) => {
    if (toS > fromS) phases.push({ name, fromS, toS, stage });
  };

  let startS;
  let endS;
  let durationS;
  let surgeFrom = Number.POSITIVE_INFINITY;
  let total = base;

  if (params.profile === 'smoke') {
    const rampEnd = base * spacing;
    const steadyFrom = rampEnd + 10;
    const steadyTo = Math.max(steadyFrom + 10, rampEnd + params.holdSeconds);
    push('warmup', 0, steadyFrom);
    push('steady', steadyFrom, steadyTo);
    durationS = steadyTo + 20;
    push('cooldown', steadyTo, durationS);
    startS = (g) => g * spacing;
    endS = (g) => steadyTo + (g % 20);
  } else if (params.profile === 'steady') {
    const rampEnd = base * spacing;
    const steadyFrom = rampEnd + params.settleSeconds;
    const steadyTo = steadyFrom + params.steadyMinutes * 60;
    push('warmup', 0, steadyFrom);
    push('steady', steadyFrom, steadyTo);
    durationS = steadyTo + params.cooldownSeconds;
    push('cooldown', steadyTo, durationS);
    startS = (g) => g * spacing;
    endS = (g) => steadyTo + (g % params.cooldownSeconds);
  } else if (params.profile === 'capacity') {
    const targets = [...new Set(params.stageFractions.map((f) => Math.max(1, Math.round(f * base))))]
      .sort((a, b) => a - b);
    if (targets[targets.length - 1] !== base) targets.push(base);
    let cursor = 0;
    let previous = 0;
    for (const target of targets) {
      const rampFrom = cursor;
      const rampTo = rampFrom + (target - previous) * spacing;
      const steadyFrom = rampTo + params.settleSeconds;
      const steadyTo = steadyFrom + params.stageHoldMinutes * 60;
      stages.push({ target, previous, rampFrom, rampTo, steadyFrom, steadyTo });
      push(stages.length === 1 ? 'warmup' : 'ramp', rampFrom, steadyFrom);
      push('steady', steadyFrom, steadyTo, target);
      cursor = steadyTo;
      previous = target;
    }
    durationS = cursor + params.cooldownSeconds;
    push('cooldown', cursor, durationS);
    startS = (g) => {
      const stage = stages.find((s) => g >= s.previous && g < s.target) ?? stages[stages.length - 1];
      return stage.rampFrom + (g - stage.previous) * spacing;
    };
    endS = (g) => cursor + (g % params.cooldownSeconds);
  } else {
    // overload: base ramp -> steady -> surge (overload) -> subside -> recovery -> cooldown
    total = base + params.surgeLearners;
    const rampEnd = base * spacing;
    const steadyFrom = rampEnd + params.settleSeconds;
    const steadyTo = steadyFrom + params.steadyMinutes * 60;
    const surgeRampTo = steadyTo + params.surgeLearners * spacing;
    const overloadTo = surgeRampTo + params.surgeHoldMinutes * 60;
    const recoveryFrom = overloadTo + params.subsideSeconds;
    const recoveryTo = recoveryFrom + params.recoveryMinutes * 60;
    surgeFrom = steadyTo;
    push('warmup', 0, steadyFrom);
    push('steady', steadyFrom, steadyTo);
    push('overload', steadyTo, overloadTo);
    push('subside', overloadTo, recoveryFrom);
    push('recovery', recoveryFrom, recoveryTo);
    durationS = recoveryTo + params.cooldownSeconds;
    push('cooldown', recoveryTo, durationS);
    startS = (g) => (g < base ? g * spacing : steadyTo + (g - base) * spacing);
    endS = (g) => (g < base
      ? recoveryTo + (g % params.cooldownSeconds)
      : overloadTo + ((g - base) % params.subsideSeconds));
  }

  const phaseAt = (tS) => {
    for (const phase of phases) {
      if (tS >= phase.fromS && tS < phase.toS) return { phase: phase.name, stage: phase.stage };
    }
    return { phase: tS < 0 ? 'warmup' : 'cooldown', stage: null };
  };

  return {
    profile: params.profile,
    totalLearners: total,
    baseLearners: base,
    durationS,
    phases,
    stages,
    startS,
    endS,
    phaseAt,
    isSurge: (g) => params.profile === 'overload' && g >= base,
    surgeFromS: surgeFrom === Number.POSITIVE_INFINITY ? null : surgeFrom,
  };
}

/**
 * The per-leg work: which global learners, how many speakers / rooms / experts this leg runs.
 * `roomsAvailable` bounds how many room learners exist at all (default: every g % 20 === 19);
 * `pairedRooms` is how many of those rooms are pre-provisioned with an assigned expert (a rooms
 * manifest): only those get an expert session. The other rooms are created by their learner and run
 * without a tutor side.
 */
export function planLeg(timeline, params, roomsAvailable = Number.POSITIVE_INFINITY, pairedRooms = 0) {
  const learners = legShare(timeline.totalLearners, params.legCount, params.legIndex);
  const counts = { learner: 0, speaker: 0, room: 0 };
  const personas = { reader: 0, listener: 0, writer: 0, browser: 0 };
  for (let local = 0; local < learners; local += 1) {
    const g = globalIndex(local, params.legCount, params.legIndex);
    const role = roleOf(g, roomsAvailable);
    counts[role] += 1;
    if (role === 'learner') personas[personaOf(g)] += 1;
  }
  const totalRooms = countRole(timeline.totalLearners, 'room', roomsAvailable);
  const paired = Math.min(totalRooms, Math.max(0, pairedRooms));
  const experts = legShare(paired, params.legCount, params.legIndex);
  return { learners, counts, personas, experts, totalRooms, pairedRooms: paired };
}

/** Plain-JSON description of the timeline for the report and the setup() banner. */
export function describeTimeline(timeline) {
  return {
    profile: timeline.profile,
    totalLearners: timeline.totalLearners,
    baseLearners: timeline.baseLearners,
    durationS: Math.round(timeline.durationS),
    surgeFromS: timeline.surgeFromS === null ? null : Math.round(timeline.surgeFromS),
    phases: timeline.phases.map((p) => ({ ...p, fromS: Math.round(p.fromS), toS: Math.round(p.toS) })),
    stages: timeline.stages.map((s) => ({
      target: s.target, steadyFromS: Math.round(s.steadyFrom), steadyToS: Math.round(s.steadyTo),
    })),
  };
}
