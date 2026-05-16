import { render, screen } from '@testing-library/react';
import { Button } from '../button';

function expectClasses(element: HTMLElement, classes: string[]) {
  for (const className of classes) {
    expect(element).toHaveClass(className);
  }
}

describe('Button primitive visual contracts', () => {
  it('uses primary styling by default', () => {
    render(<Button>Primary action</Button>);

    expectClasses(screen.getByRole('button', { name: 'Primary action' }), ['bg-primary', 'text-white']);
  });

  it('uses navy styling for secondary actions', () => {
    render(<Button variant="secondary">Secondary action</Button>);

    expectClasses(screen.getByRole('button', { name: 'Secondary action' }), ['bg-navy', 'text-white']);
  });

  it('uses border tokens for outline actions', () => {
    render(<Button variant="outline">Outline action</Button>);

    expectClasses(screen.getByRole('button', { name: 'Outline action' }), ['border', 'border-border']);
  });

  it('does not apply solid primary or navy backgrounds to ghost actions', () => {
    render(<Button variant="ghost">Ghost action</Button>);

    const button = screen.getByRole('button', { name: 'Ghost action' });
    expect(button).not.toHaveClass('bg-primary');
    expect(button).not.toHaveClass('bg-navy');
  });

  it('disables busy buttons and renders a spinner affordance while loading', () => {
    render(<Button loading>Saving</Button>);

    const button = screen.getByRole('button', { name: 'Saving' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('supports full-width layout', () => {
    render(<Button fullWidth>Full width action</Button>);

    expect(screen.getByRole('button', { name: 'Full width action' })).toHaveClass('w-full');
  });

  it('can style a child link without rendering a nested button', () => {
    render(
      <Button asChild>
        <a href="/dashboard">Dashboard</a>
      </Button>,
    );

    const link = screen.getByRole('link', { name: 'Dashboard' });
    expectClasses(link, ['bg-primary', 'text-white']);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
