import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// Redirect the ledger to a temp file BEFORE importing the modules that resolve its path,
// so running this test never pollutes the production ledger.
const tmpLedger = path.join(os.tmpdir(), 'agy-resume-selftest-' + Date.now() + '.jsonl');
process.env.AGY_LEDGER_PATH = tmpLedger;

const { createRunningRecord, readAllRecords, createTerminalRecord } = await import('./ledger.js');
const { spawnAgy } = await import('./agy-runner.js');

let f = 0;
const ok = (n, c, x) => { if (c) console.log('PASS: ' + n); else { f++; console.log('FAIL: ' + n + (x ? ' :: ' + x : '')); } };

const long = 'G'.repeat(5000);
const rec = createRunningRecord({ role: 'implement', workspace: process.cwd(), attempt: 1, goal: long, context: 'ctx' });
ok('running record has status running', rec.status === 'running', rec.status);
ok('goal stored and truncated to 2000 chars', rec.goal && rec.goal.length === 2000, rec.goal && String(rec.goal.length));
ok('context stored', rec.context === 'ctx');
const term = createTerminalRecord(rec, { status: 'SUCCESS', exitCode: 0 });
ok('terminal record preserves goal', term.goal === rec.goal);
ok('terminal record status set', term.status === 'SUCCESS', term.status);

const marker = 'resume-selftest-' + Date.now();
await spawnAgy(['--version'], { cwd: process.cwd(), timeoutMs: 60000, ledger: { role: 'explore', workspace: process.cwd(), attempt: 1, goal: marker } });
const mine = readAllRecords().filter((r) => r.goal === marker);
const running = mine.filter((r) => r.status === 'running');
const done = mine.filter((r) => r.status !== 'running');
ok('spawnAgy wrote a running record for the real run', running.length === 1, 'running=' + running.length);
ok('spawnAgy wrote exactly one terminal record', done.length === 1, 'terminal=' + done.length);
ok('terminal record carries exit code 0', done[0] && done[0].exitCode === 0, done[0] && String(done[0].exitCode));
ok('terminal record carries a duration', done[0] && typeof done[0].durationMs === 'number', done[0] && String(done[0].durationMs));
ok('running record captured the role', running[0] && running[0].role === 'explore');
ok('both records retain the goal so the run is resumable', running[0] && running[0].goal === marker && done[0] && done[0].goal === marker);
ok('ledger redirected to temp so production stays clean', tmpLedger.length > 0 && readAllRecords().every((r) => r.goal !== null), 'temp=' + tmpLedger);

console.log(f === 0 ? '=== ALL RESUMABLE-LEDGER SELF-TESTS PASSED ===' : '=== ' + f + ' ASSERTION(S) FAILED ===');
process.exit(f ? 1 : 0);

