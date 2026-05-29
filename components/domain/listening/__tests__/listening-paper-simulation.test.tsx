import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import type { ListeningSessionDto } from '@/lib/listening-api';
import { ListeningPaperSimulation } from '@/components/domain/listening/ListeningPaperSimulation';

function makeSession(): ListeningSessionDto {
  return {
    paper: {
      id: 'lp-001',
      sourceKind: 'content_paper',
      title: 'OET Listening Paper',
      slug: 'oet-listening-paper',
      difficulty: 'medium',
      estimatedDurationMinutes: 42,
      scenarioType: 'standard',
      audioUrl: 'https://cdn.example/audio.mp3',
      questionPaperUrl: null,
      audioAvailable: true,
      audioUnavailableReason: null,
      assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
      transcriptPolicy: 'per_item_post_attempt',
      extracts: [
        { partCode: 'A1', displayOrder: 1, kind: 'consultation', title: 'A1 extract', accentCode: 'en-GB', speakers: [], audioStartMs: 0, audioEndMs: 60_000 },
        { partCode: 'B', displayOrder: 2, kind: 'workplace', title: 'B clip 1', accentCode: 'en-GB', speakers: [], audioStartMs: 70_000, audioEndMs: 90_000 },
        { partCode: 'C1', displayOrder: 3, kind: 'presentation', title: 'C1 extract', accentCode: 'en-US', speakers: [], audioStartMs: 100_000, audioEndMs: 160_000 },
      ],
    },
    attempt: null,
    questions: [
      { id: 'q-a1', number: 1, partCode: 'A1', text: 'A1 note ____ blank', type: 'short_answer', options: [], points: 1 },
      { id: 'q-b1', number: 25, partCode: 'B', text: 'B1 decision?', type: 'single_choice', options: ['Refer', 'Monitor', 'Discharge'], points: 1 },
      { id: 'q-c1', number: 31, partCode: 'C1', text: 'C1 idea?', type: 'single_choice', options: ['One', 'Two', 'Three'], points: 1 },
    ],
    modePolicy: {
      mode: 'paper',
      canPause: false,
      canScrub: false,
      onePlayOnly: true,
      autosave: true,
      transcriptPolicy: 'per_item_post_attempt',
      presentationStyle: 'printable_booklet',
      printableBooklet: true,
      freeNavigation: true,
      unansweredWarningRequired: true,
      finalReviewAllPartsSeconds: 120,
    },
    scoring: { maxRawScore: 42, passRawScore: 30, passScaledScore: 350 },
    readiness: { objectiveReady: true, questionCount: 42, audioAvailable: true, missingReason: null },
  } as unknown as ListeningSessionDto;
}

function renderSimulation(overrideProps: Partial<React.ComponentProps<typeof ListeningPaperSimulation>> = {}) {
  const onAnswerChange = vi.fn();
  const utils = render(
    <ListeningPaperSimulation
      session={makeSession()}
      answers={{}}
      attemptSecondsRemaining={600}
      freeNavigationActive
      onAnswerChange={onAnswerChange}
      {...overrideProps}
    />,
  );
  return { onAnswerChange, ...utils };
}

describe('ListeningPaperSimulation', () => {
  it('renders the booklet shell with toolbar, wall timer and first page', () => {
    renderSimulation();
    expect(screen.getByTestId('listening-paper-simulation')).toBeInTheDocument();
    expect(screen.getByLabelText('Paper annotation tools')).toBeInTheDocument();
    expect(screen.getByRole('timer')).toHaveTextContent('10:00');
    // First page is the A1 notes page with the gap input.
    expect(screen.getByText(/A1 extract/)).toBeInTheDocument();
    expect(screen.getByLabelText('Answer for question 1')).toBeInTheDocument();
    expect(screen.getByText('1/3')).toBeInTheDocument();
  });

  it('navigates between booklet pages with Next / Previous', () => {
    renderSimulation();
    const next = screen.getByRole('button', { name: /^Next$/i });
    fireEvent.click(next);
    // Page 2 — Part B clip with OMR bubbles.
    expect(screen.getByText('2/3')).toBeInTheDocument();
    expect(screen.getByText('B1 decision?')).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: /Question 25 options/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Previous/i }));
    expect(screen.getByText('1/3')).toBeInTheDocument();
    expect(screen.getByLabelText('Answer for question 1')).toBeInTheDocument();
  });

  it('fires onAnswerChange when a Part A gap is typed into', () => {
    const { onAnswerChange } = renderSimulation();
    fireEvent.change(screen.getByLabelText('Answer for question 1'), { target: { value: 'tachycardia' } });
    expect(onAnswerChange).toHaveBeenCalledWith('q-a1', 'tachycardia');
  });

  it('fires onAnswerChange when a Part B OMR bubble is selected', () => {
    const { onAnswerChange } = renderSimulation();
    fireEvent.click(screen.getByRole('button', { name: /^Next$/i }));
    const group = screen.getByRole('radiogroup', { name: /Question 25 options/i });
    fireEvent.click(within(group).getByText('Monitor'));
    expect(onAnswerChange).toHaveBeenCalledWith('q-b1', 'Monitor');
  });

  it('adjusts the zoom scale within the 75–150% bounds', () => {
    renderSimulation();
    const zoomOut = screen.getByLabelText('Zoom out');
    fireEvent.click(zoomOut);
    expect(screen.getByText('95%')).toBeInTheDocument();
    const zoomIn = screen.getByLabelText('Zoom in');
    fireEvent.click(zoomIn);
    fireEvent.click(zoomIn);
    expect(screen.getByText('105%')).toBeInTheDocument();
  });

  it('reflects an existing answer value on the rendered gap', () => {
    renderSimulation({ answers: { 'q-a1': 'bradycardia' } });
    expect(screen.getByLabelText('Answer for question 1')).toHaveValue('bradycardia');
  });

  it('shows the collected notice when free navigation is inactive', () => {
    renderSimulation({ freeNavigationActive: false });
    expect(screen.getByText(/answer booklet has been collected/i)).toBeInTheDocument();
    expect(screen.queryByTestId('listening-paper-simulation')).not.toBeInTheDocument();
  });
});
