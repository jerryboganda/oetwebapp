import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockCloneReadingPaper,
  mockGetContentPaper,
  mockGetReadingStructureAdmin,
  mockPush,
  mockValidateReadingPaper,
} = vi.hoisted(() => ({
  mockCloneReadingPaper: vi.fn(),
  mockGetContentPaper: vi.fn(),
  mockGetReadingStructureAdmin: vi.fn(),
  mockPush: vi.fn(),
  mockValidateReadingPaper: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ paperId: 'paper-1' }),
  useRouter: () => ({ push: mockPush }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  AdminSettingsLayout: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
  SettingsSection: ({ title, description, actions, children }: { title: string; description?: string; actions?: React.ReactNode; children: React.ReactNode }) => (
    <section aria-label={title}>
      <h2>{title}</h2>
      {description ? <p>{description}</p> : null}
      {actions}
      {children}
    </section>
  ),
}));

vi.mock('@/components/admin/ui/kpi-tile', () => ({
  KpiTile: ({ label, value }: { label: string; value: React.ReactNode }) => <div>{label}: {value}</div>,
}));

vi.mock('@/components/domain/admin/reading/ReadingWizardSteps', () => ({
  ReadingWizardSteps: () => <nav aria-label="Reading wizard steps" />,
}));

vi.mock('./ReadingManifestPanel', () => ({
  ReadingManifestPanel: () => <section aria-label="Manifest import" />,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  cloneReadingPaper: mockCloneReadingPaper,
  getReadingStructureAdmin: mockGetReadingStructureAdmin,
  validateReadingPaper: mockValidateReadingPaper,
}));

vi.mock('@/lib/content-upload-api', () => ({
  getContentPaper: mockGetContentPaper,
  updateContentPaper: vi.fn(),
}));

import AdminReadingPaperOverviewPage from './page';

const structure = {
  paperId: 'paper-1',
  parts: [
    { id: 'part-a', partCode: 'A', questions: Array.from({ length: 20 }, (_, index) => ({ id: `a-${index}` })) },
    { id: 'part-b', partCode: 'B', questions: Array.from({ length: 6 }, (_, index) => ({ id: `b-${index}` })) },
    { id: 'part-c', partCode: 'C', questions: Array.from({ length: 16 }, (_, index) => ({ id: `c-${index}` })) },
  ],
};

describe('Admin Reading overview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetReadingStructureAdmin.mockResolvedValue(structure);
    mockValidateReadingPaper.mockResolvedValue({ isPublishReady: true, issues: [], counts: { totalPoints: 42 } });
    mockGetContentPaper.mockResolvedValue({
      id: 'paper-1',
      title: 'Reading Paper',
      difficulty: 'medium',
      estimatedDurationMinutes: 60,
      sourceProvenance: 'Owned source',
      status: 'published',
    });
    mockCloneReadingPaper.mockResolvedValue({
      sourcePaperId: 'paper-1',
      paperId: 'paper-1-clone',
      title: 'Reading Paper revision',
      slug: 'reading-paper-revision',
      adminRoute: '/admin/content/reading/paper-1-clone',
      structure,
    });
  });

  it('creates a draft revision and navigates to the cloned paper', async () => {
    const user = userEvent.setup();
    render(<AdminReadingPaperOverviewPage />);

    const cloneButton = await screen.findByRole('button', { name: /clone draft revision/i });
    await user.click(cloneButton);

    expect(mockCloneReadingPaper).toHaveBeenCalledWith('paper-1', { resetReviewState: true });
    expect(mockPush).toHaveBeenCalledWith('/admin/content/reading/paper-1-clone');
  });
});
