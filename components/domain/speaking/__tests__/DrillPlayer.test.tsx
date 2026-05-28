/**
 * Vitest spec for `<DrillPlayer />`.
 *
 * Verifies the three load-bearing behaviours called out in plan G.7:
 *   1. The drill prompt + title + kind render on first paint.
 *   2. The record button toggles state (idle → recording).
 *   3. After stop + submit, the scoring API is called and feedback
 *      (score + comments) renders in the panel.
 *
 * `MediaRecorder` / `getUserMedia` / `AudioContext` are not available
 * in jsdom; we stub them to the minimum shape the component actually
 * touches. The API calls are mocked at the module boundary so we
 * exercise the player without hitting the real backend.
 */
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DrillPlayer } from '../DrillPlayer';
import type { DrillSummary } from '@/lib/api/speaking-drills';

vi.mock('@/lib/api/speaking-drills', () => ({
  uploadDrillRecording: vi.fn(async () => ({ uploaded: true })),
  scoreDrillAttempt: vi.fn(async () => ({
    attemptId: 'sda-test',
    score: 72,
    summary: 'Solid opening — you clearly signposted the agenda.',
    specificComments: ['Good empathy phrase at 5s.', 'Pace was steady.'],
    nextRecommendations: ['Retry with one more empathy phrase.'],
  })),
}));

const SAMPLE_DRILL: DrillSummary = {
  id: 'sdi-test',
  drillId: 'sdi-test',
  drillKind: 'Opening',
  title: 'Open with a wound-check follow-up',
  instructionText:
    "You're meeting Mrs Lee for a wound check. Open the conversation: greet her, confirm identity, state your purpose, invite questions.",
  targetCriteria: ['appropriateness', 'structure'],
  hasAttempted: false,
  bestScore: null,
};

// ── jsdom shims ──────────────────────────────────────────────────────────

class FakeMediaRecorder {
  public state: 'inactive' | 'recording' = 'inactive';
  public ondataavailable: ((ev: { data: Blob }) => void) | null = null;
  public onstop: (() => void) | null = null;
  public mimeType = 'audio/webm';
  start() {
    this.state = 'recording';
  }
  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['fake-audio'], { type: 'audio/webm' }) });
    this.onstop?.();
  }
}

beforeEach(() => {
  // Stub getUserMedia + MediaRecorder + AudioContext.
  Object.defineProperty(navigator, 'mediaDevices', {
    value: {
      getUserMedia: vi.fn(async () => ({
        getTracks: () => [{ stop: vi.fn() }],
      })),
    },
    configurable: true,
  });
  (globalThis as unknown as { MediaRecorder: typeof FakeMediaRecorder }).MediaRecorder =
    FakeMediaRecorder;
  // jsdom doesn't ship AudioContext — set to undefined so the
  // waveform branch skips cleanly.
  (globalThis as unknown as { AudioContext: undefined }).AudioContext = undefined;
  if (typeof URL.createObjectURL === 'undefined') {
    URL.createObjectURL = vi.fn(() => 'blob:fake');
    URL.revokeObjectURL = vi.fn();
  }
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('DrillPlayer', () => {
  it('renders the drill kind, title, and prompt on first paint', () => {
    render(<DrillPlayer drill={SAMPLE_DRILL} attemptId="sda-test" />);

    expect(screen.getByTestId('drill-player-kind').textContent).toBe('Opening');
    expect(screen.getByTestId('drill-player-title').textContent).toBe(SAMPLE_DRILL.title);
    expect(screen.getByTestId('drill-player-prompt').textContent).toContain('Open the conversation');
  });

  it('toggles the record button into the recording state when pressed', async () => {
    render(<DrillPlayer drill={SAMPLE_DRILL} attemptId="sda-test" />);

    const button = screen.getByTestId('drill-player-record') as HTMLButtonElement;
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(button.textContent).toBe('Start recording');

    await act(async () => {
      fireEvent.click(button);
    });

    await waitFor(() => {
      expect(screen.getByTestId('drill-player-record').getAttribute('aria-pressed')).toBe('true');
    });
    expect(screen.getByTestId('drill-player-record').textContent).toBe('Stop recording');
  });

  it('uploads + scores the recording after submit and shows feedback', async () => {
    const speakingDrillsModule = await import('@/lib/api/speaking-drills');
    render(<DrillPlayer drill={SAMPLE_DRILL} attemptId="sda-test" />);

    // Start + stop the recording.
    const button = screen.getByTestId('drill-player-record') as HTMLButtonElement;
    await act(async () => {
      fireEvent.click(button); // start
    });
    await act(async () => {
      fireEvent.click(screen.getByTestId('drill-player-record')); // stop (FakeMediaRecorder.stop triggers ondataavailable+onstop)
    });

    // The submit button should now be visible.
    const submit = await screen.findByTestId('drill-player-submit');
    await act(async () => {
      fireEvent.click(submit);
    });

    // Both calls fire, feedback renders.
    await waitFor(() => {
      expect(speakingDrillsModule.uploadDrillRecording).toHaveBeenCalledWith(
        'sda-test',
        expect.any(Blob),
        'audio/webm',
      );
      expect(speakingDrillsModule.scoreDrillAttempt).toHaveBeenCalledWith('sda-test');
    });
    expect((await screen.findByTestId('drill-player-score')).textContent).toBe('72');
    expect((await screen.findByTestId('drill-player-feedback')).textContent).toContain(
      'Solid opening',
    );
  });
});
