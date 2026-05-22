import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DataTable, type Column } from '../data-table';

interface RowModel {
  id: string;
  name: string;
  status: string;
}

const columns: Column<RowModel>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (row) => row.name,
  },
  {
    key: 'status',
    header: 'Status',
    render: (row) => row.status,
  },
];

describe('DataTable', () => {
  it('renders the mobile card fallback alongside the desktop table markup', () => {
    const onRowClick = vi.fn();
    const { container } = render(
      <DataTable
        columns={columns}
        data={[{ id: '1', name: 'Alice', status: 'Ready' }]}
        keyExtractor={(row) => row.id}
        onRowClick={onRowClick}
        mobileCardRender={(row) => (
          <div data-testid="mobile-card">
            {row.name} - {row.status}
          </div>
        )}
        aria-label="Example table"
      />,
    );

    expect(container.querySelector('div.md\\:hidden')).toBeInTheDocument();
    expect(container.querySelector('div.hidden.md\\:block')).toBeInTheDocument();
    expect(screen.getByTestId('mobile-card')).toHaveTextContent('Alice - Ready');
    expect(within(screen.getByRole('table', { name: /example table/i })).getByText('Alice')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('mobile-card'));
    expect(onRowClick).toHaveBeenCalledWith(
      expect.objectContaining({ id: '1', name: 'Alice', status: 'Ready' }),
      expect.any(Object),
    );
  });

  it('renders desktop table content when no custom mobile card is provided', () => {
    const { container } = render(
      <DataTable
        columns={columns}
        data={[{ id: '1', name: 'Alice', status: 'Ready' }]}
        keyExtractor={(row) => row.id}
        aria-label="Example table"
      />,
    );

    const table = screen.getByRole('table', { name: /example table/i });

    expect(container.querySelector('div.md\\:hidden')).toBeInTheDocument();
    expect(container.querySelector('div.hidden.md\\:block')).toBeInTheDocument();
    expect(within(table).getByText('Alice')).toBeInTheDocument();
    expect(within(table).getByText('Ready')).toBeInTheDocument();
  });

  it('renders selectable header and row checkboxes', () => {
    render(
      <DataTable
        columns={columns}
        data={[
          { id: '1', name: 'Alice', status: 'Ready' },
          { id: '2', name: 'Bob', status: 'Draft' },
        ]}
        keyExtractor={(row) => row.id}
        selectable
        selectedKeys={new Set()}
        onSelectionChange={vi.fn()}
        aria-label="Selectable table"
      />,
    );

    const table = screen.getByRole('table', { name: /selectable table/i });
    expect(within(table).getByLabelText('Select all rows')).toBeInTheDocument();
    expect(within(table).getByLabelText('Select row 1')).toBeInTheDocument();
    expect(within(table).getByLabelText('Select row 2')).toBeInTheDocument();
  });

  it('selects all visible rows from the header checkbox', async () => {
    const user = userEvent.setup();
    const onSelectionChange = vi.fn();

    render(
      <DataTable
        columns={columns}
        data={[
          { id: '1', name: 'Alice', status: 'Ready' },
          { id: '2', name: 'Bob', status: 'Draft' },
        ]}
        keyExtractor={(row) => row.id}
        selectable
        selectedKeys={new Set()}
        onSelectionChange={onSelectionChange}
        aria-label="Selectable table"
      />,
    );

    await user.click(within(screen.getByRole('table')).getByLabelText('Select all rows'));

    expect(onSelectionChange).toHaveBeenCalledWith(new Set(['1', '2']));
  });

  it('toggles an individual row without firing the row click handler', async () => {
    const user = userEvent.setup();
    const onSelectionChange = vi.fn();
    const onRowClick = vi.fn();

    render(
      <DataTable
        columns={columns}
        data={[{ id: '1', name: 'Alice', status: 'Ready' }]}
        keyExtractor={(row) => row.id}
        selectable
        selectedKeys={new Set()}
        onSelectionChange={onSelectionChange}
        onRowClick={onRowClick}
        aria-label="Selectable table"
      />,
    );

    await user.click(within(screen.getByRole('table')).getByLabelText('Select row 1'));

    expect(onSelectionChange).toHaveBeenCalledWith(new Set(['1']));
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('marks the header checkbox indeterminate when some rows are selected', () => {
    render(
      <DataTable
        columns={columns}
        data={[
          { id: '1', name: 'Alice', status: 'Ready' },
          { id: '2', name: 'Bob', status: 'Draft' },
        ]}
        keyExtractor={(row) => row.id}
        selectable
        selectedKeys={new Set(['1'])}
        onSelectionChange={vi.fn()}
        aria-label="Selectable table"
      />,
    );

    const headerCheckbox = within(screen.getByRole('table')).getByLabelText('Select all rows') as HTMLInputElement;
    expect(headerCheckbox.indeterminate).toBe(true);
  });
});
