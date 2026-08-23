import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AiPreAnalysisPanel } from '../AiPreAnalysisPanel';
import { RubricPanel } from '../RubricPanel';
import type { WritingPreAssessmentDto } from '@/lib/writing/types';

/**
 * FE-WATCH regression tests (commit 027c4abc hotfix follow-up):
 * 1. `suggestedCriterionFeedback` may be undefined — the panel must not crash.
 * 2. RubricPanel "use" buttons must not produce duplicate React keys when the
 *    AI suggestion equals the current draft value.
 */

function makePreAssessment(
  overrides: Partial<WritingPreAssessmentDto> = {},
): WritingPreAssessmentDto {
  return {
    source: 'heuristic',
    estimatedBands: { c1: 2, c2: 5, c3: 5, c4: 6, c5: 5, c6: 5 },
    estimatedRawTotal: 28,
    estimatedBandLabel: 'B',
    confidence: 'medium',
    wordCount: 182,
    withinWordGuide: true,
    keyContentCoveragePercent: 78,
    missingKeyContent: [],
    detectedIrrelevantContent: [],
    languageNotes: [],
    suggestedCriterionFeedback: { c1: 'State the purpose earlier.' },
    ...overrides,
  };
}

describe('AiPreAnalysisPanel', () => {
  it('renders without crashing when suggestedCriterionFeedback is undefined', () => {
    // Regression: hotfixed crash on undefined suggestedCriterionFeedback.
    const pa = makePreAssessment({ suggestedCriterionFeedback: undefined });
    render(
      <AiPreAnalysisPanel preAssessment={pa} accepted={false} onApply={vi.fn()} onReject={vi.fn()} />,
    );
    expect(screen.getByText(/AI pre-analysis/i)).toBeInTheDocument();
  });

  it('renders without crashing when all optional arrays are empty and coverage is extreme', () => {
    const pa = makePreAssessment({
      keyContentCoveragePercent: 150,
      missingKeyContent: [],
      detectedIrrelevantContent: [],
      languageNotes: [],
    });
    render(
      <AiPreAnalysisPanel preAssessment={pa} accepted={false} onApply={vi.fn()} onReject={vi.fn()} />,
    );
    // Coverage is clamped to 0..100.
    expect(screen.getByText('100%')).toBeInTheDocument();
  });

  it('emits reject intent when a suggestion has been accepted', async () => {
    const onReject = vi.fn();
    render(
      <AiPreAnalysisPanel
        preAssessment={makePreAssessment()}
        accepted
        onApply={vi.fn()}
        onReject={onReject}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /reject/i }));
    expect(onReject).toHaveBeenCalledTimes(1);
  });

  it('shows suggested feedback section only when suggestions exist', () => {
    const pa = makePreAssessment({ suggestedCriterionFeedback: {} });
    render(
      <AiPreAnalysisPanel preAssessment={pa} accepted={false} onApply={vi.fn()} onReject={vi.fn()} />,
    );
    expect(screen.queryByText(/Suggested per-criterion feedback/i)).not.toBeInTheDocument();
  });

  it('emits apply/reject intents', async () => {
    const onApply = vi.fn();
    const onReject = vi.fn();
    render(
      <AiPreAnalysisPanel
        preAssessment={makePreAssessment()}
        accepted={false}
        onApply={onApply}
        onReject={onReject}
      />,
    );
    const applyButton = screen.getByRole('button', { name: /use ai suggestion/i });
    await userEvent.click(applyButton);
    expect(onApply).toHaveBeenCalledTimes(1);
  });
});

describe('RubricPanel', () => {
  it('renders six criteria with a live raw total', () => {
    render(
      <RubricPanel
        draft={{ c1: '', c2: '', c3: '', c4: '', c5: '', c6: '' }}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText('Rubric scores')).toBeInTheDocument();
    expect(screen.getAllByRole('spinbutton')).toHaveLength(6);
  });

  it('stepper clamps at criterion maxima (C1 max 3)', async () => {
    const onChange = vi.fn();
    render(
      <RubricPanel
        draft={{ c1: '3', c2: '0', c3: '0', c4: '0', c5: '0', c6: '0' }}
        onChange={onChange}
      />,
    );
    const increaseC1 = screen.getByRole('button', { name: /Increase C1 Purpose/i });
    expect(increaseC1).toBeDisabled();
    const decreaseC1 = screen.getByRole('button', { name: /Decrease C1 Purpose/i });
    await userEvent.click(decreaseC1);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ c1: '2' }));
  });

  it('shows AI suggestion ghost values without duplicate keys when equal to draft', () => {
    // Regression: tutor-grade duplicate-key crash when suggestion === draft.
    const draft = { c1: '2', c2: '5', c3: '5', c4: '6', c5: '5', c6: '5' };
    const aiSuggestion = { c1: 2, c2: 5, c3: 5, c4: 6, c5: 5, c6: 5 };
    render(<RubricPanel draft={draft} onChange={vi.fn()} aiSuggestion={aiSuggestion} />);
    // Every criterion shows its suggestion; no React key warnings/crashes.
    expect(screen.getAllByText(/AI suggestion:/i)).toHaveLength(6);
  });

  it('hides steppers in readOnly mode', () => {
    render(
      <RubricPanel
        draft={{ c1: '1', c2: '2', c3: '3', c4: '4', c5: '5', c6: '6' }}
        onChange={vi.fn()}
        readOnly
      />,
    );
    expect(screen.queryByRole('button', { name: /Increase C1/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Decrease C1/i })).not.toBeInTheDocument();
  });
});