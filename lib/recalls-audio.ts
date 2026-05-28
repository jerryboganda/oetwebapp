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