import { render, screen, waitFor } from '@testing-library/react';
import type { WritingResult } from '@/lib/mock-data';

const { mockFetchWritingResult, mockTrack } = vi.hoisted(() => ({
  mockFetchWritingResult: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: { children: React.ReactNode; href?: string } & Record<string, unknown>) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams('id=we-1'),
  usePathname: () => '/writing/feedback',
}));

vi.mock('@/components/layout/app-shell', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/lib/api', () => ({
  fetchWritingResult: mockFetchWritingResult,
}));

import WritingDetailedFeedback from './page';

describe('Writing detailed feedback page — rule-cited findings', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    const result: WritingResult = {
      id: 'we-1',
      taskId: 'wt-1',
      taskTitle: 'Routine referral — dermatology',
      profession: 'nursing',
      examFamilyCode: 'oet',
      examFamilyLabel: 'OET',
      estimatedScoreRange: '350–400',
      estimatedGradeRange: 'B',
      confidenceBand: 'Medium',
      confidenceLabel: 'Medium confidence practice estimate',
      learnerDisclaimer: 'Practice estimate only.',
      methodLabel: 'AI-assisted',
      provenanceLabel: 'Practice estimate',
      humanReviewRecommended: false,
      escalationRecommended: false,
      isOfficialScore: false,
      topStrengths: [],
      topIssues: [],
      submittedAt: new Date().toISOString(),
      evalStatus: 'completed',
      criteria: [
        {
          name: 'Content',
          score: 5,
          maxScore: 7,
          grade: 'B',
          explanation: 'Solid coverage of the request.',
          omissions: [],
          unnecessaryDetails: [],
          revisionSuggestions: [],
          strengths: [],
          issues: [],
          anchoredComments: [
            {
              id: 'fi-1',
              text: 'Patient is allergic to penicillin',
              comment: 'Mention the allergy in the opening paragraph for safety.',
              ruleId: 'R03.4',
              severity: 'critical',
              source: 'rule_engine',
              suggestedFix: 'Move the allergy line to the first paragraph.',
            },
          ],
        },
      ],
    };

    mockFetchWritingResult.mockResolvedValue(result);
  });

  it('renders a rule badge linking to the rulebook detail page when ruleId is present', async () => {
    render(<WritingDetailedFeedback />);

    const badge = await waitFor(() => screen.getByTestId('rule-badge'));
    expect(badge).toHaveTextContent('[R03.4]');
    expect(badge).toHaveAttribute('href', '/writing/rulebook/R03.4?profession=nursing');

    // Severity pill, source label, and suggested-fix block also render.
    expect(screen.getByText('critical')).toBeInTheDocument();
    expect(screen.getByText(/Rule check/i)).toBeInTheDocument();
    expect(screen.getByText(/Suggested fix/i)).toBeInTheDocument();
    expect(screen.getByText(/Move the allergy line/i)).toBeInTheDocument();
  });
});
