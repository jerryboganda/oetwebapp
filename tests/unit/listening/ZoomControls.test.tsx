import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { ZoomControls } from '@/components/domain/listening/ZoomControls';

function ZoomHarness({ initial = 100 }: { initial?: number }) {
  const [value, setValue] = useState(initial);
  return <ZoomControls value={value} onChange={setValue} />;
}

describe('ZoomControls', () => {
  it('zooms in, zooms out, resets, and announces the current value', async () => {
    const user = userEvent.setup();
    render(<ZoomHarness />);

    await user.click(screen.getByRole('button', { name: /increase question zoom/i }));
    expect(screen.getByText(/current zoom 110%/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /decrease question zoom/i }));
    expect(screen.getByText(/current zoom 100%/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /increase question zoom/i }));
    await user.click(screen.getByRole('button', { name: /reset question zoom/i }));
    expect(screen.getByText(/current zoom 100%/i)).toBeInTheDocument();
  });

  it('clamps to the supported zoom range', async () => {
    const user = userEvent.setup();
    render(<ZoomHarness initial={130} />);

    expect(screen.getByRole('button', { name: /increase question zoom/i })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /reset question zoom/i }));
    await user.click(screen.getByRole('button', { name: /decrease question zoom/i }));
    await user.click(screen.getByRole('button', { name: /decrease question zoom/i }));
    expect(screen.getByText(/current zoom 90%/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /decrease question zoom/i })).toBeDisabled();
  });
});
