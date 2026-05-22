import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { BulkActionBar } from '../bulk-action-bar';

describe('BulkActionBar', () => {
  it('does not render when no rows are selected', () => {
    render(<BulkActionBar selectedCount={0} actions={[]} onClearSelection={vi.fn()} />);

    expect(screen.queryByTestId('bulk-action-bar')).not.toBeInTheDocument();
  });

  it('renders the selected count and clears selection', async () => {
    const user = userEvent.setup();
    const onClearSelection = vi.fn();

    render(<BulkActionBar selectedCount={3} actions={[]} onClearSelection={onClearSelection} totalCount={10} />);

    expect(screen.getByTestId('bulk-action-bar')).toBeInTheDocument();
    expect(screen.getByText('3 items selected')).toBeInTheDocument();
    expect(screen.getByText('3 of 10 visible items')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear selection' }));

    expect(onClearSelection).toHaveBeenCalledTimes(1);
  });

  it('runs action handlers', async () => {
    const user = userEvent.setup();
    const onDelete = vi.fn();

    render(
      <BulkActionBar
        selectedCount={2}
        onClearSelection={vi.fn()}
        actions={[{ key: 'delete', label: 'Delete selected', variant: 'danger', onClick: onDelete }]}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Delete selected' }));

    expect(onDelete).toHaveBeenCalledTimes(1);
  });
});
