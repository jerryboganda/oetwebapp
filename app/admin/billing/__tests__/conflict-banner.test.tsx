import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BillingConflictBanner, isConflictError } from '@/components/admin/billing/conflict-banner';

describe('isConflictError', () => {
  it('detects HTTP 409 error objects', () => {
    expect(isConflictError({ status: 409, message: 'whatever' })).toBe(true);
  });

  it('detects 409-themed Error messages regardless of casing', () => {
    expect(isConflictError(new Error('HTTP 409 conflict'))).toBe(true);
    expect(isConflictError(new Error('Concurrent edit conflict detected'))).toBe(true);
    expect(isConflictError(new Error('etag mismatch'))).toBe(true);
    expect(isConflictError(new Error('Version mismatch on row'))).toBe(true);
  });

  it('returns false for unrelated errors and falsy values', () => {
    expect(isConflictError(null)).toBe(false);
    expect(isConflictError(undefined)).toBe(false);
    expect(isConflictError(new Error('500 internal'))).toBe(false);
    expect(isConflictError({ status: 400 })).toBe(false);
  });
});

describe('BillingConflictBanner', () => {
  it('renders the conflict warning and a Reload latest CTA', async () => {
    const onReload = vi.fn();
    const user = userEvent.setup();
    render(
      <BillingConflictBanner
        entityLabel="plan"
        detail="ETag mismatch on plan-version 7"
        onReload={onReload}
      />,
    );
    expect(screen.getByTestId('billing-conflict-banner')).toBeInTheDocument();
    expect(screen.getByText(/another admin updated this plan/i)).toBeInTheDocument();
    expect(screen.getByText(/etag mismatch on plan-version 7/i)).toBeInTheDocument();

    await user.click(screen.getByTestId('billing-conflict-reload'));
    expect(onReload).toHaveBeenCalledTimes(1);
  });

  it('hides the dismiss button when no onDismiss handler is provided', () => {
    render(<BillingConflictBanner onReload={() => undefined} />);
    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument();
  });
});
