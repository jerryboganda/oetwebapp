import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchAuthorizedObjectUrl, mockGetReadingStructureAdminPreview } = vi.hoisted(() => ({
  mockFetchAuthorizedObjectUrl: vi.fn(),
  mockGetReadingStructureAdminPreview: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ paperId: 'paper-1' }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  AdminSettingsLayout: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
  SettingsSection: ({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) => (
    <section aria-label={title}>
      <h2>{title}</h2>
      {description ? <p>{description}</p> : null}
      {children}
    </section>
  ),
}));

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: mockFetchAuthorizedObjectUrl,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingStructureAdminPreview: mockGetReadingStructureAdminPreview,
}));

import AdminReadingPreviewPage from './page';

function part(partCode: 'A' | 'B' | 'C', questionCount: number) {
  return {
    id: `part-${partCode}`,
    partCode,
    timeLimitMinutes: partCode === 'A' ? 15 : 45,
    maxRawScore: questionCount,
    instructions: `Instructions ${partCode}`,
    texts: [
      {
        id: `text-${partCode}`,
        displayOrder: 1,
        title: `Text ${partCode}`,
        source: null,
        bodyHtml: '<p>Safe learner text</p><script>SECRET_SCRIPT</script><img src="x" onerror="SECRET_ONERROR" />',
        wordCount: 50,
        topicTag: null,
      },
    ],
    questions: Array.from({ length: questionCount }, (_, index) => ({
      id: `question-${partCode}-${index + 1}`,
      partCode,
      displayOrder: index + 1,
      stem: `Question ${partCode}${index + 1}`,
      questionType: 'mcq',
      maxScore: 1,
      options: index === 0 ? [{ value: 'A', label: 'Safe option A', correctAnswer: 'SECRET-OPTION', acceptedVariants: ['SECRET-VARIANT'] }] : [],
      mediaAssetId: null,
      readingTextId: `text-${partCode}`,
    })),
  };
}

describe('Admin Reading preview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchAuthorizedObjectUrl.mockResolvedValue('blob:paper-a');
    mockGetReadingStructureAdminPreview.mockResolvedValue({
      paper: {
        id: 'paper-1',
        title: 'Reading Sample Paper',
        slug: 'reading-sample-paper',
        subtestCode: 'reading',
        questionPaperAssets: [
          { id: 'asset-a', part: 'A', title: 'Part A PDF', downloadPath: '/v1/media/asset-a/content' },
        ],
      },
      parts: [part('A', 20), part('B', 6), part('C', 16)],
    });
  });

  it('renders timed preview controls and keeps options learner-safe', async () => {
    const user = userEvent.setup();
    render(<AdminReadingPreviewPage />);

    expect(await screen.findByRole('heading', { name: 'Reading Sample Paper' })).toBeInTheDocument();
    const consolePanel = screen.getByRole('region', { name: 'Timed preview console' });
    expect(within(consolePanel).getAllByText('15:00')).toHaveLength(2);
    expect(within(consolePanel).getByText('45:00')).toBeInTheDocument();
    expect(within(consolePanel).getByText('42')).toBeInTheDocument();

    await user.click(within(consolePanel).getByRole('button', { name: /paper mode/i }));
    expect(within(consolePanel).getByRole('button', { name: /part a pdf/i })).toBeInTheDocument();

    expect(document.body.innerHTML).toContain('Safe option A');
    expect(screen.getAllByText('Safe learner text').length).toBeGreaterThan(0);
    expect(document.body.innerHTML).not.toContain('SECRET_SCRIPT');
    expect(document.body.innerHTML).not.toContain('SECRET_ONERROR');
    expect(screen.queryByText('SECRET-OPTION')).not.toBeInTheDocument();
    expect(screen.queryByText('SECRET-VARIANT')).not.toBeInTheDocument();
  });
});
