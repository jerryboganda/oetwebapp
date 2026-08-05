import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PackageList } from './package-list';
import type { UserAccessSubscriptionRow } from '@/lib/api/user-access-packages';
import type { AdminBillingPlan } from '@/lib/types/admin';

const plans = [
  {
    id: 'plan-med',
    code: 'med',
    name: 'Medicine',
    price: 0,
    currency: 'GBP',
    accessDurationDays: 180,
  },
  {
    id: 'plan-physio',
    code: 'physio',
    name: 'Physio',
    price: 0,
    currency: 'GBP',
    accessDurationDays: 180,
  },
] as AdminBillingPlan[];

function subscription(
  id: string,
  planCode: string,
  isPrimary: boolean,
): UserAccessSubscriptionRow {
  return {
    id,
    planCode,
    planName: planCode === 'med' ? 'Medicine' : 'Physio',
    status: 'Active',
    expiresAt: null,
    isPrimary,
    startedAt: '2026-08-01T00:00:00.000Z',
    fulfilmentStatus: 'auto',
  };
}

describe('PackageList primary package behavior', () => {
  it('shows primary guidance and offers Set primary only for another current package', async () => {
    const user = userEvent.setup();
    const onSetPrimary = vi.fn();

    render(
      <PackageList
        plans={plans}
        subscriptions={[subscription('sub-med', 'med', true), subscription('sub-physio', 'physio', false)]}
        onChange={vi.fn()}
        onSetPrimary={onSetPrimary}
      />,
    );

    expect(screen.getByText(/primary controls the learner's summary\/default plan/i)).toBeInTheDocument();
    expect(screen.getByTitle(/other active packages still contribute access/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /set primary/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /set primary/i }));
    expect(onSetPrimary).toHaveBeenCalledWith('sub-physio');
  });

  it('keeps the existing primary unless the new draft is explicitly marked primary', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    render(
      <PackageList
        plans={plans}
        subscriptions={[subscription('sub-med', 'med', true)]}
        onChange={onChange}
      />,
    );

    await user.selectOptions(screen.getByLabelText('Plan'), 'physio');
    let latest = onChange.mock.calls.at(-1)?.[0] as UserAccessSubscriptionRow[];
    expect(latest.filter((row) => row.isPrimary)).toHaveLength(1);
    expect(latest.find((row) => row.planCode === 'med')?.isPrimary).toBe(true);

    await user.click(screen.getByLabelText('Make primary'));
    latest = onChange.mock.calls.at(-1)?.[0] as UserAccessSubscriptionRow[];
    expect(latest.filter((row) => row.isPrimary)).toHaveLength(1);
    expect(latest.find((row) => row.planCode === 'physio')?.isPrimary).toBe(true);
    expect(latest.find((row) => row.planCode === 'med')?.isPrimary).toBe(false);
  });
});
