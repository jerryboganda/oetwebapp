'use client';

/**
 * Capacitor mobile lifecycle motion helpers.
 *
 * Sets the `data-app-resuming` attribute on `<html>` to trigger
 * the CSS-based resume fade-in animation, then removes it after
 * the animation completes to avoid re-triggering on next paint.
 */

const RESUME_ANIMATION_MS = 200;

export function triggerResumeMotion() {
  if (typeof document === 'undefined') return;

  const root = document.documentElement;
  root.dataset.appResuming = 'true';

  // Remove after the animation duration so it can retrigger on next resume
  const timer = setTimeout(() => {
    delete root.dataset.appResuming;
  }, RESUME_ANIMATION_MS);

  return () => {
    clearTimeout(timer);
    delete root.dataset.appResuming;
  };
}
