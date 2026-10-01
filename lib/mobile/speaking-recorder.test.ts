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

  describe('for the live AI patient ({ live: true })', () => {
    // The learner speaks to the patient and nothing is recorded; the control on that panel reads "Start speaking".
    it('words the permission recovery with the live control, on the web and in the apps', () => {
      const denied = new DOMException('Permission denied', 'NotAllowedError');

      const web = describeMicrophoneError(denied, false, { live: true });
      expect(web.permissionDenied).toBe(true);
      expect(web.message).toBe('Microphone permission was blocked. Allow microphone access in your browser settings, then press Start speaking again.');

      const native = describeMicrophoneError(denied, true, { live: true });
      expect(native.permissionDenied).toBe(true);
      expect(native.message).toBe('Microphone permission was blocked. Allow Microphone for this app in your device Settings (Open app settings), then press Start speaking again.');
    });

    it('never calls the audio a recording', () => {
      expect(describeMicrophoneError(new DOMException('', 'OverconstrainedError'), false, { live: true })).toEqual({
        permissionDenied: false,
        message: 'This microphone does not support the requested audio settings. Try another device.',
      });
      expect(describeMicrophoneError(new DOMException('', 'NotSupportedError'), false, { live: true })).toEqual({
        permissionDenied: false,
        message: 'This browser does not support the audio mode needed for Speaking practice.',
      });
      expect(describeMicrophoneError(new Error('boom'), false, { live: true })).toEqual({
        permissionDenied: false,
        message: 'The microphone could not start: boom',
      });
    });

    it('keeps the wording that is the same for both, and the recorder default unchanged', () => {
      expect(describeMicrophoneError(new DOMException('', 'NotFoundError'), false, { live: true }).message)
        .toBe(describeMicrophoneError(new DOMException('', 'NotFoundError'), false).message);
      expect(describeMicrophoneError(new Error(''), false, { live: true }).message)
        .toBe('Could not start the microphone. Check your audio settings and try again.');

      // The recorder hook keeps its own words.
      expect(describeMicrophoneError(new DOMException('', 'OverconstrainedError'), false).message)
        .toBe('This microphone does not support the requested recording settings. Try another device.');
      expect(describeMicrophoneError(new DOMException('', 'NotSupportedError'), false).message)
        .toBe('This browser does not support the recording mode needed for Speaking practice.');
      expect(describeMicrophoneError(new DOMException('Permission denied', 'NotAllowedError'), false).message)
        .toBe('Microphone permission was blocked. Allow microphone access in your browser settings, then press Start recording again.');
    });

    it('takes the platform from the environment when only the live option is given', () => {
      // The realtime hook passes `undefined` for the platform: the default applies (the web, in tests).
      const denied = describeMicrophoneError(new DOMException('Permission denied', 'NotAllowedError'), undefined, { live: true });
      expect(denied.message).toMatch(/browser settings, then press Start speaking again\.$/);
    });
  });
});
