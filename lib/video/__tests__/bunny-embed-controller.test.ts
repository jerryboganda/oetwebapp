// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BunnyEmbedController } from '../bunny-embed-controller';

const frames: HTMLIFrameElement[] = [];

afterEach(() => {
  for (const frame of frames.splice(0)) frame.remove();
});

describe('BunnyEmbedController', () => {
  it('pins postMessage to the embed origin and ignores untrusted messages', () => {
    const frame = document.createElement('iframe');
    frame.src = 'https://iframe.mediadelivery.net/embed/123/video-1?token=t';
    document.body.appendChild(frame);
    frames.push(frame);

    const targetWindow = frame.contentWindow;
    expect(targetWindow).not.toBeNull();
    const postMessage = vi.spyOn(targetWindow!, 'postMessage').mockImplementation(() => undefined);
    const ready = vi.fn();
    const play = vi.fn();
    const controller = new BunnyEmbedController(frame);

    controller.on('ready', ready);
    controller.on('play', play);

    expect(postMessage).toHaveBeenCalledTimes(1);
    expect(postMessage).toHaveBeenLastCalledWith(
      expect.stringContaining('"value":"ready"'),
      'https://iframe.mediadelivery.net',
    );

    window.dispatchEvent(
      new MessageEvent('message', {
        data: JSON.stringify({ context: 'player.js', event: 'ready' }),
        origin: 'https://evil.example',
        source: targetWindow,
      }),
    );
    expect(ready).not.toHaveBeenCalled();

    window.dispatchEvent(
      new MessageEvent('message', {
        data: JSON.stringify({ context: 'player.js', event: 'ready' }),
        origin: 'https://iframe.mediadelivery.net',
        source: targetWindow,
      }),
    );

    expect(ready).toHaveBeenCalledTimes(1);
    expect(postMessage).toHaveBeenCalledTimes(2);
    expect(postMessage).toHaveBeenLastCalledWith(
      expect.stringContaining('"value":"play"'),
      'https://iframe.mediadelivery.net',
    );

    controller.pause();
    expect(postMessage).toHaveBeenLastCalledWith(
      expect.stringContaining('"method":"pause"'),
      'https://iframe.mediadelivery.net',
    );

    window.dispatchEvent(
      new MessageEvent('message', {
        data: { context: 'player.js', event: 'play' },
        origin: 'https://iframe.mediadelivery.net',
        source: targetWindow,
      }),
    );
    expect(play).toHaveBeenCalledTimes(1);

    controller.destroy();
  });
});
