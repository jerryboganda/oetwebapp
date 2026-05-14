import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdminWalletTierRow } from '@/lib/api';
import { WalletTiersEditor } from '@/components/admin/billing/wallet-tiers-editor';

function makeTier(overrides: Partial<AdminWalletTierRow>): AdminWalletTierRow {
  return {
    id: 'tier-1',
    amount: 1000,
    credits: 10,
    bonus: 0,
    totalCredits: 10,
    label: null,
    isPopular: false,
    displayOrder: 0,
    isActive: true,
    currency: 'AUD',
    ...overrides,
  };
}

describe('WalletTiersEditor client-side validation', () => {
  it('warns when active amounts are not strictly ascending', () => {
    const tiers = [
      makeTier({ id: 't1', amount: 5000, displayOrder: 0 }),
      makeTier({ id: 't2', amount: 1000, displayOrder: 1 }),
    ];
    render(
      <WalletTiersEditor
        initialTiers={tiers}
        defaultCurrency="AUD"
        source="database"
        onSave={vi.fn()}
      />,
    );
    expect(screen.getByTestId('wallet-tier-ascending-warning')).toBeInTheDocument();
  });

  it('blocks save and shows duplicate warning when two rows share an amount', async () => {
    const onSave = vi.fn();
    const user = userEvent.setup();
    const tiers = [
      makeTier({ id: 't1', amount: 1000, displayOrder: 0 }),
      makeTier({ id: 't2', amount: 1000, displayOrder: 1 }),
    ];
    render(
      <WalletTiersEditor
        initialTiers={tiers}
        defaultCurrency="AUD"
        source="database"
        onSave={onSave}
      />,
    );
    expect(screen.getByText(/duplicate amounts/i)).toBeInTheDocument();
    expect(screen.getByTestId('wallet-tiers-save')).toBeDisabled();
    await user.click(screen.getByTestId('wallet-tiers-save'));
    expect(onSave).not.toHaveBeenCalled();
  });

  it('rejects non-positive whole-currency amounts (e.g. decimals or zero)', async () => {
    const user = userEvent.setup();
    render(
      <WalletTiersEditor
        initialTiers={[makeTier({ id: 't1', amount: 1000 })]}
        defaultCurrency="AUD"
        source="database"
        onSave={vi.fn()}
      />,
    );
    const row = screen.getByTestId('wallet-tier-row');
    const amountInput = within(row).getByLabelText(/tier 1 amount/i);
    await user.clear(amountInput);
    await user.type(amountInput, '12.5');
    expect(within(row).getByText(/positive whole-currency amount/i)).toBeInTheDocument();
    expect(screen.getByTestId('wallet-tiers-save')).toBeDisabled();
  });

  it('asks for confirmation before removing a tier (immutable-ID semantics)', async () => {
    const user = userEvent.setup();
    render(
      <WalletTiersEditor
        initialTiers={[
          makeTier({ id: 't1', amount: 1000 }),
          makeTier({ id: 't2', amount: 2000, displayOrder: 1 }),
        ]}
        defaultCurrency="AUD"
        source="database"
        onSave={vi.fn()}
      />,
    );
    expect(screen.getAllByTestId('wallet-tier-row')).toHaveLength(2);

    await user.click(screen.getByRole('button', { name: /remove tier 1000/i }));
    // Confirm dialog opens before any state is mutated
    expect(screen.getByTestId('billing-confirm-dialog')).toBeInTheDocument();
    expect(screen.getAllByTestId('wallet-tier-row')).toHaveLength(2);
  });
});
