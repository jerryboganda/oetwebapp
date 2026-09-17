import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Pre-flight gate: proves the code on disk is healthy BEFORE it goes live, and reports
// whether the currently RUNNING bridge processes predate it (i.e. a restart is required).
// Run:  node dist/verify-live.mjs

const here = path.dirname(fileURLToPath(import.meta.url));
const toolRoot = path.resolve(here, '..');
let problems = 0;

console.log('--- 1. syntax gate ---');
for (const f of fs.readdirSync(here).filter((x) => x.endsWith('.js') || x.endsWith('.mjs'))) {
    try { execFileSync(process.execPath, ['--check', path.join(here, f)], { cwd: toolRoot, stdio: 'ignore' }); }
    catch { problems++; console.log('FAIL  syntax  ' + f); }
}
console.log(problems === 0 ? 'all files parse' : problems + ' syntax failure(s)');

console.log('--- 2. self-test gate ---');
for (const t of fs.readdirSync(here).filter((x) => x.startsWith('selftest') && x.endsWith('.mjs')).sort()) {
    try {
        execFileSync(process.execPath, [path.join(here, t)], { encoding: 'utf8', timeout: 240000, cwd: toolRoot });
        console.log('PASS  ' + t);
    } catch { problems++; console.log('FAIL  ' + t); }
}

let newest = 0;
for (const f of fs.readdirSync(here)) {
    const m = fs.statSync(path.join(here, f)).mtimeMs;
    if (m > newest) newest = m;
}
console.log('--- 3. freshness vs running processes ---');
console.log('newest dist change: ' + new Date(newest).toISOString());

const ps = "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*antigravity-mcp*' } | ForEach-Object { $_.ProcessId.ToString() + '|' + $_.CreationDate.ToUniversalTime().ToString('o') }";
let procs = [];
try {
    const out = execFileSync('powershell', ['-NoProfile', '-Command', ps], { encoding: 'utf8', timeout: 60000 });
    procs = out.split(/\r?\n/).map((l) => l.trim()).filter(Boolean).map((l) => { const i = l.indexOf('|'); return { pid: Number(l.slice(0, i)), start: new Date(l.slice(i + 1)).getTime() }; });
} catch (e) { console.log('could not enumerate bridge processes: ' + e.message); }

let stale = 0;
for (const p of procs) {
    const isStale = Number.isFinite(p.start) && p.start < newest;
    if (isStale) stale++;
    console.log((isStale ? 'STALE   ' : 'current ') + 'pid ' + p.pid + '  started ' + new Date(p.start).toISOString());
}
if (procs.length === 0) console.log('no running bridge processes found (nothing is serving ag_* right now)');

console.log('--- 4. boot gate (fresh instance) ---');
const child = spawn(process.execPath, [path.join(here, 'index.js')], { stdio: ['pipe', 'pipe', 'pipe'] });
let buf = '';
child.stdout.on('data', (c) => { buf += c; });
child.stderr.on('data', (c) => { buf += c; });
const msgs = [
    { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'verify-live', version: '1' } } },
    { jsonrpc: '2.0', method: 'notifications/initialized' },
    { jsonrpc: '2.0', id: 2, method: 'tools/list' },
].map((m) => JSON.stringify(m)).join(String.fromCharCode(10)) + String.fromCharCode(10);
child.stdin.write(msgs);

const EXPECTED = ['ag_health', 'ag_sessions', 'ag_runs', 'ag_explore', 'ag_research', 'ag_review', 'ag_implement', 'ag_test', 'ag_debug'];
setTimeout(() => {
    try { child.kill('SIGKILL'); } catch {}
    const missing = EXPECTED.filter((n) => buf.indexOf(n) < 0);
    if (missing.length) { problems++; console.log('FAIL  missing tools: ' + missing.join(', ')); }
    else console.log('all ' + EXPECTED.length + ' tools register on a fresh instance');
    console.log('--- verdict ---');
    if (problems > 0) console.log('GATE FAILED: ' + problems + ' problem(s). Do NOT deploy.');
    else if (stale > 0) console.log('GATE PASSED, but ' + stale + ' running process(es) predate the newest change. RESTART REQUIRED to go live.');
    else console.log('GATE PASSED and running processes are current.');
    process.exit(problems > 0 ? 1 : 0);
}, 9000);

