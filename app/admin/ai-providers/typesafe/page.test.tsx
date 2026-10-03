/**
 * Vitest spec for the read-only TypeSafe / Jev status page.
 *
 * Mirrors the project pattern in `app/admin/writing/options/page.test.tsx`:
 * `vi.hoisted` for the shared mock, mock `@/lib/ai-management-api`, render the
 * default page. The page never mutates anything and never receives the key.
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { TypeSafeStatus } from '@/lib/ai-management-api';

const { mockFetch } = vi.hoisted(() => ({ mockFetch: vi.fn() }));

vi.mock('@/lib/ai-management-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/ai-management-api')>('@/lib/ai-management-api');
  return { ...actual, fetchTypeSafeStatus: mockFetch };
});

import TypeSafeStatusPage from './page';

const status: TypeSafeStatus = {
  enabled: true,
  model: 'jev-1.13.0',
  providerCode: 'typesafe-jev',
  guardEnforced: false,
  key: {
    envKeyConfigured: false,
    providerRowExists: true,
    providerRowKeyConfigured: true,
    providerRowActive: true,
    apiKeyHint: '…wxyz',
    effectiveKeyAvailable: true,
  },
  surfaces: [
    {
      flag: 'DevelopmentTriageEnabled',
      envVar: 'TYPESAFE__DEVELOPMENTTRIAGEENABLED',
      name: 'Owner console triage',
      featureCode: 'jev.development.triage',
      enabled: true,
      calls: 42,
      failures: 2,
      avgLatencyMs: 310,
      costUsd: 0.0168,
    },
    {
      flag: 'WritingGuardEnabled',
      envVar: 'TYPESAFE__WRITINGGUARDENABLED',
      name: 'Writing submission guard',
      featureCode: 'jev.writing.guard',
      enabled: false,
      calls: 0,
      failures: 0,
      avgLatencyMs: 0,
      costUsd: 0,
    },
  ],
  otherUsage: [{ featureCode: 'jev.reading.future', calls: 5, failures: 0, avgLatencyMs: 120, costUsd: 0.002 }],
  thresholds: [{ name: 'GuardBlockThreshold', envVar: 'TYPESAFE__GUARDBLOCKTHRESHOLD', value: 0.8 }],
  limits: [{ name: 'TimeoutSeconds', envVar: 'TYPESAFE__TIMEOUTSECONDS', value: 10 }],
  usageWindowDays: 7,
};

describe('TypeSafeStatusPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetch.mockResolvedValue(status);
  });

  it('renders one dense row per surface with flag state and 7-day usage', async () => {
    render(<TypeSafeStatusPage />);

    const triage = (await screen.findByText('Owner console triage')).closest('tr')!;
    expect(within(triage).getByText('TYPESAFE__DEVELOPMENTTRIAGEENABLED')).toBeInTheDocument();
    expect(within(triage).getByText('On')).toBeInTheDocument();
    expect(within(triage).getByText('jev.development.triage')).toBeInTheDocument();
    expect(within(triage).getByText('42')).toBeInTheDocument();
    expect(within(triage).getByText('2')).toBeInTheDocument();
    expect(within(triage).getByText('310 ms')).toBeInTheDocument();
    expect(within(triage).getByText('$0.0168')).toBeInTheDocument();

    const guard = screen.getByText('Writing submission guard').closest('tr')!;
    expect(within(guard).getByText('Off')).toBeInTheDocument();
    expect(within(guard).getByText('$0.0000')).toBeInTheDocument();

    // A Jev code no surface owns is still listed, without a flag.
    const other = screen.getByText('jev.reading.future').closest('tr')!;
    expect(within(other).getByText('Other Jev code (no flag)')).toBeInTheDocument();

    // Totals cover surfaces and unmapped codes.
    const total = within(screen.getByTestId('typesafe-surfaces')).getByText('Total').closest('tr')!;
    expect(within(total).getByText('47')).toBeInTheDocument();
    expect(within(total).getByText('$0.0188')).toBeInTheDocument();
  });

  it('shows key presence and the last-4 hint only, with no key field', async () => {
    const { container } = render(<TypeSafeStatusPage />);

    expect(await screen.findByText('…wxyz')).toBeInTheDocument();
    expect(screen.getByText('Key set')).toBeInTheDocument();
    expect(screen.getByText('Available')).toBeInTheDocument();
    expect(screen.getByText('Calls use the provider-row key.')).toBeInTheDocument();
    expect(container.querySelector('input, textarea')).toBeNull();
    expect(screen.getByRole('link', { name: /manage key and test connection/i })).toHaveAttribute('href', '/admin/ai-providers');
  });

  it('lists thresholds and limits by their environment names', async () => {
    render(<TypeSafeStatusPage />);

    const guardBlock = (await screen.findByText('TYPESAFE__GUARDBLOCKTHRESHOLD')).closest('tr')!;
    expect(within(guardBlock).getByText('0.80')).toBeInTheDocument();
    const timeout = screen.getByText('TYPESAFE__TIMEOUTSECONDS').closest('tr')!;
    expect(within(timeout).getByText('10')).toBeInTheDocument();
  });

  it('says flags are environment settings that need a deploy, and lists the flip order', async () => {
    render(<TypeSafeStatusPage />);

    expect(await screen.findByText(/environment settings, read when the API starts/i)).toBeInTheDocument();
    expect(screen.getByText(/run Build & Deploy/)).toBeInTheDocument();
    expect(screen.getByText('Recommended flip order')).toBeInTheDocument();
    expect(screen.getByText(/Writing submission guard, last/)).toBeInTheDocument();
  });

  it('warns when a flag is on but the master switch is off', async () => {
    mockFetch.mockResolvedValue({ ...status, enabled: false });
    render(<TypeSafeStatusPage />);

    expect(await screen.findByRole('status')).toHaveTextContent(/master switch is off/i);
  });

  it('reports a missing key and an inactive provider row plainly', async () => {
    mockFetch.mockResolvedValue({
      ...status,
      key: {
        envKeyConfigured: false,
        providerRowExists: true,
        providerRowKeyConfigured: true,
        providerRowActive: false,
        apiKeyHint: '…wxyz',
        effectiveKeyAvailable: false,
      },
    });
    render(<TypeSafeStatusPage />);

    expect(await screen.findByText('Missing')).toBeInTheDocument();
    expect(screen.getByText('Inactive')).toBeInTheDocument();
    expect(screen.getByText(/No usable key/)).toBeInTheDocument();
  });

  it('shows an error message when the initial fetch rejects', async () => {
    mockFetch.mockRejectedValue(new Error('Boom - backend down'));
    render(<TypeSafeStatusPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Boom - backend down');
  });

  it('refetches on Refresh', async () => {
    const user = userEvent.setup();
    render(<TypeSafeStatusPage />);
    await screen.findByText('Owner console triage');

    await user.click(screen.getByRole('button', { name: /refresh/i }));

    await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
  });
});
