import { render, screen, within } from '@testing-library/react';
import type { Submission } from '@/lib/mock-data';

const {
  mockFetchSpeakingHome,
  mockFetchSubmissions,
  mockFetchMockReports,
  mockLearnerListSpeakingSharedResources,
  mockTrack,
  mockUseEntitlementSnapshot,
  mockListFreeSamples,
  mockPush,
} = vi.hoisted(() => ({
  mockFetchSpeakingHome: vi.fn(),
  mockFetchSubmissions: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockLearnerListSpeakingSharedResources: vi.fn(),
  mockTrack: vi.fn(),
  mockUseEntitlementSnapshot: vi.fn(),
  mockListFreeSamples: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('next/navigation', () => ({
  usePathname: () => '/speaking',
  useRouter: () => ({
    push: mockPush,
    replace: vi.fn(),
    prefetch: vi.fn(),
    refresh: vi.fn(),
    back: vi.fn(),
    forward: vi.fn(),
  }),
}));

vi.mock('@/components/layout', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  LearnerWorkspaceContainer: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div data-testid="learner-workspace-container" className={className}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    loading: false,
    user: {
      userId: 'learner-1',
      role: 'learner',
      activeProfessionId: 'medicine',
    },
  }),
}));

vi.mock('@/lib/query/hooks', () => ({
  useEntitlementSnapshot: (...args: unknown[]) => mockUseEntitlementSnapshot(...args),
}));

// '@/lib/api' is mocked below without `apiClient`, so the free-sample lookup must be mocked too.
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));

vi.mock('@/lib/api', () => ({
  fetchSpeakingHome: mockFetchSpeakingHome,
  fetchSubmissions: mockFetchSubmissions,
  fetchMockReports: mockFetchMockReports,
  learnerListSpeakingSharedResources: mockLearnerListSpeakingSharedResources,
  downloadSpeakingSharedResourceMedia: vi.fn(),
}));

import SpeakingPage from './page';

describe('Speaking page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Unknown entitlement (loading/failed) must fail OPEN: Book a Tutor stays a live link.
    mockUseEntitlementSnapshot.mockReturnValue({ data: undefined });
    // No free sample on offer by default: the launcher renders nothing.
    mockListFreeSamples.mockResolvedValue([]);

    mockFetchSpeakingHome.mockResolvedValue({
      recommendedRolePlay: {
        id: 'sp-1',
        contentId: 'sp-1',
        title: 'Breaking Bad News - Cancer Diagnosis',
        criteriaFocus: 'appropriateness, grammar expression',
        profession: 'Clinical role play',
        duration: '20 mins',
        estimatedDurationMinutes: 20,
      },
      featuredTasks: [
        {
          id: 'sp-1',
          contentId: 'sp-1',
          title: 'Breaking Bad News - Cancer Diagnosis',
          profession: 'Clinical role play',
          duration: '20 mins',
          estimatedDurationMinutes: 20,
          criteriaFocus: 'appropriateness',
          difficulty: 'Medium',
          scenarioType: 'Role play',
        },
        {
          id: 'sp-2',
          contentId: 'sp-2',
          title: 'Patient Handover - Post-Op Recovery',
          profession: 'Nursing',
          duration: '20 mins',
          estimatedDurationMinutes: 20,
          criteriaFocus: 'fluency, appropriateness',
          difficulty: 'Medium',
          scenarioType: 'Clinical handover',
        },
        {
          id: 'sp-3',
          contentId: 'sp-3',
          title: 'Discharge Advice - Asthma',
          profession: 'Medicine',
          duration: '15 mins',
          estimatedDurationMinutes: 15,
          criteriaFocus: 'clarity',
          difficulty: 'Easy',
          scenarioType: 'Discharge advice',
        },
      ],
      drillGroups: [
        {
          id: 'pronunciation',
          title: 'Pronunciation drills',
          items: [{ id: 'dr-1', title: 'Stress important treatment words', route: '/speaking/phrasing/se-001' }],
        },
        {
          id: 'empathy_clarification',
          title: 'Empathy and clarification drills',
          items: [{ id: 'dr-2', title: 'Clarify concerns without losing structure', route: '/speaking/selection' }],
        },
      ],
      commonIssuesToImprove: [
        'Filler words interrupt flow',
        'One phrase became slightly informal',
      ],
      reviewCredits: {
        available: 3,
        route: '/reviews',
      },
    });

    mockFetchSubmissions.mockResolvedValue([
      {
        id: 'sub-1',
        contentId: 'sp-2',
        taskName: 'Patient Handover - Post-Op Recovery',
        subTest: 'Speaking',
        attemptDate: '2026-03-24T18:03:24.830217+00:00',
        scoreEstimate: '330-360',
        reviewStatus: 'reviewed',
        evaluationId: 'ev-1',
        canRequestReview: false,
        actions: {},
      },
    ] satisfies Submission[]);
    mockFetchMockReports.mockResolvedValue([]);
    mockLearnerListSpeakingSharedResources.mockResolvedValue([]);
  });

  it('shows the shared reference openers, AI exam, tutor booking, and practice cards', async () => {
    render(<SpeakingPage />);

    expect(await screen.findByText('Get assessed by AI or book a live tutor')).toBeInTheDocument();
    expect(screen.getByText('Speaking Assessment Criteria')).toBeInTheDocument();
    expect(screen.getByText('Speaking Intro Questions')).toBeInTheDocument();
    expect(screen.getByText('Open Assessment Criteria').closest('a')).toHaveAttribute('href', '/speaking/assessment-criteria');
    expect(screen.getByText('Open Intro Questions').closest('a')).toHaveAttribute('href', '/speaking/intro-questions');
    expect(screen.getAllByText('All professions').length).toBeGreaterThan(0);
    expect(screen.getByText('Language + clinical')).toBeInTheDocument();
    expect(screen.getByText('12 questions')).toBeInTheDocument();
    expect(screen.queryByText('42 points')).not.toBeInTheDocument();
    expect(screen.queryByText('11 questions')).not.toBeInTheDocument();
    expect(screen.getByText('Start Speaking Exam')).toBeInTheDocument();
    expect(screen.getByText('Full AI Speaking Mock')).toBeInTheDocument();
    expect(screen.getByText('Book a Tutor')).toBeInTheDocument();
    expect(screen.getByText('Practice Library')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open practice library' })).toHaveAttribute('href', '/speaking/selection');
    // The library card links out — individual task cards are not inlined on the hub.
    expect(screen.queryByText('Patient Handover - Post-Op Recovery')).not.toBeInTheDocument();

    expect(screen.queryByText('Recent Speaking Evidence')).not.toBeInTheDocument();
    expect(screen.queryByText('Recent Mock Reports')).not.toBeInTheDocument();
    expect(screen.queryByText('Drill Groups')).not.toBeInTheDocument();
    expect(screen.queryByText('Open Speaking Rules')).not.toBeInTheDocument();
    expect(screen.queryByText('Breaking Bad News')).not.toBeInTheDocument();
  });

  it('keeps the shared reference openers even when the practice library is empty', async () => {
    mockFetchSpeakingHome.mockResolvedValueOnce({
      recommendedRolePlay: null,
      featuredTasks: [],
      drillGroups: [],
      commonIssuesToImprove: [],
      reviewCredits: { available: 0, route: '/reviews' },
      pastAttempts: [],
    });

    render(<SpeakingPage />);

    expect(await screen.findByText('Open Assessment Criteria')).toBeInTheDocument();
    expect(screen.getByText('Open Intro Questions')).toBeInTheDocument();
    // The library link and the exam stay reachable when the library is empty.
    expect(screen.getByRole('link', { name: 'Open practice library' })).toBeInTheDocument();
    expect(screen.getByText('Start Speaking Exam')).toBeInTheDocument();
  });

  it('shows the owner-mandated credit copy: full mock "Card A + Card B" / "2 cards = 4 AI credits"', async () => {
    render(<SpeakingPage />);

    expect(await screen.findByText('2 cards = 4 AI credits')).toBeInTheDocument();
    expect(screen.getByText('Card A + Card B')).toBeInTheDocument();
  });

  it('renders the Practice Library as a full card with the exact PDF copy', async () => {
    render(<SpeakingPage />);

    const card = await screen.findByTestId('speaking-practice-library-card');
    expect(card).toHaveTextContent('Practice Library');
    expect(card).toHaveTextContent(
      'Practise one OET Speaking role-play card at a time - 3 minutes to prepare and 5 minutes to speak. The AI plays the patient and marks your result.',
    );
    expect(card).toHaveTextContent('1 card = 2 AI credits | Browsing the library is free');
    const cta = screen.getByRole('link', { name: 'Open practice library' });
    expect(cta).toHaveTextContent('Open practice library →');
    expect(cta).toHaveAttribute('href', '/speaking/selection');
  });

  it('orders the hub: criteria, intro, Practice Library, full AI mock, then Book a Tutor (no free sample offered)', async () => {
    render(<SpeakingPage />);

    const criteria = await screen.findByText('Speaking Assessment Criteria');
    const intro = screen.getByText('Speaking Intro Questions');
    const library = screen.getByRole('link', { name: 'Open practice library' });
    const exam = screen.getByText('Start Speaking Exam');
    const tutor = screen.getByText('Book a Tutor');
    const follows = (a: Element, b: Element) =>
      Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(criteria, intro)).toBe(true);
    expect(follows(intro, library)).toBe(true);
    expect(follows(library, exam)).toBe(true);
    expect(follows(exam, tutor)).toBe(true);
  });

  describe('Free Speaking Mock (Free Mocks)', () => {
    // 22 Sep 2026 handoff (item 2, CRITICAL SECURITY): the server offers at
    // most ONE row now — the caller's own profession — never a list to pick
    // from.
    const OWN_PROFESSION_OFFER = [
      { professionId: 'medicine', contentId: 'rpc-med', state: 'available', route: '/speaking/roleplay/rpc-med?free=1' },
    ];

    it('sits BEFORE the Practice Library card, Full AI Speaking Mock, and Book a Tutor (item 3 hub order)', async () => {
      mockListFreeSamples.mockResolvedValue(OWN_PROFESSION_OFFER);
      render(<SpeakingPage />);

      const free = await screen.findByTestId('speaking-free-mock-card');
      const library = screen.getByRole('link', { name: 'Open practice library' });
      const exam = screen.getByText('Start Speaking Exam');
      const tutor = screen.getByText('Book a Tutor');
      const follows = (a: Element, b: Element) =>
        Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
      expect(follows(free, library)).toBe(true);
      expect(follows(library, exam)).toBe(true);
      expect(follows(exam, tutor)).toBe(true);
      expect(free).toHaveTextContent('Free Speaking Mock');
      expect(free).toHaveTextContent('Free sample includes one full attempt. New attempts require Speaking credits.');
    });

    it('links straight to the offered card — no cross-profession picker', async () => {
      mockListFreeSamples.mockResolvedValue(OWN_PROFESSION_OFFER);
      render(<SpeakingPage />);

      const free = await screen.findByTestId('speaking-free-mock-card');
      expect(free).toHaveAttribute('href', '/speaking/roleplay/rpc-med?free=1');
      // No profession-choice UI exists anymore.
      expect(screen.queryByRole('radio')).not.toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('is hidden when the server offers no sample', async () => {
      render(<SpeakingPage />);

      await screen.findByText('Start Speaking Exam');
      expect(screen.queryByTestId('speaking-free-mock-card')).not.toBeInTheDocument();
    });
  });

  it('links Speaking submissions to the Speaking-only history view', async () => {
    render(<SpeakingPage />);

    const card = await screen.findByTestId('speaking-submissions-card');
    expect(card).toHaveTextContent('Speaking submissions');
    expect(within(card).getByRole('link', { name: /View Speaking submissions/ })).toHaveAttribute(
      'href',
      '/submissions?subtest=speaking',
    );
  });

  it('links Book a Tutor to the private-speaking booking page', async () => {
    render(<SpeakingPage />);

    const tutorLink = (await screen.findByText('Book a Tutor')).closest('a');
    expect(tutorLink).toHaveAttribute('href', '/private-speaking');
  });

  it('keeps Book a Tutor visible but gated for learners on ineligible packages', async () => {
    mockUseEntitlementSnapshot.mockReturnValue({ data: { speakingAddonsEnabled: false } });
    render(<SpeakingPage />);

    expect(await screen.findByText('Book a tutor as your patient')).toBeInTheDocument();
    expect(screen.getByText('Eligible packages only')).toBeInTheDocument();
    expect(screen.queryByText('Book a Tutor')).not.toBeInTheDocument();
    expect(screen.getByText('View eligible courses').closest('a')).toHaveAttribute('href', '/catalog');
    // Existing bookings stay reachable.
    expect(screen.getByText('My bookings').closest('a')).toHaveAttribute('href', '/private-speaking');
  });

  it('opens Book a Tutor when the entitlement grants live-tutor add-ons', async () => {
    mockUseEntitlementSnapshot.mockReturnValue({ data: { speakingAddonsEnabled: true } });
    render(<SpeakingPage />);

    expect((await screen.findByText('Book a Tutor')).closest('a')).toHaveAttribute('href', '/private-speaking');
    expect(screen.queryByText('Eligible packages only')).not.toBeInTheDocument();
  });
});
