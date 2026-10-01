import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import type { PlacementResultReport } from '@/lib/api/placement';
import { QUEUED_FOR_REVIEW_NOTE, ResultReportCard } from './result-report-card';

function report(overrides: Partial<PlacementResultReport> = {}): PlacementResultReport {
  return {
    session_id: 'ses_1',
    profileType: 'full',
    skills: [
      {
        skill: 'RD',
        status: 'measured',
        band: 'B1',
        range: null,
        notes: [],
        // The engine serializes camelCase — these must render.
        canDo: ['Can understand the main points of clear standard texts.'],
        growthAreas: ['Strengthen inference of implied meaning.'],
      },
      { skill: 'LSN', status: 'measured', band: 'A2', range: null, notes: [] },
      {
        skill: 'SPK',
        status: 'insufficient_evidence',
        band: null,
        range: null,
        // The exact note the engine leads a pending Speaking/Writing skill with.
        notes: ['Queued for human review - not yet scored.'],
      },
      { skill: 'WRT', status: 'not_measured', band: null, range: null, notes: [] },
    ],
    diagnostics: {
      languageSystems: { band: 'B1', range: null, constructsStrong: ['present simple'], constructsWeak: ['articles'] },
      language_systems: {
        grammar: { correct: 5, total: 7, strengths: ['past simple'], weaknesses: ['articles'] },
        vocabulary: { correct: 3, total: 5, strengths: ['collocation'], weaknesses: ['word formation'] },
      },
    },
    headline: { kind: 'none', band: null, range: null },
    confidence: 'Low',
    confidenceReasons: ['Receptive evidence only so far.'],
    readiness: null,
    retestAdvice: 'Retest after 2–4 weeks of focused study.',
    ...overrides,
  };
}

describe('ResultReportCard', () => {
  it('shows the four skills only — Language Systems is a diagnostic, never a fifth skill', () => {
    render(<ResultReportCard title="Your placement profile" report={report()} />);

    const skills = screen.getByRole('region', { name: /skill profile/i });
    expect(within(skills).getAllByRole('article')).toHaveLength(4);
    expect(within(skills).queryByText(/language systems/i)).toBeNull();

    expect(screen.getByRole('heading', { name: /grammar & vocabulary diagnostics/i })).toBeInTheDocument();
    expect(screen.getByText(/5 of 7 correct/)).toBeInTheDocument();
  });

  it('renders camelCase can-do and growth statements for measured skills', () => {
    render(<ResultReportCard title="Profile" report={report()} />);
    expect(screen.getByText(/understand the main points/i)).toBeInTheDocument();
    expect(screen.getByText(/strengthen inference/i)).toBeInTheDocument();
  });

  it('states review and unmeasured skills honestly instead of a band or a raw status', () => {
    render(<ResultReportCard title="Profile" report={report()} />);
    expect(screen.getByText(/being reviewed by dr hesham's team/i)).toBeInTheDocument();
    expect(screen.getByText(/not taken yet/i)).toBeInTheDocument();
    expect(screen.queryByText('not_measured')).toBeNull();
    expect(screen.queryByText('insufficient_evidence')).toBeNull();
    expect(screen.getByText(/partial profile/i)).toBeInTheDocument();
  });

  it('takes the page h1 when asked, with its sections one level below', () => {
    render(<ResultReportCard title="Your placement result" report={report()} embedded headingLevel={1} />);
    expect(screen.getByRole('heading', { level: 1, name: 'Your placement result' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: /grammar & vocabulary diagnostics/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Reading' })).toBeInTheDocument();
  });

  it('pins the under-review signal to the exact engine note', () => {
    expect(QUEUED_FOR_REVIEW_NOTE).toBe('Queued for human review - not yet scored.');
  });

  it('shows any other insufficient-evidence note as-is — mentioning "review" is not the pending signal', () => {
    const note = 'The recording was too short to assess. Please review your microphone settings and retry.';
    render(
      <ResultReportCard
        title="Profile"
        report={report({
          skills: [
            { skill: 'RD', status: 'measured', band: 'B1', range: null, notes: [] },
            { skill: 'LSN', status: 'measured', band: 'A2', range: null, notes: [] },
            { skill: 'SPK', status: 'insufficient_evidence', band: null, range: null, notes: [note] },
            { skill: 'WRT', status: 'measured', band: 'B1', range: null, notes: [] },
          ],
        })}
      />,
    );
    expect(screen.queryByText(/being reviewed by dr hesham's team/i)).toBeNull();
    expect(screen.getByText(/not enough evidence yet/i)).toBeInTheDocument();
    expect(screen.getByText(note)).toBeInTheDocument();
  });

  it('never shows High confidence and labels an uneven profile as a range', () => {
    render(
      <ResultReportCard
        title="Profile"
        report={report({ confidence: 'High', headline: { kind: 'uneven', band: null, range: ['A2', 'B2'] } })}
      />,
    );
    expect(screen.queryByText(/high/i)).toBeNull();
    expect(screen.getByText(/confidence: low/i)).toBeInTheDocument();
    expect(screen.getByText(/uneven profile: a2–b2/i)).toBeInTheDocument();
  });
});
