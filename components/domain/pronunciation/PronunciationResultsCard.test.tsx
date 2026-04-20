import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PronunciationResultsCard } from '@/components/domain/pronunciation/PronunciationResultsCard';

function setup(overrides: Partial<Parameters<typeof PronunciationResultsCard>[0]> = {}) {
  return render(
    <PronunciationResultsCard
      accuracy={82}
      fluency={76}
      completeness={90}
      prosody={74}
      overall={80}
      projectedSpeakingScaled={400}
      projectedSpeakingGrade="B"
      wordScoresJson='[{"word":"therapy","accuracyScore":88,"errorType":"None"}]'
      problematicPhonemesJson='[{"phoneme":"θ","score":72,"occurrences":3,"ruleId":"P01.1"}]'
      fluencyMarkersJson='{"speechRateWpm":152,"pauseCount":3,"averagePauseDurationMs":420}'
      feedbackJson='{"summary":"Clear attempt","strengths":["Good completeness"],"improvements":[{"ruleId":"P01.1","message":"Soften /θ/"}]}'
      provider="azure"
      drillId="pd-001"
      {...overrides}
    />,
  );
}

describe('PronunciationResultsCard', () => {
  it('renders the four primary scores + overall', () => {
    setup();
    expect(screen.getByText('Accuracy')).toBeInTheDocument();
    expect(screen.getByText('Fluency')).toBeInTheDocument();
    expect(screen.getByText('Completeness')).toBeInTheDocument();
    expect(screen.getByText('Prosody')).toBeInTheDocument();
    expect(screen.getByText('Overall')).toBeInTheDocument();
  });

  it('renders the projected Speaking band using the backend-provided value', () => {
    setup();
    expect(screen.getByText(/400\/500 · Grade B/)).toBeInTheDocument();
  });

  it('includes the advisory disclaimer', () => {
    setup();
    expect(screen.getByText(/advisory projection/i)).toBeInTheDocument();
  });

  it('lists problematic phonemes with their rule IDs', () => {
    setup();
    expect(screen.getByText('/θ/')).toBeInTheDocument();
    expect(screen.getByText('P01.1')).toBeInTheDocument();
  });

  it('surfaces the grounded AI feedback when provided', () => {
    setup();
    expect(screen.getByText('Clear attempt')).toBeInTheDocument();
    expect(screen.getByText('Soften /θ/')).toBeInTheDocument();
  });

  it('gracefully handles missing/invalid JSON blobs', () => {
    setup({
      wordScoresJson: 'not-json',
      problematicPhonemesJson: '',
      fluencyMarkersJson: '{}',
      feedbackJson: '{}',
    });
    // The heading is present even if word list is empty
    expect(screen.getByText(/Word-level accuracy/i)).toBeInTheDocument();
    expect(screen.getByText(/no word-level data/i)).toBeInTheDocument();
  });
});
