import { render, screen } from '@testing-library/react';
import type { LintFinding } from '@/lib/rulebook';
import type { SpeakingTranscriptReview } from '@/lib/mock-data';

const {
  mockUseParams,
  mockFetchTranscript,
  mockFetchSettingsSection,
  mockAuditSpeakingTranscript,
  mockInferSpeakingCardType,
  mockTrack,
} = vi.hoisted(() => ({
  mockUseParams: vi.fn(),
  mockFetchTranscript: vi.fn(),
  mockFetchSettingsSection: vi.fn(),
  mockAuditSpeakingTranscript: vi.fn(),
  mockInferSpeakingCardType: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useParams: mockUseParams,
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchTranscript: mockFetchTranscript,
  fetchSettingsSection: mockFetchSettingsSection,
}));

vi.mock('@/lib/rulebook', () => ({
  auditSpeakingTranscript: mockAuditSpeakingTranscript,
  inferSpeakingCardType: mockInferSpeakingCardType,
}));

import SpeakingTranscriptPage from './page';

const findings: LintFinding[] = [
  {
    ruleId: 'RULE_22',
    severity: 'critical',
    message: 'Did not confirm patient identity before discussing results.',
    quote: 'So your test results came back...',
  },
];

function baseReview(): SpeakingTranscriptReview {
  return {
    title: 'Role-play 1 review',
    date: '2026-09-01',
    duration: 240,
    transcript: [
      { id: 'line-1', speaker: 'Candidate', text: 'Hello, how are you today?', startTime: 0, endTime: 3 },
    ],
    audioAvailable: false,
    waveformPeaks: [],
  };
}

describe('SpeakingTranscriptPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseParams.mockReturnValue({ id: 'result-1' });
    mockInferSpeakingCardType.mockReturnValue('first_visit');
    mockAuditSpeakingTranscript.mockReturnValue(findings);
    mockFetchTranscript.mockResolvedValue(baseReview());
    mockFetchSettingsSection.mockResolvedValue({ values: { lowBandwidthMode: false } });
  });

  it('shows the finding but never an internal rule id or rulebook link to a candidate', async () => {
    render(<SpeakingTranscriptPage />);

    // Wait for the async transcript fetch to resolve and findings to render.
    expect(await screen.findByText(/Did not confirm patient identity/i)).toBeInTheDocument();

    // The message is shown; the internal rule id and any rulebook link are not.
    expect(screen.queryByText('RULE_22')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/RULE_\d+/);
    expect(document.querySelector('a[href^="/speaking/rulebook"]')).toBeNull();
  });
});
