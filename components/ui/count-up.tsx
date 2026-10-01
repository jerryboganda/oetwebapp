'use client';

import { animate, useReducedMotionConfig } from 'motion/react';
import { useLayoutEffect, useRef } from 'react';
import { motionTokens, prefersReducedMotion } from '@/lib/motion';
import { cn } from '@/lib/utils';

export interface CountUpProps {
  /** The real metric. Rendered as-is before JS, in tests and under reduced motion. */
  value: number;
  decimals?: number;
  prefix?: string;
  suffix?: string;
  className?: string;
}

/**
 * Counts a real metric up from zero once, the first time it enters the
 * viewport. The DOM starts and ends on the real value, so server HTML, tests,
 * screen readers and reduced-motion users get it unchanged. The count rewrites
 * the React-owned text node in place, so it stays one text node (`33/38`
 * remains findable as a whole). Width is reserved in `ch` with tabular digits,
 * so counting never shifts layout.
 */
export function CountUp({ value, decimals = 0, prefix = '', suffix = '', className }: CountUpProps) {
  const ref = useRef<HTMLSpanElement>(null);
  const counted = useRef(false);
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const text = `${prefix}${value.toFixed(decimals)}${suffix}`;

  useLayoutEffect(() => {
    const el = ref.current;
    const node = el?.firstChild;
    if (!el || !node || counted.current || reducedMotion || !value || !Number.isFinite(value)) return;
    if (typeof IntersectionObserver === 'undefined') return;

    const show = (n: number) => {
      node.nodeValue = `${prefix}${n.toFixed(decimals)}${suffix}`;
    };
    // Already on screen: start from zero before the first paint, so no flash.
    const rect = el.getBoundingClientRect();
    if (rect.top < window.innerHeight && rect.bottom > 0) show(0);

    let controls: ReturnType<typeof animate> | undefined;
    const observer = new IntersectionObserver((entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return;
      observer.disconnect();
      counted.current = true;
      show(0);
      controls = animate(0, value, { duration: 0.8, ease: motionTokens.ease.entrance, onUpdate: show });
    });
    observer.observe(el);

    return () => {
      observer.disconnect();
      controls?.stop();
      // React has already committed this render's text (data-final); restore it
      // in case the count stopped part-way.
      if (el.dataset.final) node.nodeValue = el.dataset.final;
    };
  }, [value, decimals, prefix, suffix, reducedMotion]);

  return (
    <span
      ref={ref}
      data-final={text}
      className={cn('inline-block tabular-nums', className)}
      style={{ minWidth: `${text.length}ch` }}
    >
      {text}
    </span>
  );
}
