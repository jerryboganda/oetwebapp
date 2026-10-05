// How long a "think" really lasts while a SignalR hub connection is held. Pure (no k6 imports): used by
// fleet-1000.k6.js for the report and unit-tested in node.
//
// While a hub is open every think() is spent long-polling it (signalr.js hubHold), and a poll returns
// only when the server has a frame: normally its 15 s keep-alive ping. A pause of T seconds therefore
// lasts 15 * ceil(T / 15) seconds, not T, and no action cadence shorter than one poll is reachable on a
// single connection. The request rate the harness drives is lower than the nominal think-time mix says;
// the report prints this table so nobody reads the nominal numbers as the achieved ones.

export const HUB_POLL_SECONDS = 15;

/**
 * Every think() range flows.js uses, by activity. flows.js keeps the literals; cadence.test.mjs fails
 * when the two drift apart.
 */
export const THINK_RANGES = Object.freeze({
  'browse tick': Object.freeze([8, 25]),
  'Reading answer save': Object.freeze([12, 30]),
  'Listening answer save': Object.freeze([10, 25]),
  'Writing draft save': Object.freeze([15, 35]),
  'speaking turn': Object.freeze([4, 9]),
  'speaking warm-up pause': Object.freeze([2, 6]),
  'speaking role-play pause': Object.freeze([1, 3]),
  'pause before a submit (Reading, Writing)': Object.freeze([5, 15]),
  'room hold / expert cue': Object.freeze([15, 25]),
  'expert rest between rooms': Object.freeze([20, 40]),
});

/** Expected wall time of one think drawn uniformly from [min, max] seconds while a hub is held. */
export function effectiveThinkSeconds(min, max, poll = HUB_POLL_SECONDS) {
  if (!(poll > 0)) throw new RangeError('poll must be a positive number of seconds');
  if (!(min >= 0) || !(max >= min)) throw new RangeError('expected 0 <= min <= max');
  if (max === min) return poll * Math.max(1, Math.ceil(min / poll));
  let total = 0;
  for (let k = Math.max(1, Math.ceil(min / poll)); (k - 1) * poll < max; k += 1) {
    const lo = Math.max(min, (k - 1) * poll);
    const hi = Math.min(max, k * poll);
    if (hi > lo) total += k * poll * (hi - lo);
  }
  return total / (max - min);
}

const round1 = (value) => Math.round(value * 10) / 10;

/** Report rows: nominal mean pause, effective mean pause and the slowdown, per activity. */
export function hubCadenceRows(thinkScale = 1, poll = HUB_POLL_SECONDS) {
  if (!(thinkScale > 0)) throw new RangeError('thinkScale must be positive');
  return Object.entries(THINK_RANGES).map(([activity, [min, max]]) => {
    const lo = min * thinkScale;
    const hi = max * thinkScale;
    const nominal = (lo + hi) / 2;
    const effective = effectiveThinkSeconds(lo, hi, poll);
    return {
      activity,
      nominalS: round1(nominal),
      effectiveS: round1(effective),
      slowdown: Math.round((effective / nominal) * 100) / 100,
    };
  });
}
