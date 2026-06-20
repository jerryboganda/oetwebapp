import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  api,
  authState,
  searchState,
  mockFetchWalletTiers,
  mockReplaceWalletTiers,
  mockFetchPlans,
  mockDeletePlan,
  mockFetchAddOns,
  mockDeleteAddOn,
  mockFetchContent,
  mockDeleteContent,
} = vi.hoisted(() => ({
  api: {} as Record<string, unknown>,
  authState: {
    adminPermissions: ['billing:read', 'billing:write'] as string[],
  },
  searchState: { tab: 'wallet' },
  mockFetchWalletTiers: vi.fn(),
  mockReplaceWalletTiers: vi.fn(),
  mockFetchPlans: vi.fn(),
  mockDeletePlan: vi.fn(),
  mockFetchAddOns: vi.fn(),
  mockDeleteAddOn: vi.fn(),
  mockFetchContent: vi.fn(),
  mockDeleteContent: vi.fn(),
}));

vi.mock('@/lib/api', () => {
  api.fetchAdminWalletTiers = mockFetchWalletTiers;
  api.replaceAdminWalletTiers = mockReplaceWalletTiers;
  api.fetchAdminBillingPlans = mockFetchPlans;
  api.createAdminBillingPlan = vi.fn();
  api.updateAdminBillingPlan = vi.fn();
  api.deleteAdminBillingPlan = mockDeletePlan;
  api.fetchAdminBillingAddOns = mockFetchAddOns;
  api.createAdminBillingAddOn = vi.fn();
  api.updateAdminBillingAddOn = vi.fn();
  api.deleteAdminBillingAddOn = mockDeleteAddOn;
  api.fetchAdminBillingContent = mockFetchContent;
  api.replaceAdminBillingContent = vi.fn();
  api.deleteAdminBillingContentEntry = mockDeleteContent;
  return api;
});

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => ({ get: (key: string) => (key === 'tab' ? searchState.tab : null) }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    user: {
      userId: 'admin-1',
      email: 'admin@test.com',
      role: 'admin',
      displayName: 'Admin',
      isEmailVerified: true,
      adminPermissions: authState.adminPermissions,
    },
  }),
}));

import AdminPricingHubPage from './page';

const dbResponse = {
  source: 'database' as const,
  currency: 'AUD',
  tiers: [
    {
      id: '11111111-1111-1111-1111-111111111111',
      amount: 25,
      credits: 28,
      bonus: 3,
      totalCredits: 31,
      label: 'Standard',
      isPopular: false,
      displayOrder: 1,
      isActive: true,
      currency: 'AUD',
    },
  ],
};

const fallbackResponse = {
  source: 'appsettings' as const,
  currency: 'AUD',
  tiers: [
    {
      id: null,
      amount: 10,
      credits: 10,
      bonus: 0,
      totalCredits: 10,
      label: 'Starter',
      isPopular: false,
      displayOrder: 0,
      isActive: true,
      currency: 'AUD',
    },
  ],
};

const planRow = {
  id: 'plan-1',
  code: 'basic-monthly',
  name: 'Basic Monthly',
  description: 'Starter access',
  price: 19.99,
  currency: 'GBP',
  interval: 'month',
  includedCredits: 0,
  status: 'active',
};

const addOnRow = {
  id: 'addon-1',
  code: 'review-pack-3',
  name: '3 Review Credits',
  description: 'Three tutor reviews',
  price: 29,
  currency: 'GBP',
  interval: 'one_time',
  grantCredits: 3,
  status: 'active',
  addonKind: 'review_credits',
};

const aiPackageRow = {
  id: 'addon-ai-1',
  code: 'ai-quick-check',
  name: 'AI Quick Check',
  description: 'Flexible AI checks',
  price: 9,
  currency: 'GBP',
  durationDays: 30,
  status: 'active',
  addonKind: 'ai_package',
  grantEntitlements: { flexible_credits: 5 },
  aiPackageGroup: 'full',
  aiFeatures: ['5 AI checks'],
};

async function confirmDestructiveAction(phrase: string) {
  expect(screen.getByTestId('billing-confirm-action')).toBeDisabled();
  await userEvent.type(screen.getByTestId('billing-confirm-input'), phrase);
  await waitFor(() => expect(screen.getByTestId('billing-confirm-action')).toBeEnabled());
  await userEvent.click(screen.getByTestId('billing-confirm-action'));
}

describe('AdminPricingHubPage — destructive pricing controls', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.adminPermissions = ['billing:read', 'billing:write'];
    searchState.tab = 'wallet';
    mockFetchWalletTiers.mockResolvedValue(dbResponse);
    mockReplaceWalletTiers.mockResolvedValue(dbResponse);
    mockFetchPlans.mockResolvedValue([]);
    mockDeletePlan.mockResolvedValue({ deleted: true });
    mockFetchAddOns.mockResolvedValue([]);
    mockDeleteAddOn.mockResolvedValue({ deleted: true });
    mockFetchContent.mockResolvedValue({ entries: [] });
    mockDeleteContent.mockResolvedValue({ deleted: true });
  });

  it('shows Delete beside every plan row and hard-deletes after exact-code confirmation', async () => {
    searchState.tab = 'plans';
    mockFetchPlans.mockResolvedValueOnce([planRow]).mockResolvedValueOnce([]);

    render(<AdminPricingHubPage />);

    await screen.findByTestId('plan-catalog-editor');
    const row = screen.getByText('Basic Monthly').closest('tr');
    expect(row).not.toBeNull();
    await userEvent.click(within(row as HTMLTableRowElement).getByRole('button', { name: /hard delete plan basic monthly/i }));

    expect(screen.getByText('Hard-delete this plan?')).toBeInTheDocument();
    await confirmDestructiveAction('basic-monthly');

    await waitFor(() => {
      expect(mockDeletePlan).toHaveBeenCalledWith('plan-1');
    });
    expect(mockFetchPlans).toHaveBeenCalledTimes(2);
  });

  it('tells admins to archive a plan when the hard-delete API refuses an in-use row', async () => {
    searchState.tab = 'plans';
    mockFetchPlans.mockResolvedValue([planRow]);
    mockDeletePlan.mockRejectedValueOnce(new Error('billing_plan_in_use: Plan has active subscribers.'));

    render(<AdminPricingHubPage />);

    await screen.findByText('Basic Monthly');
    await userEvent.click(screen.getByRole('button', { name: /hard delete plan basic monthly/i }));
    await confirmDestructiveAction('basic-monthly');

    await waitFor(() => {
      expect(screen.getByText(/archive instead/i)).toBeInTheDocument();
    });
  });

  it('shows Delete beside non-AI add-ons and calls the add-on hard-delete API', async () => {
    searchState.tab = 'addons';
    mockFetchAddOns.mockResolvedValueOnce([addOnRow, aiPackageRow]).mockResolvedValueOnce([aiPackageRow]);

    render(<AdminPricingHubPage />);

    await screen.findByTestId('addon-catalog-editor');
    expect(screen.queryByText('AI Quick Check')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /hard delete add-on 3 review credits/i }));
    await confirmDestructiveAction('review-pack-3');

    await waitFor(() => {
      expect(mockDeleteAddOn).toHaveBeenCalledWith('addon-1');
    });
  });

  it('shows Delete beside AI packages and uses the add-on hard-delete API', async () => {
    searchState.tab = 'ai';
    mockFetchAddOns.mockResolvedValueOnce([addOnRow, aiPackageRow]).mockResolvedValueOnce([addOnRow]);

    render(<AdminPricingHubPage />);

    await screen.findByTestId('ai-package-editor');
    expect(screen.queryByText('3 Review Credits')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /hard delete ai package ai quick check/i }));
    await confirmDestructiveAction('ai-quick-check');

    await waitFor(() => {
      expect(mockDeleteAddOn).toHaveBeenCalledWith('addon-ai-1');
    });
  });

  it('disables row delete controls for read-only billing admins', async () => {
    authState.adminPermissions = ['billing:read'];
    searchState.tab = 'plans';
    mockFetchPlans.mockResolvedValue([planRow]);

    render(<AdminPricingHubPage />);

    await screen.findByText('Basic Monthly');
    expect(screen.getByRole('button', { name: /hard delete plan basic monthly/i })).toBeDisabled();
  });

  it('deletes a stored Page Copy override and restores the default text locally', async () => {
    searchState.tab = 'copy';
    mockFetchContent.mockResolvedValueOnce({
      entries: [{ key: 'billing.page.title', value: 'Custom billing headline', section: 'Page & hero' }],
    });

    render(<AdminPricingHubPage />);

    await screen.findByTestId('billing-copy-editor');
    expect(screen.getByDisplayValue('Custom billing headline')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /delete override/i }));
    await confirmDestructiveAction('billing.page.title');

    await waitFor(() => {
      expect(mockDeleteContent).toHaveBeenCalledWith('billing.page.title');
    });
    expect(screen.queryByDisplayValue('Custom billing headline')).not.toBeInTheDocument();
  });
});

describe('AdminPricingHubPage — Wallet tab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.adminPermissions = ['billing:read', 'billing:write'];
    searchState.tab = 'wallet';
    mockFetchWalletTiers.mockResolvedValue(dbResponse);
    mockReplaceWalletTiers.mockResolvedValue(dbResponse);
    mockFetchPlans.mockResolvedValue([]);
    mockFetchAddOns.mockResolvedValue([]);
    mockFetchContent.mockResolvedValue({ entries: [] });
  });

  it('renders DB-backed tiers and saves an update', async () => {
    mockFetchWalletTiers.mockResolvedValueOnce(dbResponse);
    mockReplaceWalletTiers.mockResolvedValueOnce({
      ...dbResponse,
      tiers: [{ ...dbResponse.tiers[0], amount: 30, credits: 32, totalCredits: 35 }],
    });

    render(<AdminPricingHubPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    const amountInput = screen.getByDisplayValue('25');
    await userEvent.clear(amountInput);
    await userEvent.type(amountInput, '30');

    const saveButton = screen.getByTestId('wallet-tiers-save');
    await userEvent.click(saveButton);

    await waitFor(() => {
      expect(mockReplaceWalletTiers).toHaveBeenCalledTimes(1);
    });

    const payload = mockReplaceWalletTiers.mock.calls[0][0];
    expect(payload).toHaveLength(1);
    expect(payload[0]).toMatchObject({
      id: '11111111-1111-1111-1111-111111111111',
      amount: 30,
      credits: 28,
      currency: 'AUD',
    });

    await waitFor(() => {
      expect(screen.getByText('Wallet top-up tiers saved.')).toBeInTheDocument();
    });
  });

  it('shows the appsettings fallback notice when no DB rows exist', async () => {
    mockFetchWalletTiers.mockResolvedValueOnce(fallbackResponse);

    render(<AdminPricingHubPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });
    expect(screen.getByText(/Showing appsettings fallback/i)).toBeInTheDocument();
  });

  it('blocks save when an amount is invalid', async () => {
    mockFetchWalletTiers.mockResolvedValueOnce(dbResponse);

    render(<AdminPricingHubPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    const amountInput = screen.getByDisplayValue('25');
    await userEvent.clear(amountInput);

    const saveButton = screen.getByTestId('wallet-tiers-save');
    expect(saveButton).toBeDisabled();
    expect(mockReplaceWalletTiers).not.toHaveBeenCalled();
  });

  it('surfaces an error when the API call rejects', async () => {
    mockFetchWalletTiers.mockResolvedValueOnce(dbResponse);
    mockReplaceWalletTiers.mockRejectedValueOnce(new Error('boom'));

    render(<AdminPricingHubPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    await userEvent.click(screen.getByTestId('wallet-tiers-save'));

    await waitFor(() => {
      expect(screen.getByText('boom')).toBeInTheDocument();
    });
  });

  it('renders read-only controls for billing read admins without write permission', async () => {
    authState.adminPermissions = ['billing:read'];
    mockFetchWalletTiers.mockResolvedValueOnce(dbResponse);

    render(<AdminPricingHubPage />);

    await waitFor(() => {
      expect(screen.getByTestId('wallet-tiers-editor')).toBeInTheDocument();
    });

    expect(screen.getByText(/Read-only access/i)).toBeInTheDocument();
    expect(screen.getByLabelText('Tier 1 amount')).toBeDisabled();
    expect(screen.getByTestId('wallet-tiers-save')).toBeDisabled();
    expect(mockReplaceWalletTiers).not.toHaveBeenCalled();
  });
});
