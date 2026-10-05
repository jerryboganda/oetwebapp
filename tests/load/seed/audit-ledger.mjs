#!/usr/bin/env node
// Post-run credit audit for the fleet load test: "no duplicate credits / charges".
//
// k6 proves idempotency at the request level (a replayed submit returns the same submission, a page
// refresh does not charge twice). This script proves it at the LEDGER level, after the run, for every
// disposable account: no (reason, referenceId) pair may appear twice with a movement, and no balance
// may be negative. Read-only: it only GETs the admin credit snapshot of each account.
//
//   OET_LOAD_ADMIN_EMAIL=... OET_LOAD_ADMIN_PASSWORD=... \
//   node tests/load/seed/audit-ledger.mjs --api https://api.staging.example --learners 1500 --report ledger-audit.json
//
// Exit code 1 when any duplicate or negative balance is found; the report lists them.

import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { createAdminClient, listAccounts, mapUserIds, parseArgs, runPool } from './seed-lib.mjs';

const POOLS = ['flexibleCredits', 'writingOnlyCredits', 'speakingOnlyCredits', 'sharedCredits', 'creditsRemaining',
  'listeningTestsRemaining', 'readingTestsRemaining', 'mockExamsRemaining'];

const MOVEMENT_FIELDS = ['sharedCreditsDelta', 'flexibleCreditsDelta', 'writingOnlyCreditsDelta', 'speakingOnlyCreditsDelta',
  'listeningTestsDelta', 'readingTestsDelta', 'mockExamsDelta', 'creditsDelta', 'delta'];

/** A transaction moves credits when any delta field is a non-zero number. */
export function movesCredits(transaction) {
  return MOVEMENT_FIELDS.some((field) => typeof transaction?.[field] === 'number' && transaction[field] !== 0);
}

/**
 * Inspect one account's credit snapshot.
 * @returns {{ inspected:number, duplicates:{reason:string, referenceId:string, count:number}[], negativePools:string[] }}
 */
export function analyseLedger(snapshot) {
  const transactions = Array.isArray(snapshot?.transactions) ? snapshot.transactions : [];
  const counts = new Map();
  for (const transaction of transactions) {
    if (!movesCredits(transaction)) continue;
    const referenceId = typeof transaction.referenceId === 'string' ? transaction.referenceId : '';
    if (referenceId === '') continue; // no reference: nothing to compare
    const key = `${transaction.reason ?? ''}\u0000${referenceId}`;
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  const duplicates = [];
  for (const [key, count] of counts) {
    if (count > 1) {
      const [reason, referenceId] = key.split('\u0000');
      duplicates.push({ reason, referenceId, count });
    }
  }
  const negativePools = POOLS.filter((pool) => typeof snapshot?.[pool] === 'number' && snapshot[pool] < 0);
  return { inspected: transactions.length, duplicates, negativePools };
}

export async function auditAccounts(client, options) {
  const accounts = listAccounts(options).filter((account) => account.kind !== 'expert');
  const idByEmail = await mapUserIds(client, `${options.prefix}-`);
  const report = { accounts: accounts.length, missing: 0, transactionsInspected: 0, duplicates: [], negativeBalances: [], failed: [] };
  await runPool(accounts, options.concurrency ?? 6, async (account) => {
    const userId = idByEmail.get(account.email.toLowerCase());
    if (!userId) {
      report.missing += 1;
      return;
    }
    try {
      const snapshot = await client.get(`/v1/admin/ai-package-credits/${encodeURIComponent(userId)}?pageSize=200`);
      const result = analyseLedger(snapshot);
      report.transactionsInspected += result.inspected;
      for (const duplicate of result.duplicates) report.duplicates.push({ email: account.email, ...duplicate });
      if (result.negativePools.length > 0) report.negativeBalances.push({ email: account.email, pools: result.negativePools });
    } catch (error) {
      report.failed.push({ email: account.email, error: String(error.message ?? error).slice(0, 200) });
    }
  });
  report.passed = report.duplicates.length === 0 && report.negativeBalances.length === 0 && report.failed.length === 0;
  return report;
}

export async function main(argv, env = process.env, deps = {}) {
  const args = parseArgs(argv, env);
  if (!env.OET_LOAD_ADMIN_EMAIL || !env.OET_LOAD_ADMIN_PASSWORD) {
    throw new Error('OET_LOAD_ADMIN_EMAIL and OET_LOAD_ADMIN_PASSWORD are required (a NON-production admin)');
  }
  const client = deps.client ?? createAdminClient({ api: args.api, email: env.OET_LOAD_ADMIN_EMAIL, password: env.OET_LOAD_ADMIN_PASSWORD });
  const report = await auditAccounts(client, args);
  if (args.report) writeFileSync(args.report, JSON.stringify(report, null, 2));
  return report;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  main(process.argv.slice(2))
    .then((report) => {
      process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
      process.exitCode = report.passed ? 0 : 1;
    })
    .catch((error) => {
      process.stderr.write(`audit-ledger: ${error.message}\n`);
      process.exitCode = 2;
    });
}
