// Debug: test renderWithRouter utility
import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import { useRouter, usePathname, useSearchParams } from 'next/navigation';

function TestComponent() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  return (
    <div data-testid="test">
      push={typeof router.push} path={pathname} q={searchParams.get('q') ?? 'none'}
    </div>
  );
}

describe('renderWithRouter utility', () => {
  it('provides router, pathname, and searchParams', () => {
    renderWithRouter(<TestComponent />, {
      pathname: '/achievements',
      searchParams: new URLSearchParams('q=hello'),
    });
    const el = screen.getByTestId('test');
    expect(el).toBeInTheDocument();
    expect(el.textContent).toContain('path=/achievements');
    expect(el.textContent).toContain('q=hello');
  });
});
