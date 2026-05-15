import type { Page } from '@playwright/test';

export async function installFakeRecordingMedia(page: Page) {
  await page.addInitScript(() => {
    const fakeWebmBytes = new Uint8Array([
      0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81,
      0x01, 0x42, 0xF7, 0x81, 0x01, 0x42, 0xF2, 0x81,
      0x04, 0x42, 0xF3, 0x81, 0x08,
    ]);

    class FakeMediaStreamTrack {
      kind = 'audio';

      stop() {
        return undefined;
      }
    }

    class FakeMediaStream {
      getTracks() {
        return [new FakeMediaStreamTrack()];
      }
    }

    class FakeAnalyserNode {
      fftSize = 32;
      frequencyBinCount = 16;

      getByteFrequencyData(array: Uint8Array) {
        for (let index = 0; index < array.length; index += 1) {
          array[index] = 128;
        }
      }
    }

    class FakeAudioContext {
      state: 'running' | 'closed' = 'running';

      createMediaStreamSource() {
        return {
          connect() {
            return undefined;
          },
        };
      }

      createAnalyser() {
        return new FakeAnalyserNode();
      }

      close() {
        this.state = 'closed';
        return Promise.resolve();
      }
    }

    class FakeMediaRecorder extends EventTarget {
      static isTypeSupported() {
        return true;
      }

      mimeType = 'audio/webm';
      ondataavailable: ((event: Event & { data: Blob }) => void) | null = null;
      onstop: ((event: Event) => void) | null = null;
      onerror: ((event: Event) => void) | null = null;
      state: 'inactive' | 'recording' | 'paused' = 'inactive';
      private intervalId: number | null = null;

      constructor(public readonly stream: MediaStream) {
        super();
      }

      start() {
        this.state = 'recording';
        this.intervalId = window.setInterval(() => {
          if (this.state !== 'recording') {
            return;
          }

          const event = new Event('dataavailable') as Event & { data: Blob };
          Object.defineProperty(event, 'data', {
            configurable: true,
            enumerable: true,
            value: new Blob([fakeWebmBytes], { type: this.mimeType }),
          });
          this.dispatchEvent(event);
          this.ondataavailable?.(event);
        }, 75);
      }

      pause() {
        this.state = 'paused';
      }

      resume() {
        this.state = 'recording';
      }

      stop() {
        if (this.intervalId !== null) {
          window.clearInterval(this.intervalId);
          this.intervalId = null;
        }

        if (this.state === 'inactive') {
          return;
        }

        this.state = 'inactive';
        const dataEvent = new Event('dataavailable') as Event & { data: Blob };
        Object.defineProperty(dataEvent, 'data', {
          configurable: true,
          enumerable: true,
          value: new Blob([fakeWebmBytes], { type: this.mimeType }),
        });
        this.dispatchEvent(dataEvent);
        this.ondataavailable?.(dataEvent);

        const stopEvent = new Event('stop');
        this.dispatchEvent(stopEvent);
        this.onstop?.(stopEvent);
      }
    }

    Object.defineProperty(window, 'AudioContext', {
      configurable: true,
      writable: true,
      value: FakeAudioContext,
    });

    Object.defineProperty(window, 'webkitAudioContext', {
      configurable: true,
      writable: true,
      value: FakeAudioContext,
    });

    Object.defineProperty(window, 'MediaRecorder', {
      configurable: true,
      writable: true,
      value: FakeMediaRecorder,
    });

    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: async () => new FakeMediaStream(),
      },
    });
  });
}
