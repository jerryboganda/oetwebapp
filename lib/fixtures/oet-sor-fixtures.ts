import type { OetStatementOfResults } from '@/components/domain/OetStatementOfResultsCard';

/**
 * Fixture data for the OET Statement of Results card. Mirrors the scores
 * shown in the reference screenshots in
 * `Project Real Content/Create Similar Table Formats for Results to show
 * to Candidates/`. Used for Storybook, visual regression, and unit tests.
 */

export const SOR_FIXTURE_MEDICINE_UK: OetStatementOfResults = {
  candidate: {
    name: 'Ms Sarah Miller',
    candidateNumber: 'OET-199254-502751',
    dateOfBirth: '12 Apr 1992',
    gender: 'Female',
  },
  venue: {
    name: 'OET Prep — Practice Mock',
    number: 'PREP-0001',
    country: 'United Kingdom',
  },
  test: {
    date: '25 Jun 2022',
    deliveryMode: 'OET on computer (practice)',
    profession: 'Medicine',
  },
  scores: {
    listening: 430,
    reading: 420,
    speaking: 350,
    writing: 370,
  },
  isPractice: true,
  issuedAt: '2022-06-25T14:00:00Z',
};

export const SOR_FIXTURE_MEDICINE_USA: OetStatementOfResults = {
  candidate: {
    name: 'Mr Leo Bennett',
    candidateNumber: 'OET-284011-118322',
    dateOfBirth: '03 Jan 1988',
    gender: 'Male',
  },
  venue: {
    name: 'OET Prep — Practice Mock',
    number: 'PREP-0002',
    country: 'United States',
  },
  test: {
    date: '11 Mar 2023',
    deliveryMode: 'OET on computer (practice)',
    profession: 'Medicine',
  },
  scores: {
    // USA writing pass is 300 (Grade C+). This fixture is intentionally
    // a pass-in-USA, would-fail-in-UK scenario.
    listening: 390,
    reading: 410,
    speaking: 430,
    writing: 300,
  },
  isPractice: true,
  issuedAt: '2023-03-11T09:15:00Z',
};

export const SOR_FIXTURE_BORDERLINE: OetStatementOfResults = {
  candidate: {
    name: 'Ms Priya Shah',
    candidateNumber: 'OET-500001-900001',
  },
  venue: { name: 'OET Prep — Practice Mock', number: 'PREP-0003', country: 'Australia' },
  test: { date: '18 Apr 2026', deliveryMode: 'OET on computer (practice)', profession: 'Nursing' },
  scores: { listening: 350, reading: 350, speaking: 350, writing: 350 },
  isPractice: true,
  issuedAt: '2026-04-18T10:00:00Z',
};

export const SOR_FIXTURE_PERFECT: OetStatementOfResults = {
  candidate: { name: 'Dr Aiko Tanaka', candidateNumber: 'OET-900000-000001' },
  venue: { name: 'OET Prep — Practice Mock', number: 'PREP-9999', country: 'Ireland' },
  test: { date: '01 Jan 2026', deliveryMode: 'OET on computer (practice)', profession: 'Medicine' },
  scores: { listening: 500, reading: 500, speaking: 500, writing: 500 },
  isPractice: true,
  issuedAt: '2026-01-01T00:00:00Z',
};

export const SOR_FIXTURE_FAIL: OetStatementOfResults = {
  candidate: { name: 'Mr Tom Carter', candidateNumber: 'OET-100001-000002' },
  venue: { name: 'OET Prep — Practice Mock', number: 'PREP-0004', country: 'Canada' },
  test: { date: '05 Feb 2026', deliveryMode: 'OET on computer (practice)', profession: 'Dentistry' },
  scores: { listening: 180, reading: 220, speaking: 260, writing: 140 },
  isPractice: true,
  issuedAt: '2026-02-05T12:00:00Z',
};
