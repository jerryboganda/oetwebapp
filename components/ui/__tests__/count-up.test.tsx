import { render, screen } from '@testing-library/react';
import { CountUp } from '../count-up';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('CountUp', () => {
  it('renders the real value as one text node with its width reserved', () => {
    render(<p>Raw <CountUp value={33} suffix="/38" /></p>);

    const value = screen.getByText('33/38');
    expect(value.childNodes).toHaveLength(1);
    expect(value).toHaveClass('tabular-nums');
    expect(value.style.minWidth).toBe('5ch');
  });

  it('rounds to a whole number', () => {
    render(<CountUp value={72.6} suffix="%" />);
    expect(screen.getByText('73%')).toBeInTheDocument();
  });

  it('counts from zero when it enters the viewport, then restores the committed value', () => {
    const observers: Array<(entries: Array<{ isIntersecting: boolean }>) => void> = [];
    vi.stubGlobal('IntersectionObserver', class {
      constructor(callback: (entries: Array<{ isIntersecting: boolean }>) => void) { observers.push(callback); }
      observe() {}
      disconnect() {}
    });

    const { unmount } = render(<CountUp value={420} suffix="/500" />);
    const value = screen.getByText('420/500');
    observers[0]([{ isIntersecting: true }]);
    expect(value.textContent).toBe('0/500');

    unmount();
    expect(value.textContent).toBe('420/500');
  });
});
