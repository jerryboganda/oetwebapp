import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';

describe('ScoreConversionEvidence', () => {
  it('keeps the practice-only disclosure inside the branded score graph', () => {
    render(
      <ScoreConversionEvidence
        assessment="Reading"
        rawScore={31}
        maxRawScore={42}
        scaledScore={370}
        passed={true}
        grade="B"
        tableVersion="owner-v1"
      />,
    );

    expect(screen.getByText('AI Practice Score — not an official OET result')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /370 out of 500.*not an official OET result/i })).toBeInTheDocument();
    expect(screen.getByText('370/500')).toBeInTheDocument();
  });

  it('keeps the branded graph visible when conversion is unavailable', () => {
    render(
      <ScoreConversionEvidence
        assessment="Listening"
        rawScore={18}
        maxRawScore={42}
        scaledScore={370}
        passed={null}
        errorCode="score_table_not_configured"
      />,
    );

    expect(screen.getByText('AI Practice Score — not an official OET result')).toBeInTheDocument();
    expect(screen.getByText('Awaiting table')).toBeInTheDocument();
    expect(screen.queryByText('370/500')).not.toBeInTheDocument();
    expect(screen.getByText(/score_table_not_configured/)).toBeInTheDocument();
  });
});
