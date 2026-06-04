import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/react';
import { WritingReadingWindowOverlay } from '../WritingReadingWindowOverlay';
import type { WritingScenarioDto } from '@/lib/writing/types';

// Mock the child so we never pull in pdf.js / the real viewer.
vi.mock('../WritingStimulus', () => ({
  WritingStimulus: () => <div data-testid="stimulus" />,
}));

const scenario: WritingScenarioDto = {
  id: 'scen-1',
  title: 'Letter to Dr Smith',
  letterType: 'LT-RR',
  profession: 'medicine',
  subDiscipline: null,
  topics: [],
  difficulty: 3,
  caseNotesMarkdown: '## Patient\n- John Doe\n',
  caseNotesStructured: [],
  isDiagnostic: false,
  status: 'published',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
  stimulusPdfDownloadPath: '/v1/media/pdf-abc/content',
};

afterEach(() => {
  cleanup();
  // Reset any scroll lock the overlay may have left behind between tests.
  document.body.style.overflow = '';
});

describe('WritingReadingWindowOverlay', () => {
  it('renders the countdown and stimulus when open, nothing when closed', () => {
    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={125}
        onAutoClose={vi.fn()}
      />,
    );

    // 125s -> 02:05
    expect(screen.getByText('02:05')).toBeInTheDocument();
    expect(screen.getByTestId('stimulus')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true');

    rerender(
      <WritingReadingWindowOverlay
        open={false}
        scenario={scenario}
        secondsRemaining={125}
        onAutoClose={vi.fn()}
      />,
    );

    expect(screen.queryByText('02:05')).not.toBeInTheDocument();
    expect(screen.queryByTestId('stimulus')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('strict mode (allowSkip=false) renders no skip button', () => {
    render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={300}
        allowSkip={false}
        onAutoClose={vi.fn()}
      />,
    );

    expect(
      screen.queryByRole('button', { name: /start writing early/i }),
    ).toBeNull();
  });

  it('practice mode: clicking "Start writing early" calls onSkip', () => {
    const onSkip = vi.fn();
    render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={300}
        allowSkip
        onSkip={onSkip}
        onAutoClose={vi.fn()}
      />,
    );

    fireEvent.click(
      screen.getByRole('button', { name: /start writing early/i }),
    );
    expect(onSkip).toHaveBeenCalledTimes(1);
  });

  it('calls onAutoClose exactly once when secondsRemaining first hits 0', () => {
    const onAutoClose = vi.fn();
    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={3}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).not.toHaveBeenCalled();

    rerender(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={0}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).toHaveBeenCalledTimes(1);

    // Persisting at zero (and going negative) must NOT re-fire.
    rerender(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={0}
        onAutoClose={onAutoClose}
      />,
    );
    rerender(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={-1}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onAutoClose prematurely while time remains', () => {
    const onAutoClose = vi.fn();
    render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={5}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).not.toHaveBeenCalled();
  });

  it('re-fires onAutoClose on a fresh open cycle (guard resets on close)', () => {
    const onAutoClose = vi.fn();
    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={0}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).toHaveBeenCalledTimes(1);

    rerender(
      <WritingReadingWindowOverlay
        open={false}
        scenario={scenario}
        secondsRemaining={0}
        onAutoClose={onAutoClose}
      />,
    );
    rerender(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={0}
        onAutoClose={onAutoClose}
      />,
    );
    expect(onAutoClose).toHaveBeenCalledTimes(2);
  });

  it('swallows Escape (preventDefault) without calling onAutoClose/onSkip', () => {
    const onAutoClose = vi.fn();
    const onSkip = vi.fn();
    render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        allowSkip
        onSkip={onSkip}
        onAutoClose={onAutoClose}
      />,
    );

    const escapeEvent = new KeyboardEvent('keydown', {
      key: 'Escape',
      cancelable: true,
      bubbles: true,
    });
    document.dispatchEvent(escapeEvent);

    expect(escapeEvent.defaultPrevented).toBe(true);
    expect(onAutoClose).not.toHaveBeenCalled();
    expect(onSkip).not.toHaveBeenCalled();
  });

  it('traps the browser Back button via history.pushState + popstate re-push', () => {
    const pushSpy = vi.spyOn(window.history, 'pushState');

    render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );

    // Pushed once on open.
    expect(pushSpy).toHaveBeenCalledTimes(1);

    // A Back press (popstate) re-pushes to neutralize it.
    fireEvent.popState(window);
    expect(pushSpy).toHaveBeenCalledTimes(2);

    pushSpy.mockRestore();
  });

  it('restores focus to the previously-focused element on close', () => {
    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    // Focus moves into the overlay (off the trigger) on open.
    expect(document.activeElement).not.toBe(trigger);

    rerender(
      <WritingReadingWindowOverlay
        open={false}
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    // Focus returns to the original element on close.
    expect(document.activeElement).toBe(trigger);

    document.body.removeChild(trigger);
  });

  it('announces remaining time only at minute boundaries / final 10s (no per-second spam)', () => {
    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={125}
        onAutoClose={vi.fn()}
      />,
    );
    // 125s is not a minute boundary and > 10s → the live region stays empty.
    expect(document.querySelector('[aria-live="polite"]')?.textContent).toBe('');

    rerender(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    // 120s = 02:00 is a minute boundary → announced.
    expect(document.querySelector('[aria-live="polite"]')?.textContent).toContain('02:00');
  });

  it('locks body scroll while open and restores it on close', () => {
    const { rerender } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    expect(document.body.style.overflow).toBe('hidden');

    rerender(
      <WritingReadingWindowOverlay
        open={false}
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    expect(document.body.style.overflow).toBe('');
  });

  it('restores body scroll on unmount', () => {
    const { unmount } = render(
      <WritingReadingWindowOverlay
        open
        scenario={scenario}
        secondsRemaining={120}
        onAutoClose={vi.fn()}
      />,
    );
    expect(document.body.style.overflow).toBe('hidden');

    unmount();
    expect(document.body.style.overflow).toBe('');
  });
});
