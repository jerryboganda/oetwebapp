import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ScoreBandGraph } from './score-band-graph';

describe('ScoreBandGraph', () => {
  it('renders an approved conversion and persistent practice disclaimer', () => {
    render(
      <ScoreBandGraph
        rawScore={32}
        maxRawScore={42}
        scaledScore={380}
        grade="B"
        tableVersion="lr-2026-v1"
      />,
    );

    expect(screen.getByTestId('score-band-graph')).toHaveTextContent('380/500');
    expect(screen.getByTestId('score-band-graph')).toHaveTextContent('Table lr-2026-v1');
    expect(screen.getByText('AI Practice Score — not an official OET result.')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /scaled score 380 out of 500/i })).toBeInTheDocument();
  });

  it('renders raw-only state when conversion data is unavailable', () => {
    render(<ScoreBandGraph rawScore={25} maxRawScore={42} scaledScore={null} />);

    expect(screen.getByTestId('score-band-graph')).toHaveTextContent('Scaled score unavailable');
    expect(screen.getByTestId('score-band-graph')).toHaveTextContent('Owner-approved table required');
    expect(screen.getByRole('img', { name: /raw practice score 25 out of 42/i })).toBeInTheDocument();
  });
});
