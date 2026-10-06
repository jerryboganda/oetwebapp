#!/usr/bin/env node
// MANUAL TOOL, INERT: run manually by the owner from a self-provisioned load generator; no CI runs
// this; agents never run it (AGENTS.md "NO AUTOMATED QA ANYWHERE").
//
// Seed (or purge) the disposable accounts of the fleet load test on a NON-production stack.
//
//   OET_LOAD_ADMIN_EMAIL=... OET_LOAD_ADMIN_PASSWORD=... OET_LOAD_PASSWORD=... \
//   node tests/load/seed/seed-accounts.mjs --api https://api.staging.example --learners 1500 --experts 75
//   ... --purge        delete every account of the run afterwards
//   ... --dry-run      print the plan, send nothing
//
// 1 probe + N learners + M experts, created through POST /v1/admin/users (password set, no invite
// email) with credit pools granted through the admin adjust endpoint. Idempotent. See
// docs/ops/LOAD-TESTING.md.
//
// This script only calls an HTTP API. Run it from the dedicated load-generator host, never against
// production (the host guard refuses it).

import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import {
  createAdminClient, listAccounts, parseArgs, purgeAccounts, seedAccounts,
} from './seed-lib.mjs';

export async function main(argv, env = process.env, deps = {}) {
  const args = parseArgs(argv, env);
  const accounts = listAccounts(args);
  const plan = { api: args.api, accounts: accounts.length, learners: args.learners, experts: args.experts, purge: args.purge };
  if (args.dryRun) return { plan, dryRun: true };

  const adminEmail = env.OET_LOAD_ADMIN_EMAIL;
  const adminPassword = env.OET_LOAD_ADMIN_PASSWORD;
  const password = env.OET_LOAD_PASSWORD;
  if (!adminEmail || !adminPassword) throw new Error('OET_LOAD_ADMIN_EMAIL and OET_LOAD_ADMIN_PASSWORD are required (a NON-production admin)');
  if (!args.purge && !password) throw new Error('OET_LOAD_PASSWORD is required (the shared password of the disposable accounts)');

  const client = deps.client ?? createAdminClient({ api: args.api, email: adminEmail, password: adminPassword });
  const log = (message) => process.stderr.write(`${message}\n`);
  const options = {
    learners: args.learners, experts: args.experts, prefix: args.prefix, domain: args.domain,
    password, professionId: args.profession, concurrency: args.concurrency, log,
  };
  const result = args.purge ? await purgeAccounts(client, options) : await seedAccounts(client, options);
  const report = { plan, result };
  if (args.report) writeFileSync(args.report, JSON.stringify(report, null, 2));
  return report;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  main(process.argv.slice(2))
    .then((report) => {
      process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
      if (report.result?.failed?.length) process.exitCode = 1;
    })
    .catch((error) => {
      process.stderr.write(`seed-accounts: ${error.message}\n`);
      process.exitCode = 2;
    });
}
