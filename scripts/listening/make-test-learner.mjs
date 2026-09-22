import { randomBytes } from 'node:crypto';
import { writeFileSync } from 'node:fs';
const B = 'https://api.oetwithdrhesham.co.uk';
const s = await fetch(`${B}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: process.env.OET_ADMIN_EMAIL, password: process.env.OET_ADMIN_PASSWORD, rememberMe: true }) });
const tok = (await s.json()).accessToken;
const H = { Authorization: `Bearer ${tok}`, Accept: 'application/json', 'Content-Type': 'application/json' };
const api = async (method, path, json) => { const r = await fetch(B + path, { method, headers: H, body: json !== undefined ? JSON.stringify(json) : undefined }); const t = await r.text(); if (!r.ok) throw new Error(`${method} ${path} -> ${r.status}: ${t.slice(0, 300)}`); return t ? JSON.parse(t) : null; };

const email = 'qa-listening-audit-2026-09-22@oet-prep.dev';
const password = 'Qa-' + randomBytes(9).toString('base64url') + '9!';

const user = await api('POST', '/v1/admin/users', { Name: 'QA Listening Audit', Email: email, Role: 'learner', ProfessionId: 'medicine', MobileNumber: null, TargetExamDate: '2026-12-01', Password: password, SendInvite: false });
console.log('created user:', JSON.stringify(user).slice(0, 300));
const userId = user.id ?? user.userId ?? user.Id;
if (!userId) throw new Error('no userId in response: ' + JSON.stringify(user));

const grant = await api('POST', `/v1/admin/users/${userId}/access/packages`, { PlanCode: 'crash-course', StartsAt: null, ExpiresAt: null, MakePrimary: true, GrantIncludedCredits: true, OverrideProfessionMismatch: true });
console.log('granted package:', JSON.stringify(grant).slice(0, 300));

// confirm sign-in works
const check = await fetch(`${B}/v1/auth/sign-in`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password, rememberMe: true }) });
console.log('learner sign-in check:', check.status);

writeFileSync('scripts/listening/state/repair/test-learner.json', JSON.stringify({ email, password, userId, createdAt: new Date().toISOString(), plan: 'crash-course' }, null, 1));
console.log('saved scripts/listening/state/repair/test-learner.json (email + password; local only, gitignored)');
