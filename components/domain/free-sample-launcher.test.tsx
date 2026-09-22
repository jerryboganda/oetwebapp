import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PenTool } from 'lucide-react';

const { mockList, mockPush, mockTrack, mockUseAuth } = vi.hoisted(() => ({
  mockList: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: mockPush }) }));
vi.mock('@/contexts/auth-context', () => ({ useAuth: () => mockUseAuth() }));
vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockList }));

import { FreeSampleLauncher } from './free-sample-launcher';

const PROPS = {
  subtest: 'writing' as const,
  icon: PenTool,
  testId: 'free-card',
  title: 'Free Writing Mock',
  description: 'Try one AI-graded OET letter for free.',
  modalTitle: 'Choose your profession',
  modalDescription: 'We will open the free writing task for your profession.',
  startLabel: 'Start free sample',
  usedLabel: 'Free sample already used',
};

const OFFERS = [
  { professionId: 'medicine', contentId: 'w-med', state: 'available', route: '/writing/practice/session/w-med' },
  { professionId: 'nursing', contentId: 'w-nur', state: 'available', route: '/writing/practice/session/w-nur' },
];

describe('FreeSampleLauncher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ user: { activeProfessionId: 'medicine' } });
    mockList.mockResolvedValue(OFFERS);
  });

  it('renders nothing when the server offers no sample', async () => {
    mockList.mockResolvedValue([]);
    const { container } = render(<FreeSampleLauncher {...PROPS} />);

    await waitFor(() => expect(mockList).toHaveBeenCalledWith('writing'));
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when the lookup fails (a bonus entry point never breaks the page)', async () => {
    mockList.mockRejectedValue(new Error('boom'));
    const { container } = render(<FreeSampleLauncher {...PROPS} />);

    await waitFor(() => expect(mockList).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });

  it('asks for the profession first, defaulting to the account profession, and opens that sample', async () => {
    const user = userEvent.setup();
    render(<FreeSampleLauncher {...PROPS} />);

    await user.click(await screen.findByTestId('free-card'));
    expect(mockPush).not.toHaveBeenCalled(); // the sample does NOT open before the choice

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Choose your profession');
    expect(screen.getByRole('radio', { name: 'Medicine' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Nursing' })).not.toBeChecked();

    await user.click(screen.getByRole('radio', { name: 'Nursing' }));
    await user.click(screen.getByTestId('free-card-start'));

    expect(mockPush).toHaveBeenCalledWith('/writing/practice/session/w-nur');
    expect(mockTrack).toHaveBeenCalledWith('free_sample_click', { module: 'writing', professionId: 'nursing' });
  });

  it('still prompts when only one profession is live', async () => {
    mockList.mockResolvedValue([OFFERS[0]]);
    const user = userEvent.setup();
    render(<FreeSampleLauncher {...PROPS} />);

    await user.click(await screen.findByTestId('free-card'));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    await user.click(screen.getByTestId('free-card-start'));
    expect(mockPush).toHaveBeenCalledWith('/writing/practice/session/w-med');
  });

  it('falls back to the first profession when the account profession has no sample', async () => {
    mockUseAuth.mockReturnValue({ user: { activeProfessionId: 'other-allied-health' } });
    const user = userEvent.setup();
    render(<FreeSampleLauncher {...PROPS} />);

    await user.click(await screen.findByTestId('free-card'));
    expect(await screen.findByRole('radio', { name: 'Medicine' })).toBeChecked();
  });

  it('continues an in-progress sample directly — no picker, no profession switch', async () => {
    mockList.mockResolvedValue([
      { professionId: 'nursing', contentId: 'w-nur', state: 'in_progress', route: '/writing/practice/session/w-nur' },
    ]);
    render(<FreeSampleLauncher {...PROPS} />);

    const card = await screen.findByTestId('free-card');
    expect(card).toHaveAttribute('href', '/writing/practice/session/w-nur');
  });

  it('shows a spent sample as inert with a "used" note', async () => {
    mockList.mockResolvedValue([
      { professionId: 'nursing', contentId: 'w-nur', state: 'used', route: '/writing/practice/session/w-nur' },
    ]);
    render(<FreeSampleLauncher {...PROPS} />);

    const card = await screen.findByTestId('free-card');
    expect(card).toBeDisabled();
    expect(card).toHaveTextContent('Free sample already used');
  });
});
