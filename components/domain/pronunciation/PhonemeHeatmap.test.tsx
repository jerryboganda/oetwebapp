import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PhonemeHeatmap } from '@/components/domain/pronunciation/PhonemeHeatmap';

describe('PhonemeHeatmap', () => {
  it('renders empty state when there are no words', () => {
    render(<PhonemeHeatmap wordScores={[]} />);
    expect(screen.getByText(/no word-level data/i)).toBeInTheDocument();
  });

  it('renders a chip per word with the rounded score', () => {
    render(
      <PhonemeHeatmap
        wordScores={[
          { word: 'therapy', accuracyScore: 88.4, errorType: 'None' },
          { word: 'method', accuracyScore: 62.6, errorType: 'Mispronunciation' },
          { word: 'author', accuracyScore: 12.2, errorType: 'Omission' },
        ]}
      />,
    );
    expect(screen.getByText('therapy')).toBeInTheDocument();
    expect(screen.getByText('method')).toBeInTheDocument();
    expect(screen.getByText('author')).toBeInTheDocument();
    expect(screen.getByText('88')).toBeInTheDocument();
    expect(screen.getByText('63')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('marks each chip with a listitem role for a11y', () => {
    render(
      <PhonemeHeatmap
        wordScores={[
          { word: 'therapy', accuracyScore: 88, errorType: 'None' },
        ]}
      />,
    );
    expect(screen.getByRole('list')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(1);
  });
});
