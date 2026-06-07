import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { AdminManagedTable, type ManagedBulkAction } from './admin-managed-table';
import type { Column } from '@/components/ui/data-table';
import type { BulkResult } from './admin-managed-table';

type Row = { id: string; name: string; status: 'draft' | 'active' };

const COLUMNS: Column<Row>[] = [
  { key: 'name', header: 'Name', render: (r) => <span>{r.name}</span> },
  { key: 'status', header: 'Status', render: (r) => <span>{r.status}</span> },
];

function makeRows(n: number): Row[] {
  return Array.from({ length: n }, (_, i) => ({
    id: String(i + 1),
    name: `Row ${i + 1}`,
    status: i % 2 === 0 ? 'draft' : 'active',
  }));
}

function noop() {}

const baseProps = {
  columns: COLUMNS,
  keyExtractor: (r: Row) => r.id,
  page: 1,
  pageSize: 10,
  onPageChange: noop,
  onPageSizeChange: noop,
  itemLabel: 'row',
  itemLabelPlural: 'rows',
};

async function selectRow(user: ReturnType<typeof userEvent.setup>, id: string) {
  // DataTable renders both a mobile-card and a desktop-table checkbox per row;
  // either toggles the same selection state, so click the first match.
  await user.click(screen.getAllByLabelText(`Select row ${id}`)[0]);
}

describe('AdminManagedTable', () => {
  it('disables a bulk action when a selected row fails isEligible, enables when all eligible', async () => {
    const user = userEvent.setup();
    const rows = makeRows(3); // ids 1(draft) 2(active) 3(draft)
    const action: ManagedBulkAction<Row> = {
      key: 'publish',
      label: 'Publish',
      isEligible: (row) => row.status === 'draft',
      run: vi.fn(async () => ({ totalRequested: 0, succeeded: 0 })),
    };

    render(
      <AdminManagedTable
        {...baseProps}
        data={rows}
        total={rows.length}
        bulkActions={[action]}
        onResult={noop}
        onError={noop}
      />,
    );

    // Select an eligible (draft) row -> action enabled
    await selectRow(user, '1');
    const button = await screen.findByRole('button', { name: /publish/i });
    expect(button).not.toBeDisabled();

    // Also select an ineligible (active) row -> action disabled
    await selectRow(user, '2');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /publish/i })).toBeDisabled();
    });
  });

  it('chunks selected ids and sums succeeded across chunks', async () => {
    const user = userEvent.setup();
    const rows = makeRows(5);
    const run = vi.fn(async (ids: string[]): Promise<BulkResult> => ({
      totalRequested: ids.length,
      succeeded: ids.length,
    }));
    const onResult = vi.fn();

    render(
      <AdminManagedTable
        {...baseProps}
        data={rows}
        total={rows.length}
        chunkSize={2}
        bulkActions={[{ key: 'go', label: 'Go', run }]}
        onResult={onResult}
        onError={noop}
      />,
    );

    for (const id of ['1', '2', '3', '4', '5']) {
      await selectRow(user, id);
    }

    await user.click(await screen.findByRole('button', { name: /^go$/i }));

    await waitFor(() => expect(onResult).toHaveBeenCalledTimes(1));
    // 5 ids / chunk 2 => 3 calls (2,2,1)
    expect(run).toHaveBeenCalledTimes(3);
    const [, result] = onResult.mock.calls[0];
    expect(result.succeeded).toBe(5);
    expect(result.totalRequested).toBe(5);
  });

  it('opens a confirm modal and passes the typed reason to run', async () => {
    const user = userEvent.setup();
    const rows = makeRows(2);
    const run = vi.fn(async (ids: string[], reason?: string): Promise<BulkResult> => ({
      totalRequested: ids.length,
      succeeded: ids.length,
      message: reason,
    }));
    const onResult = vi.fn();

    render(
      <AdminManagedTable
        {...baseProps}
        data={rows}
        total={rows.length}
        bulkActions={[
          {
            key: 'delete',
            label: 'Delete',
            run,
            confirm: {
              title: (n) => `Delete ${n}`,
              description: (n) => `Delete ${n} rows?`,
              confirmLabel: 'Delete selected',
              destructive: true,
              requireReason: true,
            },
          },
        ]}
        onResult={onResult}
        onError={noop}
      />,
    );

    await selectRow(user, '1');
    await user.click(await screen.findByRole('button', { name: /^delete$/i }));

    // Modal opens; run not yet called
    expect(await screen.findByText('Delete 1')).toBeInTheDocument();
    expect(run).not.toHaveBeenCalled();

    // Confirm disabled until reason typed
    const confirmButton = screen.getByRole('button', { name: /delete selected/i });
    expect(confirmButton).toBeDisabled();

    await user.type(screen.getByLabelText('Reason'), 'spam cleanup');
    await waitFor(() => expect(confirmButton).not.toBeDisabled());
    await user.click(confirmButton);

    await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
    expect(run).toHaveBeenCalledWith(['1'], 'spam cleanup');
    await waitFor(() => expect(onResult).toHaveBeenCalled());
  });
});
