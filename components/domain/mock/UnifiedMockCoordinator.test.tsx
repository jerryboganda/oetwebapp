import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { UnifiedMockCoordinator } from '@/components/domain/mock/UnifiedMockCoordinator';
import type { MockSession } from '@/lib/mock-data';

const mockPush = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockPush,
  }),
}));

vi.mock('@/lib/api', () => ({
  startMockSection: vi.fn().mockResolvedValue({ launchRoute: '/reading/paper/mock-paper-1' }),
  completeMockSection: vi.fn().mockResolvedValue({}),
  submitMockSession: vi.fn().mockResolvedValue({}),
}));

describe('UnifiedMockCoordinator', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  const sampleSession: MockSession = {
    sessionId: 'mock-session-test-1',
    state: 'in_progress',
    resumeRoute: '/mocks/session/mock-session-test-1',
    config: {
      id: 'mock-cfg-1',
      type: 'full',
      title: 'Full 4-Skill OET Simulation (Medicine)',
      profession: 'Medicine',
      mode: 'exam',
      strictTimer: true,
      includeReview: false,
      reviewSelection: 'none',
      deliveryMode: 'computer',
    },
    sectionStates: [
      {
        id: 'sec-listening',
        subtest: 'listening',
        title: 'Listening Sub-Test (Part A, B, C)',
        state: 'completed',
        status: 'completed',
        reviewAvailable: false,
        reviewSelected: false,
        launchRoute: '/listening/paper/lp-1',
        timeLimitMinutes: 45,
      },
      {
        id: 'sec-reading',
        subtest: 'reading',
        title: 'Reading Sub-Test (Part A, B, C)',
        state: 'in_progress',
        status: 'in_progress',
        reviewAvailable: false,
        reviewSelected: false,
        launchRoute: '/reading/paper/rp-1',
        timeLimitMinutes: 60,
      },
      {
        id: 'sec-writing',
        subtest: 'writing',
        title: 'Writing Sub-Test (Referral Letter)',
        state: 'not_started',
        status: 'not_started',
        reviewAvailable: false,
        reviewSelected: false,
        launchRoute: '/writing/paper/wp-1',
        timeLimitMinutes: 45,
      },
      {
        id: 'sec-speaking',
        subtest: 'speaking',
        title: 'Speaking Sub-Test (2 Role-Play Cards)',
        state: 'not_started',
        status: 'not_started',
        reviewAvailable: false,
        reviewSelected: false,
        launchRoute: '/speaking/paper/sp-1',
        timeLimitMinutes: 20,
      },
    ],
  };

  it('renders the 4-Skill Unified Mock Orchestrator header', () => {
    render(<UnifiedMockCoordinator session={sampleSession} />);
    expect(screen.getByText('4-Skill Unified Mock Orchestrator')).toBeDefined();
    expect(screen.getByText('1 / 4 Sub-Tests Complete')).toBeDefined();
  });

  it('renders all 4 sub-tests in sequential pipeline', () => {
    render(<UnifiedMockCoordinator session={sampleSession} />);
    expect(screen.getByText('Listening Sub-Test (Part A, B, C)')).toBeDefined();
    expect(screen.getByText('Reading Sub-Test (Part A, B, C)')).toBeDefined();
    expect(screen.getByText('Writing Sub-Test (Referral Letter)')).toBeDefined();
    expect(screen.getByText('Speaking Sub-Test (2 Role-Play Cards)')).toBeDefined();
  });

  it('shows Resume Now button on in_progress subtest', () => {
    render(<UnifiedMockCoordinator session={sampleSession} />);
    expect(screen.getByRole('button', { name: /Resume Now/i })).toBeDefined();
  });

  it('opens transition modal when starting not_started sub-test', () => {
    render(<UnifiedMockCoordinator session={sampleSession} />);
    const startButtons = screen.getAllByRole('button', { name: /Start Sub-Test/i });
    expect(startButtons.length).toBeGreaterThan(0);
    fireEvent.click(startButtons[0]);

    expect(screen.getByText(/Sub-Test Protocol & Invariants/i)).toBeDefined();
  });
});
