import { fireEvent, render, screen, within } from '@testing-library/react';
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
});
