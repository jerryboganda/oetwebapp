import { extractJsonObject, scanTopLevelObjects, parseWorkerResult } from './schemas.js';

const TICK = String.fromCharCode(96);
let f = 0;
const ok = (n, c, x) => { if (c) console.log('PASS: ' + n); else { f++; console.log('FAIL: ' + n + (x ? ' :: ' + x : '')); } };

const innerObj = { status: 'SUCCESS', role: 'explore', summary: 'done', evidence: [{ path: 'a', finding: 'b' }], filesRead: ['a'], filesChanged: [], commandsRun: [], tests: [], risks: [], blockers: [], recommendedNextStep: 'none', confidence: 'high' };
const inner = JSON.stringify(innerObj);
const dup = JSON.stringify({ status: 'SUCCESS', role: 'explore', summary: 'first-pass' });

ok('single object still parses (no regression)', (extractJsonObject(inner) || {}).summary === 'done');
ok('scanner finds 2 top-level objects in a concatenated payload', scanTopLevelObjects(dup + inner).length === 2, String(scanTopLevelObjects(dup + inner).length));
ok('scanner ignores nested braces', scanTopLevelObjects(JSON.stringify({ a: { b: { c: 1 } } })).length === 1);
ok('scanner ignores braces inside string literals', scanTopLevelObjects(JSON.stringify({ summary: 'has { and } inside' })).length === 1);
ok('scanner returns none for garbage', scanTopLevelObjects('not json at all').length === 0);
ok('concatenated objects resolve to the LAST complete result', (extractJsonObject(dup + inner) || {}).summary === 'done', JSON.stringify(extractJsonObject(dup + inner)));

const envelope = JSON.stringify({ conversation_id: 'abc-123', status: 'SUCCESS', response: dup + inner });
const parsed = parseWorkerResult('explore', envelope) || {};
ok('parseWorkerResult returns structured status SUCCESS', parsed.status === 'SUCCESS', parsed.status);
ok('REGRESSION FIXED: evidence populated not empty', Array.isArray(parsed.evidence) && parsed.evidence.length === 1, JSON.stringify(parsed.evidence));
ok('filesRead preserved', Array.isArray(parsed.filesRead) && parsed.filesRead.length === 1);
ok('markdown fence path still works', (extractJsonObject(TICK + TICK + TICK + 'json' + String.fromCharCode(10) + inner + String.fromCharCode(10) + TICK + TICK + TICK) || {}).summary === 'done');
ok('unparseable input returns null', extractJsonObject('total garbage') === null);

console.log(f === 0 ? '=== ALL PARSER SELF-TESTS PASSED ===' : '=== ' + f + ' ASSERTION(S) FAILED ===');
process.exit(f ? 1 : 0);

