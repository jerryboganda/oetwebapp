import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { BillingConfirmDialog } from '@/components/admin/billing/confirm-dialog';

function Harness({ phrase }: { phrase?: string }) {
  const [input, setInput] = useState('');
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  return (
    <>
      <BillingConfirmDialog
        open
        title="Archive billing plan?"
        description="This is destructive."
        confirmPhrase={phrase}
        confirmInput={input}
        onConfirmInputChange={setInput}
        onConfirm={onConfirm}
        onCancel={onCancel}
      />
      <button type="button" data-testid="external">external</button>
    </>
  );
}

describe('BillingConfirmDialog', () => {
  it('renders the destructive description and disables confirm until the phrase matches', async () => {
    render(<Harness phrase="ARCHIVE" />);

    expect(screen.getByText(/archive billing plan\?/i)).toBeInTheDocument();
    expect(screen.getByText(/destructive action/i)).toBeInTheDocument();

    expect(screen.getByTestId('billing-confirm-action')).toBeDisabled();

    fireEvent.change(screen.getByTestId('billing-confirm-input'), { target: { value: 'ARCHIVE' } });
    await waitFor(() => {
      expect(screen.getByTestId('billing-confirm-action')).not.toBeDisabled();
    });
  });

  it('does not require a typed phrase when none is configured', () => {
    function NoPhrase() {
      return (
        <BillingConfirmDialog
          open
          title="Remove tier?"
          description="Heads up."
          onConfirm={() => undefined}
          onCancel={() => undefined}
        />
      );
    }
    render(<NoPhrase />);
    expect(screen.queryByTestId('billing-confirm-input')).not.toBeInTheDocument();
    expect(screen.getByTestId('billing-confirm-action')).not.toBeDisabled();
  });

  it('invokes onCancel when the cancel button is pressed', async () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    const user = userEvent.setup();
    render(
      <BillingConfirmDialog
        open
        title="Archive coupon?"
        description="Stops new redemptions."
        confirmPhrase="ARCHIVE"
        confirmInput=""
        onConfirmInputChange={() => undefined}
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );
    await user.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
