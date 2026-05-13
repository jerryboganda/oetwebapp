import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListeningSectionStepper } from '../ListeningSectionStepper';
import { ListeningPreviewBanner, ListeningReviewBanner } from '../ListeningPhaseBanner';
import { ListeningAudioTransport } from '../ListeningAudioTransport';

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
    isPreviewPhase: false,
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

  it('omits the scrub slider when canScrub=false', () => {
    render(<ListeningAudioTransport {...baseProps} canScrub={false} />);
    expect(screen.getByTestId('listening-audio-transport').querySelector('input[type="range"]')).toBeNull();
  });
});
