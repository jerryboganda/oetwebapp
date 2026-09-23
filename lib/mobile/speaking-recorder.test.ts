import { describe, expect, it } from 'vitest';
import { describeMicrophoneError } from './speaking-recorder';

describe('describeMicrophoneError', () => {
  it('flags a WebView/browser NotAllowedError as a permission denial with platform-specific recovery wording', () => {
    const denied = new DOMException('Permission denied', 'NotAllowedError');

    const web = describeMicrophoneError(denied, false);
    expect(web.permissionDenied).toBe(true);
    expect(web.message).toMatch(/browser settings/);

    const native = describeMicrophoneError(denied, true);
    expect(native.permissionDenied).toBe(true);
    expect(native.message).toMatch(/Open app settings/);
    expect(native.message).not.toMatch(/browser/);
  });

  it('treats the native SpeakingRecorder plugin rejection as a permission denial', () => {
    const info = describeMicrophoneError(new Error('Microphone permission was denied.'), true);
    expect(info.permissionDenied).toBe(true);
  });

  it('keeps hardware problems distinct from permission problems', () => {
    expect(describeMicrophoneError(new DOMException('', 'NotFoundError'), true)).toEqual({
      permissionDenied: false,
      message: 'No microphone was found. Connect a microphone and try again.',
    });
    expect(describeMicrophoneError(new DOMException('', 'NotReadableError'), false).message).toMatch(/busy/);
    expect(describeMicrophoneError(new Error('boom'), false)).toEqual({
      permissionDenied: false,
      message: 'Recording could not start: boom',
    });
  });
});
