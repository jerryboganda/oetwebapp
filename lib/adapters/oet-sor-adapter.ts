/**
 * Adapter: MockReport → OetStatementOfResults.
 *
 * Maps whatever we have on a MockReport into the exact shape the pixel-
 * faithful OET SoR card expects. Falls back to safe defaults (placeholder
 * dates, candidate-generated number, practice-mode venue) when the mock
 * report doesn't carry a particular field.
 *
 * Clamps any non-numeric or out-of-range subtest score to a valid 0–500
 * value (step 10). The canonical scoring module enforces this elsewhere;
 * this adapter is defensive for the rendering path only.
 */

import type { OetStatementOfResults } from '@/components/domain/OetStatementOfResultsCard';
import type { MockReport } from '@/lib/mock-data';
import { OET_SCALED_MAX, OET_SCALED_MIN } from '@/lib/scoring';

export interface OetSorAdapterInputs {
  report: MockReport;
  candidate?: {
    name?: string;
    candidateNumber?: string;
    dateOfBirth?: string;
    gender?: 'Male' | 'Female' | 'Non-binary' | 'Prefer not to say';
  };
  profession?: string;
  country?: string;
}

const REQUIRED_SOR_SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;
const FINAL_REVIEW_STATES = new Set(['completed', 'reviewed', 'final', 'released']);
const LEGACY_SUBTEST_IDS: Record<string, (typeof REQUIRED_SOR_SUBTESTS)[number]> = {
  'g-l': 'listening',
  'g-r': 'reading',
  'g-w': 'writing',
  'g-s': 'speaking',
};

function normalizeSubtestKey(value: string | undefined): string {
  return value?.trim().toLowerCase().replace(/\s+/g, '_') ?? '';
}

function reportSubtestKey(subtest: MockReport['subTests'][number]): string {
  const id = normalizeSubtestKey(subtest.id);
  if (LEGACY_SUBTEST_IDS[id]) return LEGACY_SUBTEST_IDS[id];
  if (REQUIRED_SOR_SUBTESTS.includes(id as (typeof REQUIRED_SOR_SUBTESTS)[number])) return id;
  const name = normalizeSubtestKey(subtest.name);
  if (REQUIRED_SOR_SUBTESTS.includes(name as (typeof REQUIRED_SOR_SUBTESTS)[number])) return name;
  return id;
}

function subtestsByCanonicalKey(report: MockReport) {
  return Object.fromEntries(report.subTests.map((subtest) => [reportSubtestKey(subtest), subtest]));
}

export function isMockReportStatementOfResultsReady(report: MockReport): boolean {
  const byId = subtestsByCanonicalKey(report);
  return REQUIRED_SOR_SUBTESTS.every((id) => {
    const subtest = byId[id];
    if (!subtest) return false;
    if (subtest.reviewState && !FINAL_REVIEW_STATES.has(subtest.reviewState)) return false;
    if (subtest.state && ['queued', 'in_review', 'awaiting_payment', 'pending', 'not_completed'].includes(subtest.state)) return false;
    const raw = subtest.scaledScore ?? subtest.score;
    const score = Number(String(raw).trim());
    return Number.isFinite(score);
  });
}

export function mockReportToStatementOfResults(inputs: OetSorAdapterInputs): OetStatementOfResults {
  const { report, candidate, profession, country } = inputs;

  const byId = subtestsByCanonicalKey(report);

  const pickScore = (id: string): number => {
    const raw = byId[id]?.scaledScore ?? byId[id]?.score ?? '0';
    const n = Number(String(raw).trim());
    if (!Number.isFinite(n)) return 0;
    const clamped = Math.min(OET_SCALED_MAX, Math.max(OET_SCALED_MIN, Math.round(n / 10) * 10));
    return clamped;
  };

  // Candidate number: either the one we already have, or a stable
  // pseudo-number derived from the report id so the same mock always yields
  // the same displayed number.
  const fallbackCandidateNumber = (() => {
    const hash = [...report.id].reduce((acc, c) => acc + c.charCodeAt(0), 0);
    const a = String(100000 + (hash % 800000)).padStart(6, '0');
    const b = String(Math.abs(hash * 31) % 1_000_000).padStart(6, '0');
    return `OET-${a}-${b}`;
  })();

  return {
    candidate: {
      name: candidate?.name ?? 'Practice Candidate',
      candidateNumber: candidate?.candidateNumber ?? fallbackCandidateNumber,
      dateOfBirth: candidate?.dateOfBirth,
      gender: candidate?.gender,
    },
    venue: {
      name: 'OET Prep — Practice Mock',
      number: `PREP-${report.id.slice(0, 6).toUpperCase()}`,
      country: country ?? 'United Kingdom',
    },
    test: {
      date: formatDate(report.date),
      deliveryMode: 'OET on computer (practice)',
      profession: titleCase(profession ?? 'Medicine'),
    },
    scores: {
      listening: pickScore('listening'),
      reading: pickScore('reading'),
      speaking: pickScore('speaking'),
      writing: pickScore('writing'),
    },
    isPractice: true,
    issuedAt: new Date().toISOString(),
  };
}

function formatDate(input: string): string {
  const d = new Date(input);
  if (Number.isNaN(d.getTime())) return input;
  return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

function titleCase(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1).toLowerCase();
}
