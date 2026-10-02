import { act, render, screen } from '@testing-library/react';
import type { WritingMyWorkItemDto } from '@/lib/writing/types';

const { mockTrack, mockListFreeSamples, mockGetMyWork } = vi.hoisted(() => ({
  mockTrack: vi.fn(),
  mockListFreeSamples: vi.fn(),
  mockGetMyWork: vi.fn(),
}));

// Echo the key plus its values (the global mock drops values) so the banner's
// word count and clock are assertable; value-less keys read exactly as before.
vi.mock('next-intl', () => ({
  useTranslations: () => (key: string, values?: Record<string, unknown>) =>
    (values ? `${key} ${JSON.stringify(values)}` : key),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
  usePathname: () => '/writing',
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { activeProfessionId: 'medicine' } }),
}));

vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));

vi.mock('@/lib/writing/api', () => ({ getWritingMyWork: mockGetMyWork }));

vi.mock('@/components/domain/learner-skill-switcher', () => ({
  LearnerSkillSwitcher: () => <div data-testid="skill-switcher" />,
}));

import WritingHome from './page';

function myWorkItem(overrides: Partial<WritingMyWorkItemDto>): WritingMyWorkItemDto {
  return {
    key: 'submission:sub-1',
    kind: 'submission',
    state: 'graded',
    rawStatus: 'graded',
    scenarioId: 'scn-1',
    title: 'Discharge letter for Mr Smith',
    letterType: 'LT-DG',
    mode: 'practice',
    isRevision: false,
    isFreeSample: false,
    draftId: null,
    submissionId: 'sub-1',
    wordCount: 182,
    phase: null,
    readingSecondsRemaining: null,
    writingSecondsRemaining: null,
    lastActivityAt: '2026-10-02T09:30:00Z',
    canRetry: false,
    autoRetrying: false,
    actions: [{ kind: 'open_result', href: '/writing/submissions/sub-1/results' }],
    ...overrides,
  };
}

const ACTIVE_DRAFT = myWorkItem({
  key: 'draft:d-1',
  kind: 'draft',
  state: 'draft',
  rawStatus: 'active',
  scenarioId: 'scn-2',
  title: 'Referral to a physiotherapist',
  draftId: 'd-1',
  submissionId: null,
  wordCount: 320,
  phase: 'writing',
  readingSecondsRemaining: 0,
  writingSecondsRemaining: 1865,
  actions: [{ kind: 'resume', href: '/writing/practice/session/scn-2' }],
});

const RETRYABLE_FAILURE = myWorkItem({
  key: 'submission:sub-2',
  state: 'failed',
  rawStatus: 'failed',
  submissionId: 'sub-2',
  title: 'Urgent referral for Ms Jones',
  canRetry: true,
  actions: [
    { kind: 'retry', href: '/writing/submissions/sub-2/grading' },
    { kind: 'view_letter', href: '/writing/submissions/sub-2' },
  ],
});

describe('Writing landing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // No free sample on offer by default: the launcher renders nothing.
    mockListFreeSamples.mockResolvedValue([]);
    // Nothing to resume by default: the banner renders nothing.
    mockGetMyWork.mockResolvedValue({ items: [], hasMore: false });
  });

  describe('Resume writing banner', () => {
    it('offers Resume writing for an active draft with its words and the time left on its paused clock', async () => {
      mockGetMyWork.mockResolvedValue({ items: [ACTIVE_DRAFT], hasMore: false });
      render(<WritingHome />);

      const banner = await screen.findByTestId('resume-writing-banner');
      expect(banner).toHaveAttribute('href', '/writing/practice/session/scn-2');
      expect(banner).toHaveAttribute('data-state', 'draft');
      expect(banner).toHaveTextContent('writing.myWork.banner.resume {"count":320,"time":"31:05"}');
      expect(banner).toHaveTextContent('Referral to a physiotherapist');
    });

    it('counts the writing window while the draft is still being read, and shows only words for a draft without a clock', async () => {
      mockGetMyWork.mockResolvedValue({
        items: [{ ...ACTIVE_DRAFT, phase: 'reading', readingSecondsRemaining: 120, writingSecondsRemaining: 2400 }],
        hasMore: false,
      });
      const { unmount } = render(<WritingHome />);
      expect(await screen.findByTestId('resume-writing-banner')).toHaveTextContent('writing.myWork.banner.resume {"count":320,"time":"42:00"}');
      unmount();

      mockGetMyWork.mockResolvedValue({
        items: [{ ...ACTIVE_DRAFT, phase: null, readingSecondsRemaining: null, writingSecondsRemaining: null }],
        hasMore: false,
      });
      render(<WritingHome />);
      expect(await screen.findByTestId('resume-writing-banner')).toHaveTextContent('writing.myWork.banner.resumeNoTime {"count":320}');
    });

    it('points at the newest letter needing the learner: a saved letter whose grading can be retried', async () => {
      mockGetMyWork.mockResolvedValue({ items: [myWorkItem({}), RETRYABLE_FAILURE, ACTIVE_DRAFT], hasMore: false });
      render(<WritingHome />);

      const banner = await screen.findByTestId('resume-writing-banner');
      expect(banner).toHaveAttribute('href', '/writing/submissions/sub-2/grading');
      expect(banner).toHaveAttribute('data-state', 'failed');
      expect(banner).toHaveTextContent('writing.myWork.banner.failed');
      expect(banner).toHaveTextContent('Urgent referral for Ms Jones');
    });

    it('renders nothing when nothing needs resuming or the lookup fails', async () => {
      mockGetMyWork.mockResolvedValue({
        items: [myWorkItem({}), { ...RETRYABLE_FAILURE, canRetry: false, actions: [{ kind: 'view_letter', href: '/writing/submissions/sub-2' }] }],
        hasMore: false,
      });
      const { unmount } = render(<WritingHome />);
      await act(async () => {});
      expect(mockGetMyWork).toHaveBeenCalledTimes(1);
      expect(screen.queryByTestId('resume-writing-banner')).toBeNull();
      unmount();

      mockGetMyWork.mockRejectedValue(new Error('offline'));
      render(<WritingHome />);
      await act(async () => {});
      expect(screen.queryByTestId('resume-writing-banner')).toBeNull();
    });
  });

  it('shows the Free Writing Mock as the FIRST action under Start Writing, above the practice library', async () => {
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'w-med', state: 'available', route: '/writing/practice/session/w-med' },
    ]);
    render(<WritingHome />);

    const card = await screen.findByTestId('writing-free-mock-card');
    expect(card).toHaveTextContent('writing.hub.freeSample.title');
    expect(card).toHaveTextContent('writing.hub.freeSample.badge');
    expect(mockListFreeSamples).toHaveBeenCalledWith('writing');

    const practice = screen.getByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ });
    const submissions = screen.getByRole('link', { name: /writing\.hub\.cards\.submissions\.cta/ });
    expect(card.compareDocumentPosition(practice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(card.compareDocumentPosition(submissions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('hides the free card when the server offers no sample (Practice Library and Past Submissions unchanged)', async () => {
    render(<WritingHome />);

    await screen.findByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ });
    expect(screen.queryByTestId('writing-free-mock-card')).toBeNull();
  });

  it('routes to the V2 writing flows', () => {
    render(<WritingHome />);

    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ })).toHaveAttribute('href', '/writing/practice/library');
    // Past Submissions must never open the global all-subtest history — it opens
    // pre-filtered to Writing only (brief item 5).
    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.submissions\.cta/ })).toHaveAttribute('href', '/submissions?subtest=writing');
  });

  it('no longer surfaces mock exams or model answers', () => {
    render(<WritingHome />);

    expect(screen.queryByRole('link', { name: /writing\.hub\.cards\.mocks\.cta/ })).toBeNull();
    expect(screen.queryByRole('link', { name: /writing\.hub\.cards\.model\.cta/ })).toBeNull();
  });

  it('does not surface the internal rulebook to learners', () => {
    const { container } = render(<WritingHome />);

    const hrefs = Array.from(container.querySelectorAll('a')).map((a) => a.getAttribute('href') ?? '');
    expect(hrefs.some((h) => h.startsWith('/writing/rulebook'))).toBe(false);
  });

  it('does not link to any retired V1 writing surfaces', () => {
    const { container } = render(<WritingHome />);

    const hrefs = Array.from(container.querySelectorAll('a')).map((a) => a.getAttribute('href') ?? '');
    expect(hrefs.some((h) => h.startsWith('/writing/player'))).toBe(false);
    expect(hrefs.some((h) => h.startsWith('/writing/library'))).toBe(false);
    expect(hrefs.some((h) => h.startsWith('/writing/revision'))).toBe(false);
  });

  it('tracks module entry on mount', () => {
    render(<WritingHome />);
    expect(mockTrack).toHaveBeenCalledWith('module_entry', { module: 'writing' });
  });
});
