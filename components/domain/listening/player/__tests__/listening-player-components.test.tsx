import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListeningSectionStepper } from '../ListeningSectionStepper';
import { ListeningPreviewBanner, ListeningReviewBanner } from '../ListeningPhaseBanner';
import { ListeningAudioTransport } from '../ListeningAudioTransport';
import { ListeningIntroCard } from '../ListeningIntroCard';
import { listeningSessionFixture } from '../__stories__/fixture';

const introCardProps = {
  session: listeningSessionFixture,
  isExam: true,
  drillId: null,
  strictReadinessRequired: true,
  techReadiness: null,
  audioUrls: [],
  isStarting: false,
  audioError: null,
  startError: null,
  onTechReadinessReady: vi.fn(),
  onStart: vi.fn(),
};

describe('ListeningIntroCard', () => {
  it('describes irreversible one-play controls for strict mode', () => {
    render(<ListeningIntroCard {...introCardProps} />);

    expect(screen.getByText(/Audio plays once per section and cannot be paused/i)).toBeInTheDocument();
    expect(screen.getByText(/Forward-only:/i)).toBeInTheDocument();
  });

  it('describes policy-controlled review controls for practice mode', () => {
    render(
      <ListeningIntroCard
        {...introCardProps}
        isExam={false}
        strictReadinessRequired={false}
        session={{
          ...listeningSessionFixture,
          modePolicy: {
            ...listeningSessionFixture.modePolicy,
            mode: 'practice',
            canPause: true,
            canScrub: true,
            onePlayOnly: false,
          },
        }}
      />,
    );

    expect(screen.getByText(/Practice controls:/i)).toBeInTheDocument();
    expect(screen.queryByText(/Audio plays once per section and cannot be paused/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Forward-only:/i)).not.toBeInTheDocument();
  });
});

describe('ListeningSectionStepper', () => {
  it('marks past sections as locked and current as active', () => {
    render(<ListeningSectionStepper sections={['A1', 'B', 'C1']} currentIndex={1} isReviewing={false} />);
    const pills = screen.getByTestId('listening-section-stepper').querySelectorAll('[data-state]');
    expect(pills.length).toBe(3);
    expect(pills[0].getAttribute('data-state')).toBe('locked');
    expect(pills[1].getAttribute('data-state')).toBe('active');
    expect(pills[2].getAttribute('data-state')).toBe('pending');
  });

  it('renders the active section as reviewing when isReviewing=true', () => {
    render(<ListeningSectionStepper sections={['A1', 'B', 'C1']} currentIndex={0} isReviewing />);
    const pills = screen.getByTestId('listening-section-stepper').querySelectorAll('[data-state]');
    expect(pills[0].getAttribute('data-state')).toBe('reviewing');
  });

  it('renders clickable available sections when free navigation is enabled', async () => {
    const onSelectSection = vi.fn();
    const user = userEvent.setup();
    render(
      <ListeningSectionStepper
        sections={['A1', 'B', 'C1']}
        currentIndex={0}
        isReviewing={false}
        freeNavigation
        onSelectSection={onSelectSection}
      />,
    );

    const pills = screen.getByTestId('listening-section-stepper').querySelectorAll('[data-state]');
    expect(pills[1].getAttribute('data-state')).toBe('available');
    expect(screen.queryByLabelText(/locked/i)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'C1, available' }));
    expect(onSelectSection).toHaveBeenCalledWith(2);
  });
});

describe('ListeningPreviewBanner', () => {
  it('shows the skip button when canSkip=true and fires onSkip', async () => {
    const onSkip = vi.fn();
    const user = userEvent.setup();
    render(<ListeningPreviewBanner section="A1" secondsRemaining={20} canSkip onSkip={onSkip} />);
    expect(screen.getByTestId('listening-preview-banner')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /start audio/i }));
    expect(onSkip).toHaveBeenCalledTimes(1);
  });

  it('hides the skip button when canSkip=false', () => {
    render(<ListeningPreviewBanner section="B" secondsRemaining={5} canSkip={false} onSkip={() => {}} />);

    expect(screen.queryByRole('button', { name: /start audio/i })).not.toBeInTheDocument();
  });
});

describe('ListeningReviewBanner', () => {
  it('fires onNext when CTA is clicked', async () => {
    const onNext = vi.fn();
    const user = userEvent.setup();
    render(<ListeningReviewBanner section="A1" secondsRemaining={30} isLastSection={false} onNext={onNext} />);
    expect(screen.getByTestId('listening-review-banner')).toBeInTheDocument();
    await user.click(screen.getByRole('button'));
    expect(onNext).toHaveBeenCalledTimes(1);
  });
});

describe('ListeningAudioTransport', () => {
  const baseProps = {
    isPlaying: false,
    progressSeconds: 30,
    durationSeconds: 240,
    canScrub: true,
    canPause: true,
    isPreviewPhase: false,
    isHalted: false,
    audioState: 'ready' as const,
    saveState: 'idle' as const,
    answeredCount: 5,
    totalQuestions: 42,
    attemptSecondsRemaining: 2400,
    onTogglePlayPause: vi.fn(),
    onScrub: vi.fn(),
  };

  it('renders the attempt timer chip when attemptSecondsRemaining is set', () => {
    render(<ListeningAudioTransport {...baseProps} />);
    expect(screen.getByTestId('listening-attempt-timer')).toBeInTheDocument();
  });

  it('uses owner-configured warning thresholds for the attempt timer', () => {
    render(
      <ListeningAudioTransport
        {...baseProps}
        attemptSecondsRemaining={45}
        warningThresholdsSeconds={[300, 60, 15]}
      />,
    );
    expect(screen.getByTestId('listening-attempt-timer').className).toContain('bg-warning/20');
  });

  it('does not invent warning bands when the policy snapshot is empty', () => {
    render(
      <ListeningAudioTransport
        {...baseProps}
        attemptSecondsRemaining={45}
        warningThresholdsSeconds={[]}
      />,
    );
    expect(screen.getByTestId('listening-attempt-timer').className).toContain('bg-white/10');
  });

  it('hides the attempt timer chip when attemptSecondsRemaining is null', () => {
    render(<ListeningAudioTransport {...baseProps} attemptSecondsRemaining={null} />);
    expect(screen.queryByTestId('listening-attempt-timer')).not.toBeInTheDocument();
  });

  it('disables play/pause during preview phase', () => {
    const onToggle = vi.fn();
    render(<ListeningAudioTransport {...baseProps} isPreviewPhase onTogglePlayPause={onToggle} />);
    const buttons = screen.getByTestId('listening-audio-transport').querySelectorAll('button');
    // First button is the play/pause toggle.
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(true);
  });

  it('halts playback and seeking while an attempt requires admin review', () => {
    render(<ListeningAudioTransport {...baseProps} isHalted />);
    const transport = screen.getByTestId('listening-audio-transport');
    const button = transport.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-label')).toMatch(/administrator review/i);
    expect(transport.querySelector('input[type="range"]')).toBeNull();
  });

  it('omits the scrub slider when canScrub=false', () => {
    render(<ListeningAudioTransport {...baseProps} canScrub={false} />);
    expect(screen.getByTestId('listening-audio-transport').querySelector('input[type="range"]')).toBeNull();
  });

  it('disables the play/pause control while playing when audio is non-pausable', () => {
    const onToggle = vi.fn();
    render(
      <ListeningAudioTransport {...baseProps} canPause={false} isPlaying onTogglePlayPause={onToggle} />,
    );
    const button = screen.getByTestId('listening-audio-transport').querySelectorAll('button')[0] as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-label')).toMatch(/cannot be paused/i);
  });

  it('keeps the play control enabled before playback even when audio is non-pausable', () => {
    render(<ListeningAudioTransport {...baseProps} canPause={false} isPlaying={false} />);
    const button = screen.getByTestId('listening-audio-transport').querySelectorAll('button')[0] as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });
});
