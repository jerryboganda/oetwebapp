import { render, screen } from '@testing-library/react';
import { Badge, CriterionChip, StatusBadge } from '../badge';

function expectClasses(element: HTMLElement, classes: string[]) {
  for (const className of classes) {
    expect(element).toHaveClass(className);
  }
}

describe('Badge visual contracts', () => {
  it('keeps the default compact pill styling', () => {
    render(<Badge>Default badge</Badge>);

    expectClasses(screen.getByText('Default badge'), [
      'rounded-full',
      'px-2',
      'py-0.5',
      'text-xs',
      'font-bold',
    ]);
  });

  it('applies muted medium sizing and surface colors', () => {
    render(
      <Badge variant="muted" size="md">
        Muted badge
      </Badge>,
    );

    expectClasses(screen.getByText('Muted badge'), [
      'bg-surface',
      'text-slate-700',
      'border-border',
      'px-3',
      'py-1',
      'text-sm',
    ]);
  });

  it('renders completed status with success styling', () => {
    render(<StatusBadge status="completed" />);

    expectClasses(screen.getByText('Completed'), [
      'bg-emerald-50',
      'text-emerald-700',
      'border-emerald-200/60',
    ]);
  });

  it('keeps criterion chips as rounded active/inactive pills', () => {
    render(
      <>
        <CriterionChip label="Active criterion" active />
        <CriterionChip label="Inactive criterion" />
      </>,
    );

    expectClasses(screen.getByRole('button', { name: 'Active criterion' }), [
      'rounded-full',
      'bg-primary',
      'text-white',
    ]);
    expectClasses(screen.getByRole('button', { name: 'Inactive criterion' }), [
      'border-border',
      'bg-surface',
      'hover:bg-background-light',
    ]);
  });
});
