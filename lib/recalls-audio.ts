export function playTransientAudio(url: string) {
  const audioElement = new Audio(url);
  const revokeObjectUrl = () => {
    if (url.startsWith('blob:')) URL.revokeObjectURL(url);
  };

  if (typeof audioElement.addEventListener === 'function') {
    audioElement.addEventListener('ended', revokeObjectUrl, { once: true });
    audioElement.addEventListener('error', revokeObjectUrl, { once: true });
  }
  const playResult = audioElement.play();
  if (playResult && typeof playResult.catch === 'function') {
    void playResult.catch(() => revokeObjectUrl());
  }

  return audioElement;
}

/**
 * Speak text using the browser's built-in Web Speech API (SpeechSynthesis).
 * Used as a fallback when server-side TTS audio is unavailable.
 */
export function speakWithBrowserTts(text: string, lang = 'en-GB'): boolean {
  if (typeof window === 'undefined' || !window.speechSynthesis) return false;

  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = lang;
  utterance.rate = 0.9;

  // Try to pick a good English voice
  const voices = window.speechSynthesis.getVoices();
  const preferred = voices.find(
    (v) => v.lang.startsWith('en') && v.name.toLowerCase().includes('female'),
  ) ?? voices.find((v) => v.lang.startsWith('en-GB'))
    ?? voices.find((v) => v.lang.startsWith('en'));
  if (preferred) utterance.voice = preferred;

  window.speechSynthesis.speak(utterance);
  return true;
}