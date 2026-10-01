import { render, screen } from '@testing-library/react';

// The component only reads CRITERION_LABEL while a comment or the composer is open; keep the API client out of the test.
vi.mock('@/lib/api/speaking-assessments', () => ({ CRITERION_LABEL: {} }));

import { TranscriptPlayerWithComments } from '../TranscriptPlayerWithComments';

const transcript = {
  segments: [
    { speaker: 'candidate', startMs: 0, endMs: 4_000, text: 'Good morning, Mr Lee.' },
    { speaker: 'patient', startMs: 5_000, endMs: 9_000, text: 'Hello doctor.' },
  ],
};

describe('TranscriptPlayerWithComments audio strip', () => {
  it('keeps the audio strip and the seek chips by default (the expert console is unchanged)', () => {
    render(<TranscriptPlayerWithComments recordingUrl={null} transcript={transcript} comments={[]} readOnly />);

    expect(screen.getByText('Recording')).toBeInTheDocument();
    expect(screen.getByText('Recording unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Play recording' })).toBeDisabled();
    expect(screen.getAllByRole('button', { name: /^Seek to / })).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Seek to 00:05 (patient)' })).toBeInTheDocument();
  });

  it('hideAudioPlayer drops the dead player strip and the seek buttons but keeps the transcript', () => {
    render(<TranscriptPlayerWithComments recordingUrl={null} transcript={transcript} comments={[]} readOnly hideAudioPlayer />);

    expect(screen.queryByText('Recording')).not.toBeInTheDocument();
    expect(screen.queryByText('Recording unavailable')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /play recording/i })).not.toBeInTheDocument();
    expect(screen.queryAllByRole('button', { name: /^Seek to / })).toHaveLength(0);

    // The time and speaker are still shown, as plain text.
    expect(screen.getByText('[00:05]')).toBeInTheDocument();
    expect(screen.getByText('patient')).toBeInTheDocument();
    expect(screen.getByText('Good morning, Mr Lee.')).toBeInTheDocument();
    expect(screen.getByText('Hello doctor.')).toBeInTheDocument();
  });

  it('hideAudioPlayer does not mark the first segment as the one being played', () => {
    const { rerender } = render(
      <TranscriptPlayerWithComments recordingUrl={null} transcript={transcript} comments={[]} readOnly hideAudioPlayer />,
    );
    expect(screen.getByText('Good morning, Mr Lee.').closest('div.group')?.className).not.toContain('border-primary/60');

    // Default behaviour is untouched: with no playback the segment at 0 ms still reads as active.
    rerender(<TranscriptPlayerWithComments recordingUrl={null} transcript={transcript} comments={[]} readOnly />);
    expect(screen.getByText('Good morning, Mr Lee.').closest('div.group')?.className).toContain('border-primary/60');
  });
});
