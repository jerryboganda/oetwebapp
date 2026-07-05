'use client';

import { useCallback, useRef } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';

/**
 * Grab-to-pan ("Hand tool") for any pdf.js viewer.
 *
 * Every PDF surface in the app renders pdf.js → <canvas> inside (or under) a
 * scrolling container. On touch devices — and always with a mouse — dragging
 * the paper to move it is the natural gesture, but the browser only offers it
 * inconsistently for nested scrollers, and not at all for a mouse. This hook
 * gives every viewer the same reliable "grab the page and drag" behaviour:
 *
 * - On pointer-down it finds the nearest scrollable ancestor and, as the pointer
 *   moves, scrolls it by the drag delta — panning BOTH axes at once (native
 *   nested-scroll only handles one axis well on touch).
 * - It works with mouse, touch, and stylus (Pointer Events + pointer capture).
 * - It intentionally IGNORES drags that begin on an <input>, <button>, <a>,
 *   <select>, contenteditable, or any element flagged `data-nopan`, so answer
 *   fields, delete buttons, and annotation handles stay fully usable.
 *
 * The hook is deliberately DOM-only (no pdf.js coupling) so it can be shared by
 * every viewer without re-introducing the deploy-time coupling the individual
 * viewers avoid by duplicating their pdf.js loading idiom.
 *
 * Apply {@link PAN_SURFACE_CLASS} to the element carrying the handlers whenever
 * `enabled` is true: it sets the grab cursor and `touch-action: none` so our JS
 * owns the touch pan (otherwise native scroll fights the drag).
 */
export const PAN_SURFACE_CLASS = 'cursor-grab touch-none select-none active:cursor-grabbing';

const INTERACTIVE_SELECTOR =
  'input, textarea, select, button, a, [role="button"], [contenteditable="true"], [data-nopan]';

function findScrollParent(el: HTMLElement | null): HTMLElement | null {
  let node: HTMLElement | null = el;
  while (node && node !== document.body && node !== document.documentElement) {
    const style = window.getComputedStyle(node);
    const canScrollY =
      (style.overflowY === 'auto' || style.overflowY === 'scroll') && node.scrollHeight > node.clientHeight;
    const canScrollX =
      (style.overflowX === 'auto' || style.overflowX === 'scroll') && node.scrollWidth > node.clientWidth;
    if (canScrollY || canScrollX) return node;
    node = node.parentElement;
  }
  return document.scrollingElement as HTMLElement | null;
}

interface PanState {
  scroller: HTMLElement;
  captureTarget: HTMLElement;
  pointerId: number;
  startX: number;
  startY: number;
  startLeft: number;
  startTop: number;
  moved: boolean;
}

export interface PanScrollHandlers {
  onPointerDown: (event: ReactPointerEvent<HTMLElement>) => void;
  onPointerMove: (event: ReactPointerEvent<HTMLElement>) => void;
  onPointerUp: (event: ReactPointerEvent<HTMLElement>) => void;
  onPointerCancel: (event: ReactPointerEvent<HTMLElement>) => void;
}

/**
 * @param enabled When false every handler is a no-op — pass the viewer's
 *   "is the Hand tool the active tool?" flag so drawing tools take over cleanly.
 */
export function usePanScroll(enabled: boolean): PanScrollHandlers {
  const state = useRef<PanState | null>(null);

  const onPointerDown = useCallback(
    (event: ReactPointerEvent<HTMLElement>) => {
      if (!enabled) return;
      // Left button only for mouse; any primary contact for touch/pen.
      if (event.pointerType === 'mouse' && event.button !== 0) return;
      const target = event.target as HTMLElement | null;
      if (target?.closest(INTERACTIVE_SELECTOR)) return; // keep inputs/buttons interactive
      const scroller = findScrollParent(event.currentTarget);
      if (!scroller) return;
      const captureTarget = event.currentTarget;
      state.current = {
        scroller,
        captureTarget,
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        startLeft: scroller.scrollLeft,
        startTop: scroller.scrollTop,
        moved: false,
      };
      try {
        captureTarget.setPointerCapture(event.pointerId);
      } catch {
        /* capture is best-effort */
      }
    },
    [enabled],
  );

  const onPointerMove = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    const s = state.current;
    if (!s || s.pointerId !== event.pointerId) return;
    const dx = event.clientX - s.startX;
    const dy = event.clientY - s.startY;
    if (!s.moved && Math.abs(dx) + Math.abs(dy) > 3) s.moved = true;
    s.scroller.scrollLeft = s.startLeft - dx;
    s.scroller.scrollTop = s.startTop - dy;
    event.preventDefault();
  }, []);

  const end = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    const s = state.current;
    if (!s || s.pointerId !== event.pointerId) return;
    try {
      s.captureTarget.releasePointerCapture(event.pointerId);
    } catch {
      /* nothing captured */
    }
    state.current = null;
  }, []);

  return { onPointerDown, onPointerMove, onPointerUp: end, onPointerCancel: end };
}
