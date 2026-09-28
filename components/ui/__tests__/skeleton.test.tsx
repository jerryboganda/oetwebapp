import { render, screen } from '@testing-library/react';
import { CardSkeleton, PageSkeleton, Skeleton } from '../skeleton';

describe('Skeleton status regions', () => {
  it('PageSkeleton exposes exactly one status region', () => {
    render(<PageSkeleton />);

    expect(screen.getAllByRole('status')).toHaveLength(1);
    expect(screen.getByRole('status', { name: 'Loading page' })).toBeInTheDocument();
  });

  it('CardSkeleton exposes exactly one status region', () => {
    render(<CardSkeleton />);

    expect(screen.getAllByRole('status')).toHaveLength(1);
    expect(screen.getByRole('status', { name: 'Loading card' })).toBeInTheDocument();
  });

  it('keeps the status role on a standalone Skeleton', () => {
    render(<Skeleton />);

    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
  });
});
