#!/usr/bin/env node

const requiredLines = [
  ['ACCESSIBILITY_AXE_CRITICAL', '0'],
  ['ACCESSIBILITY_AXE_SERIOUS', '0'],
  ['ACCESSIBILITY_NVDA_SIGNOFF', 'pass'],
  ['ACCESSIBILITY_VOICEOVER_SIGNOFF', 'pass'],
  ['ACCESSIBILITY_AUTH_SIGN_IN', 'pass'],
  ['ACCESSIBILITY_LEARNER_DASHBOARD', 'pass'],
  ['ACCESSIBILITY_LEARNER_BILLING', 'pass'],
  ['ACCESSIBILITY_LEARNER_IMMERSIVE_FLOW', 'pass'],
  ['ACCESSIBILITY_EXPERT_REVIEW_SUBMIT', 'pass'],
  ['ACCESSIBILITY_ADMIN_AUDIT_LOGS', 'pass'],
  ['ACCESSIBILITY_ADMIN_USER_CREDIT', 'pass'],
  ['ACCESSIBILITY_REVIEWER', '<reviewer-name>'],
  ['ACCESSIBILITY_REVIEWED_AT_UTC', '<YYYY-MM-DDTHH:MM:SSZ>'],
  ['ACCESSIBILITY_PLAYWRIGHT_REPORT', '<https://github.com/.../actions/runs/... or artifact:...>'],
  ['ACCESSIBILITY_MANUAL_EVIDENCE_URL', '<https://.../manual-nvda-voiceover-evidence>'],
];

console.log(`# accessibility-signoff.env
# Fill this file only after automated axe has zero critical/serious issues and
# a human has completed manual NVDA and VoiceOver checks for each launch flow.
# All pass/fail fields must be exactly "pass"; do not commit real private notes.
`);

for (const [key, value] of requiredLines) {
  console.log(`${key}=${value}`);
}
