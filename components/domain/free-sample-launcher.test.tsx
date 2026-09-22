import { render, screen, waitFor } from '@testing-library/react';
import { PenTool } from 'lucide-react';

const { mockList, mockTrack } = vi.hoisted(() => ({
  mockList: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockList }));

import { FreeSampleLauncher } from './free-sample-launcher';

const PROPS = {
  subtest: 'writing' as const,
  icon: PenTool,
  testId: 'free-card',
  title: 'Free Writing Mock',
  description: 'Try one AI-graded OET letter for free.',
  usedLabel: 'Free sample already used',
};

// 22 Sep 2026 handoff (item 2, CRITICAL SECURITY): the server offers at most
// ONE row — the caller's own profession. There is no cross-profession picker.
const OWN_OFFER = { professionId: 'medicine', contentId: 'w-med', state: 'available', route: '/writing/practice/session/w-med' };

describe('FreeSampleLauncher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList.mockResolvedValue([OWN_OFFER]);
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

  it('links straight to the offered sample — no picker, no profession switch', async () => {
    render(<FreeSampleLauncher {...PROPS} />);

    const card = await screen.findByTestId('free-card');
    expect(card).toHaveAttribute('href', '/writing/practice/session/w-med');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
  });

  it('tracks free_sample_click on click', async () => {
    const userEvent = (await import('@testing-library/user-event')).default.setup();
    render(<FreeSampleLauncher {...PROPS} />);

    await userEvent.click(await screen.findByTestId('free-card'));

    expect(mockTrack).toHaveBeenCalledWith('free_sample_click', { module: 'writing', professionId: 'medicine' });
  });

  it('continues an in-progress sample directly — no picker, no profession switch', async () => {
    mockList.mockResolvedValue([
      { professionId: 'medicine', contentId: 'w-med', state: 'in_progress', route: '/writing/practice/session/w-med' },
    ]);
    render(<FreeSampleLauncher {...PROPS} />);

    const card = await screen.findByTestId('free-card');
    expect(card).toHaveAttribute('href', '/writing/practice/session/w-med');
  });

  it('shows a spent sample as inert with a "used" note', async () => {
    mockList.mockResolvedValue([
      { professionId: 'medicine', contentId: 'w-med', state: 'used', route: '/writing/practice/session/w-med' },
    ]);
    render(<FreeSampleLauncher {...PROPS} />);

    const card = await screen.findByTestId('free-card');
    expect(card).toBeDisabled();
    expect(card).toHaveTextContent('Free sample already used');
  });
});
