import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ResultsScorePanel } from './results-score-panel';

// Launch handoff UI-1 (2 Oct 2026): the score card must never spill its text
// at desktop widths. Pixel geometry is proven by the Playwright spec
// tests/e2e/writing-v2/results-layout.spec.ts; this pins the class contract
// that delivers it (container-query sizing, shrinkable tiles, clipped card).
describe('ResultsScorePanel containment contract', () => {
  function renderPanel() {
    return render(
      <ResultsScorePanel
        eyebrow="Result"
        title="AI Estimated Practice Score — not an official OET result"
        subtitle="Occupational Therapy · Speech Pathology"
        gaugeValue={76}
        gaugeCenter={<span>382</span>}
        gaugeLabel="Grade B"
        stats={[
          { label: 'Score', value: '382/500' },
          { label: 'Occupational Therapy', value: 'Speech Pathology' },
        ]}
      />,
    );
  }

  it('sizes from its own width and clips nothing outside the card', () => {
    renderPanel();
    const card = screen.getByTestId('results-score-panel');
    expect(card).toHaveClass('@container', 'overflow-hidden');
    // The gauge row and its text column may shrink below their content width.
    const title = screen.getByRole('heading', { level: 1 });
    expect(title.parentElement).toHaveClass('min-w-0');
    expect(title.parentElement?.parentElement).toHaveClass('flex', 'min-w-0');
  });

  it('lays the stat tiles out as a shrinkable auto-fit grid with wrapping values', () => {
    renderPanel();
    const tiles = screen.getAllByTestId('results-score-stat');
    expect(tiles).toHaveLength(2);
    expect(tiles[0].parentElement?.className).toContain('grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))]');
    for (const tile of tiles) {
      expect(tile).toHaveClass('min-w-0');
      expect(tile.querySelector('p.tile-label')).toHaveClass('min-w-0');
      expect(tile.lastElementChild).toHaveClass('break-words');
    }
  });

  it('exposes the gauge centre value as grade-value', () => {
    renderPanel();
    expect(screen.getByTestId('grade-value')).toHaveTextContent('382');
  });
});
