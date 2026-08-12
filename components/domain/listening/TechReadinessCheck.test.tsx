import { act, fireEvent, render, screen } from '@testing-library/react';

import { TechReadinessCheck } from './TechReadinessCheck';

class FakeAudio extends EventTarget {
  static instances: FakeAudio[] = [];

  preload = '';
  volume = 1;
  private _src = '';

  set src(value: string) {
    this._src = value;
    if (value && FakeAudio.instances.length === 1) {
      queueMicrotask(() => this.dispatchEvent(new Event('canplaythrough')));
    }
  }

  get src() {
    return this._src;
  }

  constructor(url?: string) {
    super();
    FakeAudio.instances.push(this);
    if (url) this.src = url;
  }

  load() {
    if (FakeAudio.instances.length === 1) {
      this.dispatchEvent(new Event('canplaythrough'));
    } else {
      this.dispatchEvent(new Event('error'));
    }
  }

  play = vi.fn(() => Promise.resolve());

  pause() {}

  removeAttribute() {}
}

describe('TechReadinessCheck', () => {
  beforeEach(() => {
    FakeAudio.instances = [];
    vi.stubGlobal('Audio', FakeAudio as unknown as typeof Audio);
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('fails closed when a scored audio asset fails integrity verification', async () => {
    render(
      <TechReadinessCheck
        audioProbeUrl="/probe.mp3"
        audioUrls={['https://cdn.example.test/scored.mp3']}
        onReady={vi.fn()}
      />,
    );

    const volume = screen.getByRole('slider', { name: 'Sound-check volume' });
    expect(volume).toHaveValue('0.7');
    fireEvent.change(volume, { target: { value: '0.4' } });
    expect(volume).toHaveValue('0.4');
    fireEvent.click(screen.getByRole('button', { name: 'Play audio probe' }));
    expect(FakeAudio.instances[0]?.volume).toBe(0.4);
    await act(async () => {
      await vi.runAllTimersAsync();
    });

    expect(screen.getByRole('alert')).toHaveTextContent('A scored audio asset failed its readiness check.');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Continue anyway' })).not.toBeInTheDocument();
  });
});
