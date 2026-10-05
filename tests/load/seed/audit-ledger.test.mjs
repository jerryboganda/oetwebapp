import assert from 'node:assert/strict';
import test from 'node:test';
import { analyseLedger, auditAccounts, main, movesCredits } from './audit-ledger.mjs';

const debit = (referenceId, reason = 'GradingDeduct', delta = -2) => ({ reason, referenceId, writingOnlyCreditsDelta: delta });

test('movesCredits needs a non-zero numeric delta', () => {
  assert.equal(movesCredits({ writingOnlyCreditsDelta: -2 }), true);
  assert.equal(movesCredits({ sharedCreditsDelta: 0, flexibleCreditsDelta: 0 }), false);
  assert.equal(movesCredits({}), false);
  assert.equal(movesCredits(null), false);
});

test('a clean ledger has no duplicates', () => {
  const r = analyseLedger({ writingOnlyCredits: 98, transactions: [debit('writing-grade:1'), debit('writing-grade:2'), debit('writing-grade:1', 'RefundOnFailure', 2)] });
  assert.deepEqual(r, { inspected: 3, duplicates: [], negativePools: [] });
});

test('the same reason and reference charged twice is a duplicate charge', () => {
  const r = analyseLedger({ transactions: [debit('writing-grade:1'), debit('writing-grade:1'), debit('writing-grade:1'), debit('writing-grade:9')] });
  assert.deepEqual(r.duplicates, [{ reason: 'GradingDeduct', referenceId: 'writing-grade:1', count: 3 }]);
});

test('rows without a reference or without a movement are ignored', () => {
  const r = analyseLedger({ transactions: [
    { reason: 'Grant', writingOnlyCreditsDelta: 100 },
    { reason: 'Grant', writingOnlyCreditsDelta: 100 },
    { reason: 'Audit', referenceId: 'x', writingOnlyCreditsDelta: 0 },
    { reason: 'Audit', referenceId: 'x', writingOnlyCreditsDelta: 0 },
  ] });
  assert.equal(r.duplicates.length, 0);
});

test('a negative pool means the learner spent credits they did not have', () => {
  assert.deepEqual(analyseLedger({ sharedCredits: -2, flexibleCredits: 4, transactions: [] }).negativePools, ['sharedCredits']);
  assert.deepEqual(analyseLedger(null), { inspected: 0, duplicates: [], negativePools: [] });
});

function fakeAuditClient(snapshots) {
  return {
    async get(path) {
      if (path.startsWith('/v1/admin/users?')) {
        return { items: Object.keys(snapshots).map((email) => ({ id: `id:${email}`, email })) };
      }
      const id = decodeURIComponent(path.match(/ai-package-credits\/([^?]+)/)[1]);
      const email = id.slice(3);
      if (snapshots[email] instanceof Error) throw snapshots[email];
      return snapshots[email];
    },
  };
}

test('auditAccounts aggregates duplicates, negative balances and failures across accounts', async () => {
  const client = fakeAuditClient({
    'lt-probe-0000@x.test': { transactions: [] },
    'lt-learner-0000@x.test': { transactions: [debit('s:1'), debit('s:1')] },
    'lt-learner-0001@x.test': { sharedCredits: -1, transactions: [] },
    'lt-learner-0002@x.test': new Error('HTTP 500'),
  });
  const report = await auditAccounts(client, { learners: 4, experts: 2, prefix: 'lt', domain: 'x.test' });
  assert.equal(report.accounts, 5); // experts are not audited
  assert.equal(report.missing, 1); // learner 3 was never created
  assert.equal(report.duplicates.length, 1);
  assert.equal(report.duplicates[0].email, 'lt-learner-0000@x.test');
  assert.equal(report.negativeBalances.length, 1);
  assert.equal(report.failed.length, 1);
  assert.equal(report.passed, false);
});

test('an all-clean run passes', async () => {
  const client = fakeAuditClient({ 'lt-probe-0000@x.test': { transactions: [debit('s:1')] }, 'lt-learner-0000@x.test': { transactions: [] } });
  const report = await auditAccounts(client, { learners: 1, experts: 0, prefix: 'lt', domain: 'x.test' });
  assert.equal(report.passed, true);
  assert.equal(report.transactionsInspected, 1);
});

test('the CLI needs admin credentials and refuses production', async () => {
  await assert.rejects(main(['--api', 'https://api.staging.example'], {}), /OET_LOAD_ADMIN_EMAIL/);
  await assert.rejects(main(['--api', 'https://api.oetwithdrhesham.co.uk'], { OET_LOAD_ADMIN_EMAIL: 'a', OET_LOAD_ADMIN_PASSWORD: 'b' }), /production host/);
});
