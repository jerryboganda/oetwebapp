import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { WaveformMeter } from '@/components/domain/pronunciation/WaveformMeter';

describe('WaveformMeter', () => {
  it('renders Ready state when not recording', () => {
    render(<WaveformMeter level={0} isRecording={false} elapsedMs={0} maxDurationMs={60_000} />);
    expect(screen.getByText(/Ready/i)).toBeInTheDocument();
    expect(screen.getByText(/0:00 \/ 1:00/)).toBeInTheDocument();
  });

  it('renders Recording state with elapsed time', () => {
    render(<WaveformMeter level={0.5} isRecording={true} elapsedMs={15_000} maxDurationMs={60_000} />);
    expect(screen.getByText(/Recording/i)).toBeInTheDocument();
    expect(screen.getByText(/0:15 \/ 1:00/)).toBeInTheDocument();
  });

  it('elapsed time announcement is live-polite for screen readers', () => {
    render(<WaveformMeter level={0.3} isRecording={true} elapsedMs={5_000} maxDurationMs={60_000} />);
    const el = screen.getByText(/0:05 \/ 1:00/);
    expect(el).toHaveAttribute('aria-live', 'polite');
  });

  it('handles zero max duration without crashing', () => {
    render(<WaveformMeter level={0} isRecording={false} elapsedMs={0} maxDurationMs={0} />);
    expect(screen.getByText(/0:00 \/ 0:00/)).toBeInTheDocument();
  });
});
