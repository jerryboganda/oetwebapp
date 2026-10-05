import assert from 'node:assert/strict';
import test from 'node:test';
import { accountFor, accountPlan, assertNonProduction, hostOf, isProductionHost, ordinalLabel } from './accounts.mjs';

test('hostOf extracts the lower-case host without URL support', () => {
  assert.equal(hostOf('https://user:pw@Host.Example:8443/x?y#z'), 'host.example');
  assert.equal(hostOf('http://localhost:5198'), 'localhost');
  assert.equal(hostOf('api.example.test/path'), 'api.example.test');
  assert.equal(hostOf('[::1]:5198'), '[::1]');
  // credentials in the URL must not smuggle a production host past the guard
  assert.equal(isProductionHost('https://staging.example.test@api.oetwithdrhesham.co.uk/'), true);
  assert.equal(isProductionHost('https://api.oetwithdrhesham.co.uk@staging.example.test/'), false);
});

test('accounts are deterministic and distinct', () => {
  const a = accountFor('learner', 7);
  assert.deepEqual(a, accountFor('learner', 7));
  assert.equal(a.email, 'loadtest-learner-0007@load.oet.test');
  assert.equal(a.deviceId, 'dev-loadtest-learner-0007');
  assert.equal(accountFor('expert', 12).email, 'loadtest-expert-012@load.oet.test');
  assert.equal(accountFor('probe', 0).email, 'loadtest-probe-0000@load.oet.test');
  const emails = new Set();
  for (let i = 0; i < 1500; i += 1) emails.add(accountFor('learner', i).email);
  assert.equal(emails.size, 1500);
  assert.ok(a.deviceId.length <= 128);
});

test('prefix and domain are configurable', () => {
  assert.equal(accountFor('learner', 1, { prefix: 'lt2', domain: 'x.test' }).email, 'lt2-learner-0001@x.test');
});

test('ordinalLabel validates', () => {
  assert.equal(ordinalLabel('learner', 1499), '1499');
  assert.throws(() => ordinalLabel('admin', 1), /unknown account kind/);
  assert.throws(() => ordinalLabel('learner', -1), /ordinal/);
  assert.throws(() => ordinalLabel('learner', 1.5), /ordinal/);
});

test('accountPlan counts every account the run needs', () => {
  assert.deepEqual(accountPlan({ learners: 1500, experts: 75 }), { learners: 1500, experts: 75, probes: 1, total: 1576 });
  assert.throws(() => accountPlan({ learners: -1, experts: 0 }), /learners/);
});

test('production hosts are recognised in every spelling the harness might receive', () => {
  for (const value of [
    'https://api.oetwithdrhesham.co.uk', 'https://app.oetwithdrhesham.co.uk/path', 'api.oetwithdrhesham.co.uk',
    'https://staging.app.oetwithdrhesham.co.uk', '185.252.233.186', 'http://185.252.233.186:8080',
    'HTTPS://API.OETWITHDRHESHAM.CO.UK',
  ]) {
    assert.equal(isProductionHost(value), true, value);
  }
  for (const value of ['https://staging.example.test', 'http://localhost:5198', 'https://oet-load.example.org']) {
    assert.equal(isProductionHost(value), false, value);
  }
  assert.throws(() => isProductionHost(''), /required/);
  assert.throws(() => assertNonProduction('K6_API_URL', 'https://api.oetwithdrhesham.co.uk'), /production host/);
  assert.doesNotThrow(() => assertNonProduction('K6_API_URL', 'https://staging.example.test'));
});
