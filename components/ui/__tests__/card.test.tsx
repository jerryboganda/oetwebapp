import { render, screen } from '@testing-library/react';
import { Card } from '../card';

describe('Card padding', () => {
  it('lets a caller p-* replace the responsive default at every breakpoint', () => {
    render(<Card className="p-6">content</Card>);
    const classes = screen.getByText('content').className.split(/\s+/);

    expect(classes).toContain('p-6');
    expect(classes).not.toContain('p-(--card-pad)');
    expect(classes.filter((name) => /^sm:p-/.test(name))).toEqual([]);
  });

  it('keeps the dense-mobile, comfortable-desktop default without an override', () => {
    render(<Card>content</Card>);
    const classes = screen.getByText('content').className.split(/\s+/);

    expect(classes).toEqual(expect.arrayContaining(['p-(--card-pad)', '[--card-pad:0.75rem]', 'sm:[--card-pad:1.25rem]']));
  });
});
