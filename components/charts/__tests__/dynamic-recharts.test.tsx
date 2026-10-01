import { render, screen } from '@testing-library/react';

const motion = vi.hoisted(() => ({ reduced: false }));

vi.mock('motion/react', () => ({ useReducedMotionConfig: () => motion.reduced }));
vi.mock('recharts', () => ({
  Line: ({ isAnimationActive }: { isAnimationActive?: boolean | 'auto' }) => (
    <span data-testid="series">{String(isAnimationActive)}</span>
  ),
}));

import { Line } from '../dynamic-recharts';

describe('dynamic recharts series', () => {
  it("leave recharts' OS-aware default alone", async () => {
    motion.reduced = false;
    render(<Line dataKey="score" />);
    expect(await screen.findByTestId('series')).toHaveTextContent('undefined');
  });

  it('stop animating under the in-app Reduce motion setting', async () => {
    motion.reduced = true;
    render(<Line dataKey="score" isAnimationActive />);
    expect(await screen.findByTestId('series')).toHaveTextContent('false');
  });
});
