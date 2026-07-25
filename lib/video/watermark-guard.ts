'use client';

import { useEffect, useRef, type RefObject } from 'react';

const CHECK_INTERVAL_MS = 5000;

function isIntact(node: HTMLElement): boolean {
  if (!node.isConnected) return false;
  const style = window.getComputedStyle(node);
  if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) {
    return false;
  }
  const rect = node.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

/**
 * Integrity watchdog for the forensic watermark overlay (Course Platform
 * Security Requirements §2.3). Watches for the watermark DOM node being
 * hidden, detached, or CSS-tampered (opacity zeroed, display:none) via a
 * MutationObserver plus a periodic poll — some tamper paths (e.g. an
 * injected stylesheet rule) never fire a MutationObserver callback on the
 * node itself, only change computed style.
 *
 * Calls `onTamper` at most once per continuous tampered state (it resets
 * once the node is intact again), so the caller's tamper counter increments
 * once per incident rather than once per poll tick.
 */
export function useWatermarkGuard(rootRef: RefObject<HTMLElement | null>, onTamper: () => void): void {
  const tamperedRef = useRef(false);
  const onTamperRef = useRef(onTamper);
  onTamperRef.current = onTamper;

  useEffect(() => {
    const node = rootRef.current;
    if (!node) return;

    const check = () => {
      if (!isIntact(node)) {
        if (!tamperedRef.current) {
          tamperedRef.current = true;
          onTamperRef.current();
        }
      } else {
        tamperedRef.current = false;
      }
    };

    const observer = new MutationObserver(check);
    observer.observe(node, { attributes: true, attributeFilter: ['style', 'class', 'hidden'] });
    if (node.parentElement) {
      observer.observe(node.parentElement, { childList: true });
    }

    const interval = window.setInterval(check, CHECK_INTERVAL_MS);
    check();

    return () => {
      observer.disconnect();
      window.clearInterval(interval);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- rootRef.current is read once per mount; the ref object identity is stable.
  }, [rootRef]);
}
