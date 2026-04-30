import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MicCheckPanel } from './mic-check-panel';

type RecorderInstance = {
  state: 'inactive' | 'recording';
  mimeType: string;
  ondataavailable: ((event: { data: Blob }) => void) | null;
  onstop: (() => void) | null;
  start: () => void;
  stop: () => void;
};

const createTrack = () => ({ stop: vi.fn() });
const createStream = () => ({ getTracks: vi.fn(() => [createTrack()]) }) as unknown as MediaStream;

class MockMediaRecorder implements RecorderInstance {
  static isTypeSupported = vi.fn(() => true);
  state: 'inactive' | 'recording' = 'inactive';
  mimeType = 'audio/webm';
  ondataavailable: RecorderInstance['ondataavailable'] = null;
  onstop: RecorderInstance['onstop'] = null;

  constructor(public stream: MediaStream, public options?: MediaRecorderOptions) {
    this.mimeType = options?.mimeType || 'audio/webm';
  }

  addEventListener(event: string, handler: () => void) {
    if (event === 'stop') this.onstop = handler;
  }

  start() {
    this.state = 'recording';
  }

  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['sample'], { type: this.mimeType }) });
    this.onstop?.();
  }
}

class MockAudioContext {
  state: AudioContextState = 'running';
  createMediaStreamSource = vi.fn(() => ({ connect: vi.fn() }));
  createAnalyser = vi.fn(() => ({
    fftSize: 0,
    frequencyBinCount: 4,
    getByteFrequencyData: (array: Uint8Array) => array.fill(5),
  }));
  resume = vi.fn(() => Promise.resolve());
  close = vi.fn(() => Promise.resolve());
}

describe('MicCheckPanel', () => {
  beforeEach(() => {
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn(() => Promise.resolve(createStream())) },
    });
    (globalThis as unknown as { MediaRecorder: typeof MediaRecorder }).MediaRecorder = MockMediaRecorder as unknown as typeof MediaRecorder;
    (globalThis as unknown as { AudioContext: typeof AudioContext }).AudioContext = MockAudioContext as unknown as typeof AudioContext;
    vi.spyOn(window.URL, 'createObjectURL').mockReturnValue('blob:mic-check');
    vi.spyOn(window.URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(window.HTMLMediaElement.prototype, 'play').mockImplementation(function play(this: HTMLMediaElement) {
      setTimeout(() => this.dispatchEvent(new Event('ended')), 0);
      return Promise.resolve();
    });
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      setTimeout(() => callback(performance.now()), 16);
      return 1;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('performs a real permission, recording, playback, and noise-check flow before completing', async () => {
    const user = userEvent.setup();
    const onComplete = vi.fn();
    render(<MicCheckPanel onComplete={onComplete} />);

    await user.click(screen.getByRole('button', { name: 'Allow Access' }));
    expect(await screen.findByText('Microphone Permission')).toBeInTheDocument();
    await screen.findByRole('button', { name: 'Record 3 seconds' });

    await user.click(screen.getByRole('button', { name: 'Record 3 seconds' }));
    await screen.findByRole('button', { name: 'Play Back' }, { timeout: 5000 });

    await user.click(screen.getByRole('button', { name: 'Play Back' }));
    await screen.findByRole('button', { name: 'Check Noise' });

    await user.click(screen.getByRole('button', { name: 'Check Noise' }));

    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1), { timeout: 4000 });
    expect(screen.getByText('Background Noise Check').closest('div')).toHaveTextContent('Passed');
  }, 10000);

  it('shows a real microphone permission failure instead of marking the step as passed', async () => {
    const user = userEvent.setup();
    (navigator.mediaDevices.getUserMedia as unknown as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      new Error('Permission denied'),
    );

    render(<MicCheckPanel onComplete={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Allow Access' }));

    expect(await screen.findByText('Permission denied')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Record 3 seconds' })).not.toBeInTheDocument();
  }, 10000);
});
