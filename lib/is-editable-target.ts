/**
 * True when a keyboard event originated inside a field the learner is typing in.
 *
 * Global and card-level shortcut handlers (Space to replay audio, Space/Enter to
 * activate a card) must consult this before calling `preventDefault()`, otherwise
 * they swallow the keystroke that belongs to the input. That is exactly how
 * Spacebar stopped typing spaces in Recalls > Practice Spelling: the spelling
 * input renders inside a card whose `onKeyDown` replayed the audio on Space.
 */
export function isEditableEventTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return (
    target instanceof HTMLInputElement
    || target instanceof HTMLTextAreaElement
    || target instanceof HTMLSelectElement
    // `=== true`, not a bare truthiness check: `isContentEditable` is typed
    // boolean but is undefined on a detached element (and in jsdom), which would
    // leak undefined out of a function that promises a boolean.
    || target.isContentEditable === true
  );
}
