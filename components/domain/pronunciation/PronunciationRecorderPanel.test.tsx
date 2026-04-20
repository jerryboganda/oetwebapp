import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PronunciationRecorderPanel } from '@/components/domain/pronunciation/PronunciationRecorderPanel';

vi.mock('@/hooks/usePronunciationRecorder', () => ({
  usePronunciationRecorder: () => ({
    status: 'idle',
    permission: 'unknown',
    errorMessage: null,
    level: 0,
    elapsedMs: 0,
    result: null,
    requestPermission: vi.fn(),
    start: vi.fn(),
    stop: vi.fn(),
    reset: vi.fn(),
  }),
}));

describe('PronunciationRecorderPanel', () => {
  it('prompts the user to enable the microphone when permission is unknown', () => {
    render(<PronunciationRecorderPanel onUpload={vi.fn()} />);
    expect(screen.getByRole('heading', { name: /record your attempt/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /enable microphone/i })).toBeInTheDocument();
  });

  it('shows the contextual subtitle when no model audio is provided', () => {
    render(<PronunciationRecorderPanel onUpload={vi.fn()} />);
    expect(screen.getByText(/record yourself saying the example words and sentences/i)).toBeInTheDocument();
  });

  it('shows the model-audio subtitle when a model audio URL is passed', () => {
    render(<PronunciationRecorderPanel onUpload={vi.fn()} modelAudioUrl="https://example.com/model.mp3" />);
    expect(screen.getByText(/listen to the model, then record yourself/i)).toBeInTheDocument();
  });

  it('uses a semantic landmark heading for accessibility', () => {
    render(<PronunciationRecorderPanel onUpload={vi.fn()} />);
    const region = screen.getByRole('region', { name: /record your attempt/i });
    expect(region).toBeInTheDocument();
  });
});
