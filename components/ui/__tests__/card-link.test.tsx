import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { CardLink } from '../card-link';

describe('CardLink', () => {
  it('renders a semantic link wrapper for card-like navigation', () => {
    render(
      <CardLink href="/study-plan">
        <span>Open Study Plan</span>
      </CardLink>,
    );

    const link = screen.getByRole('link', { name: /open study plan/i });
    expect(link).toHaveAttribute('href', '/study-plan');
  });
});
