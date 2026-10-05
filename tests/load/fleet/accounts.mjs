// Deterministic disposable-account naming shared by the seeding script (node) and the k6 harness.
// There is no accounts file to ship around: account N is always the same email and device id, so the
// seeder can create them, every k6 leg can derive its own slice, and the purge can find them again.
//
// SAFETY: these accounts only ever exist on a NON-production stack. isProductionHost() is the guard
// every entry point (seed, purge, k6 setup, workflow preflight) calls before any request is sent.

export const PRODUCTION_HOSTS = Object.freeze([
  'app.oetwithdrhesham.co.uk',
  'api.oetwithdrhesham.co.uk',
  'oetwithdrhesham.co.uk',
  'www.oetwithdrhesham.co.uk',
  '185.252.233.186',
]);

/** Lower-case host of a URL or bare host. Regex based on purpose: this file also runs inside k6,
 * where relying on a global URL implementation would tie the safety guard to the k6 version. */
export function hostOf(urlOrHost) {
  let rest = String(urlOrHost ?? '').trim();
  const scheme = rest.indexOf('://');
  if (scheme !== -1) rest = rest.slice(scheme + 3);
  rest = rest.split('/')[0].split('?')[0].split('#')[0];
  const at = rest.lastIndexOf('@');
  if (at !== -1) rest = rest.slice(at + 1);
  return rest.replace(/:\d+$/, '').toLowerCase();
}

/** True for a production host or any subdomain of one. Accepts a URL or a bare host. Throws on junk. */
export function isProductionHost(urlOrHost) {
  const text = String(urlOrHost ?? '').trim();
  if (!text) throw new TypeError('a URL or host is required');
  return PRODUCTION_HOSTS.some((blocked) => {
    const host = hostOf(text);
    return host === blocked || host.endsWith(`.${blocked}`);
  });
}

export function assertNonProduction(label, urlOrHost) {
  if (isProductionHost(urlOrHost)) {
    throw new Error(`${label} points at a production host (${urlOrHost}); the load harness never runs against production`);
  }
}

const PAD = Object.freeze({ learner: 4, expert: 3, probe: 4 });

export const ACCOUNT_KINDS = Object.freeze(Object.keys(PAD));

export function ordinalLabel(kind, ordinal) {
  if (!(kind in PAD)) throw new RangeError(`unknown account kind '${kind}'`);
  if (!Number.isInteger(ordinal) || ordinal < 0) throw new RangeError('ordinal must be a non-negative integer');
  return String(ordinal).padStart(PAD[kind], '0');
}

/**
 * @param {'learner'|'expert'|'probe'} kind
 * @param {number} ordinal
 * @param {{prefix?:string, domain?:string}} [options]
 */
export function accountFor(kind, ordinal, { prefix = 'loadtest', domain = 'load.oet.test' } = {}) {
  const label = `${prefix}-${kind}-${ordinalLabel(kind, ordinal)}`;
  return {
    kind,
    ordinal,
    email: `${label}@${domain}`,
    name: `Load ${kind} ${ordinalLabel(kind, ordinal)}`,
    // Stable per account (first sign-in of a device is auto-trusted); well under the 128-char limit.
    deviceId: `dev-${label}`,
  };
}

/** The accounts a run of this size needs, for the seeder. */
export function accountPlan({ learners, experts }) {
  if (!Number.isInteger(learners) || learners < 0) throw new RangeError('learners must be a non-negative integer');
  if (!Number.isInteger(experts) || experts < 0) throw new RangeError('experts must be a non-negative integer');
  return { learners, experts, probes: 1, total: learners + experts + 1 };
}
