import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WaveformCuePointEditor, type WaveformCuePointEditorProps } from '../WaveformCuePointEditor';

// ── Module mocks ────────────────────────────────────────────────────────────
// Mirror the existing component-test pattern (audio-player-waveform.test.tsx /
// ListeningExtractMetadataEditor.test.tsx): hoist the spies, then point the
// `@/lib/...` modules at them.

const { mockFetchAuthorizedObjectUrl, mockPatchListeningExtract } = vi.hoisted(() => ({
  mockFetchAuthorizedObjectUrl: vi.fn(async (url: string) => `blob:${url}`),
  mockPatchListeningExtract: vi.fn(async () => ({ extracts: [] })),
}));

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: (url: string) => mockFetchAuthorizedObjectUrl(url),
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  patchListeningExtract: (
    paperId: string,
    extractCode: string,
    patch: { audioStartMs: number | null; audioEndMs: number | null },
  ) => mockPatchListeningExtract(paperId, extractCode, patch),
}));

// ── WebAudio / media stubs (jsdom ships none of these) ──────────────────────

class FakeAudioBuffer {
  duration = 12; // seconds → 12000 ms
  length = 48000;
  numberOfChannels = 1;
  private channel = new Float32Array(this.length).fill(0.5);
  getChannelData() {
    return this.channel;
  }
}

class FakeAudioContext {
  decodeAudioData = vi.fn(async () => new FakeAudioBuffer() as unknown as AudioBuffer);
  close = vi.fn(async () => undefined);
}

function installAudioStubs() {
  (globalThis as unknown as { AudioContext: unknown }).AudioContext = FakeAudioContext;
  // Canvas 2d context — jsdom returns null otherwise. The component tolerates
  // null, but a stub lets the draw path exercise without throwing.
  if (!HTMLCanvasElement.prototype.getContext) {
    Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
      configurable: true,
      value: () => ({
        setTransform: vi.fn(),
        clearRect: vi.fn(),
        fillRect: vi.fn(),
        fillStyle: '',
      }),
    });
  }
  // Object URL + media playback.
  if (!URL.createObjectURL) {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:fake') });
  }
  if (!URL.revokeObjectURL) {
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
  }
  Object.defineProperty(HTMLMediaElement.prototype, 'play', {
    configurable: true,
    value: vi.fn(async () => undefined),
  });
  Object.defineProperty(HTMLMediaElement.prototype, 'pause', {
    configurable: true,
    value: vi.fn(),
  });
  // `fetch(blobUrl)` inside the decode path → return a tiny ArrayBuffer.
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => ({ arrayBuffer: async () => new ArrayBuffer(8) })),
  );
}

const baseProps = {
  paperId: 'paper-1',
  extractCode: 'A1' as const,
  audioUrl: '/v1/media/media-a/content',
  audioStartMs: 1000,
  audioEndMs: 5000,
};

async function renderReady(props: Partial<typeof baseProps> & { onChange?: WaveformCuePointEditorProps['onChange'] } = {}) {
  const onChange = props.onChange ?? vi.fn();
  render(<WaveformCuePointEditor {...baseProps} {...props} onChange={onChange} />);
  // Wait for fetch → decode → ready so the handles mount.
  await screen.findByTestId('cue-handle-start');
  return { onChange };
}

describe('WaveformCuePointEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    installAudioStubs();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('fetches the extract audio through the authorized-object-URL helper and decodes it', async () => {
    await renderReady();
    expect(mockFetchAuthorizedObjectUrl).toHaveBeenCalledWith('/v1/media/media-a/content');
    // Length chip proves the decoded 12s duration was read.
    expect(screen.getByText(/Length 00:12/)).toBeInTheDocument();
  });

  it('renders the attach-audio hint and no handles when no audio is attached', () => {
    render(<WaveformCuePointEditor {...baseProps} audioUrl={null} onChange={vi.fn()} />);
    expect(screen.getByText(/Attach an audio asset/i)).toBeInTheDocument();
    expect(screen.queryByTestId('cue-handle-start')).not.toBeInTheDocument();
    expect(mockFetchAuthorizedObjectUrl).not.toHaveBeenCalled();
  });

  it('updates the start ms via the numeric input', async () => {
    const { onChange } = await renderReady();
    fireEvent.change(screen.getByTestId('cue-input-start'), { target: { value: '2500' } });
    expect(onChange).toHaveBeenCalledWith({ audioStartMs: 2500, audioEndMs: 5000 });
  });

  it('updates the end ms via the numeric input, clamped to the audio duration', async () => {
    const { onChange } = await renderReady();
    // 99999 ms exceeds the 12000 ms decoded length → clamps to 12000.
    fireEvent.change(screen.getByTestId('cue-input-end'), { target: { value: '99999' } });
    expect(onChange).toHaveBeenCalledWith({ audioStartMs: 1000, audioEndMs: 12000 });
  });

  it('nudges the start handle with the arrow keys', async () => {
    const { onChange } = await renderReady();
    fireEvent.keyDown(screen.getByTestId('cue-handle-start'), { key: 'ArrowRight' });
    // Coarse step is 1000 ms: 1000 → 2000.
    expect(onChange).toHaveBeenCalledWith({ audioStartMs: 2000, audioEndMs: 5000 });
  });

  it('applies a fine nudge with Shift+Arrow', async () => {
    const { onChange } = await renderReady();
    fireEvent.keyDown(screen.getByTestId('cue-handle-end'), { key: 'ArrowLeft', shiftKey: true });
    // Fine step is 100 ms: 5000 → 4900.
    expect(onChange).toHaveBeenCalledWith({ audioStartMs: 1000, audioEndMs: 4900 });
  });

  it('exposes accessible slider semantics on both handles', async () => {
    await renderReady();
    const sliders = screen.getAllByRole('slider');
    expect(sliders).toHaveLength(2);
    const start = screen.getByTestId('cue-handle-start');
    expect(start).toHaveAttribute('aria-valuenow', '1000');
    expect(start).toHaveAttribute('aria-valuemin', '0');
    expect(start).toHaveAttribute('aria-valuemax', '12000');
  });

  it('saves cue points via the existing patchListeningExtract helper', async () => {
    const onSaved = vi.fn();
    render(<WaveformCuePointEditor {...baseProps} onChange={vi.fn()} onSaved={onSaved} />);
    await screen.findByTestId('cue-handle-start');

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /save cue points/i }));
    });

    await waitFor(() =>
      expect(mockPatchListeningExtract).toHaveBeenCalledWith('paper-1', 'A1', {
        audioStartMs: 1000,
        audioEndMs: 5000,
      }),
    );
    expect(onSaved).toHaveBeenCalled();
    expect(await screen.findByText(/Cue points saved\./i)).toBeInTheDocument();
  });

  it('surfaces a save error when the patch helper rejects', async () => {
    mockPatchListeningExtract.mockRejectedValueOnce(new Error('Conflict'));
    render(<WaveformCuePointEditor {...baseProps} onChange={vi.fn()} />);
    await screen.findByTestId('cue-handle-start');

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /save cue points/i }));
    });

    expect(await screen.findByText('Conflict')).toBeInTheDocument();
  });

  it('shows a decode error when the audio cannot be decoded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new Error('network down');
      }),
    );
    render(<WaveformCuePointEditor {...baseProps} onChange={vi.fn()} />);
    expect(await screen.findByText(/Audio could not be decoded/i)).toBeInTheDocument();
  });
});
