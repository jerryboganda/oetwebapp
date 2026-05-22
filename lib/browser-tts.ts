/**
 * Browser-based text-to-speech using the Web Speech API.
 *
 * This is a zero-cost, instant pronunciation helper that works offline.
 * Used as the default audio playback method for vocabulary terms when no
 * pre-recorded/AI-generated audio asset is available.
 */

/** Check if the browser supports speech synthesis */
export function isBrowserTtsAvailable(): boolean {
  return typeof window !== 'undefined' && 'speechSynthesis' in window;
}

export interface BrowserTtsOptions {
  /** Speech rate (0.1 – 10, default 0.9 for clarity) */
  rate?: number;
  /** Pitch (0 – 2, default 1) */
  pitch?: number;
  /** Preferred voice language (default 'en-GB' for British pronunciation) */
  lang?: string;
  /** Preferred voice name substring match (e.g. 'Google UK', 'Daniel') */
  voiceHint?: string;
}

/**
 * Speak a term using the Web Speech API.
 * Returns a promise that resolves when speech finishes or rejects on error.
 */
export function speakTerm(
  text: string,
  options: BrowserTtsOptions = {},
): Promise<void> {
  return new Promise((resolve, reject) => {
    if (!isBrowserTtsAvailable()) {
      reject(new Error('Browser TTS not available'));
      return;
    }

    // Cancel any ongoing speech
    window.speechSynthesis.cancel();

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.rate = options.rate ?? 0.9;
    utterance.pitch = options.pitch ?? 1;
    utterance.lang = options.lang ?? 'en-GB';

    // Try to find a preferred voice
    const voices = window.speechSynthesis.getVoices();
    const hint = options.voiceHint?.toLowerCase();
    const preferredVoice = voices.find((v) => {
      if (hint && v.name.toLowerCase().includes(hint)) return true;
      return v.lang.startsWith(utterance.lang) && v.localService;
    }) ?? voices.find((v) => v.lang.startsWith(utterance.lang));

    if (preferredVoice) {
      utterance.voice = preferredVoice;
    }

    utterance.onend = () => resolve();
    utterance.onerror = (event) => {
      if (event.error === 'canceled' || event.error === 'interrupted') {
        resolve();
      } else {
        reject(new Error(`TTS error: ${event.error}`));
      }
    };

    window.speechSynthesis.speak(utterance);
  });
}

/**
 * Preload voices — some browsers load voices asynchronously.
 * Call this once on page mount to ensure voices are ready.
 */
export function preloadVoices(): void {
  if (!isBrowserTtsAvailable()) return;
  // Trigger voice loading
  window.speechSynthesis.getVoices();
  // Some browsers fire voiceschanged event
  if (typeof window.speechSynthesis.onvoiceschanged !== 'undefined') {
    window.speechSynthesis.onvoiceschanged = () => {
      window.speechSynthesis.getVoices();
    };
  }
}
