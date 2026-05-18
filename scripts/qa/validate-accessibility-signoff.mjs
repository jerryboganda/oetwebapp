#!/usr/bin/env node

import fs from 'node:fs';
import path from 'node:path';

const ZERO_KEYS = ['ACCESSIBILITY_AXE_CRITICAL', 'ACCESSIBILITY_AXE_SERIOUS'];
const PASS_KEYS = [
  'ACCESSIBILITY_NVDA_SIGNOFF',
  'ACCESSIBILITY_VOICEOVER_SIGNOFF',
  'ACCESSIBILITY_AUTH_SIGN_IN',
  'ACCESSIBILITY_LEARNER_DASHBOARD',
  'ACCESSIBILITY_LEARNER_BILLING',
  'ACCESSIBILITY_LEARNER_IMMERSIVE_FLOW',
  'ACCESSIBILITY_EXPERT_REVIEW_SUBMIT',
  'ACCESSIBILITY_ADMIN_AUDIT_LOGS',
  'ACCESSIBILITY_ADMIN_USER_CREDIT',
];
const REQUIRED_VALUE_KEYS = [
  'ACCESSIBILITY_REVIEWER',
  'ACCESSIBILITY_REVIEWED_AT_UTC',
  'ACCESSIBILITY_PLAYWRIGHT_REPORT',
  'ACCESSIBILITY_MANUAL_EVIDENCE_URL',
];
const REQUIRED_KEYS = [...ZERO_KEYS, ...PASS_KEYS, ...REQUIRED_VALUE_KEYS];
const PLACEHOLDER_PATTERN = /(<[^>]+>|placeholder|todo|tbd|changeme|replace-with|example\.invalid)/i;
const BAD_STATUS_PATTERN = /^(fail|failed|blocked|n\/a|na|skip|skipped)$/i;
const ARTIFACT_OR_URL_PATTERN = /^(https?:\/\/|artifact:|artifact:\/\/|gh-run:|s3:\/\/|gs:\/\/).+/i;
const ISO_UTC_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/;

function parseEnvFile(filePath) {
  const entries = new Map();
  const duplicates = [];
  const lines = fs.readFileSync(filePath, 'utf8').split(/\r?\n/);

  lines.forEach((line, index) => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) {
      return;
    }

    const equalsIndex = line.indexOf('=');
    if (equalsIndex <= 0) {
      throw new Error(`Invalid env line ${index + 1}: ${line}`);
    }

    const key = line.slice(0, equalsIndex).trim();
    const value = line.slice(equalsIndex + 1).trim();
    if (!/^[A-Z0-9_]+$/.test(key)) {
      throw new Error(`Invalid env key on line ${index + 1}: ${key}`);
    }

    if (entries.has(key)) {
      duplicates.push(key);
    }

    entries.set(key, value);
  });

  if (duplicates.length > 0) {
    throw new Error(`Duplicate accessibility signoff keys are not allowed: ${[...new Set(duplicates)].join(', ')}`);
  }

  return entries;
}

function requireValue(entries, key) {
  const value = entries.get(key);
  if (!value) {
    throw new Error(`${key} is required and must be non-empty.`);
  }
  if (PLACEHOLDER_PATTERN.test(value)) {
    throw new Error(`${key} contains a placeholder value.`);
  }
  if (BAD_STATUS_PATTERN.test(value)) {
    throw new Error(`${key} contains a non-passing status: ${value}`);
  }
  return value;
}

function validate(filePath) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`Accessibility signoff file does not exist: ${filePath}`);
  }

  const entries = parseEnvFile(filePath);
  for (const key of REQUIRED_KEYS) {
    if (!entries.has(key)) {
      throw new Error(`Missing required accessibility signoff key: ${key}`);
    }
  }

  for (const key of ZERO_KEYS) {
    const value = requireValue(entries, key);
    if (value !== '0') {
      throw new Error(`${key} must be 0; found ${value}.`);
    }
  }

  for (const key of PASS_KEYS) {
    const value = requireValue(entries, key);
    if (value.toLowerCase() !== 'pass') {
      throw new Error(`${key} must be pass; found ${value}.`);
    }
  }

  for (const key of REQUIRED_VALUE_KEYS) {
    requireValue(entries, key);
  }

  const reviewedAt = entries.get('ACCESSIBILITY_REVIEWED_AT_UTC');
  if (!ISO_UTC_PATTERN.test(reviewedAt) || Number.isNaN(Date.parse(reviewedAt))) {
    throw new Error('ACCESSIBILITY_REVIEWED_AT_UTC must use UTC ISO format YYYY-MM-DDTHH:MM:SSZ.');
  }

  for (const key of ['ACCESSIBILITY_PLAYWRIGHT_REPORT', 'ACCESSIBILITY_MANUAL_EVIDENCE_URL']) {
    const value = entries.get(key);
    if (!ARTIFACT_OR_URL_PATTERN.test(value)) {
      throw new Error(`${key} must be a URL or artifact reference.`);
    }
  }
}

const filePath = path.resolve(process.argv[2] || 'release-evidence/accessibility-signoff.env');

try {
  validate(filePath);
  console.log(`[accessibility-signoff] validated ${filePath}`);
} catch (error) {
  console.error(`[accessibility-signoff] ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}
