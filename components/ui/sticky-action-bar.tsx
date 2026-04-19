'use client';

import { cn } from '@/lib/utils';
import { motion, useReducedMotion } from 'motion/react';
import { motionTokens } from '@/lib/motion';
import { type ReactNode } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// StickyActionBar — bottom-pinned save / cancel footer.
//
// Design contract: DESIGN.md §5.15. Bottom-nav aware, safe-area aware, used
// for any edit page with batched mutations (roles, free-tier, content editor).
// ─────────────────────────────────────────────────────────────────────────────

export interface StickyActionBarProps {
  visible?: boolean;
  children?: ReactNode;
  description?: ReactNode;
  className?: string;
  align?: 'left' | 'right' | 'between';
}

export function StickyActionBar({ visible = true, children, description, className, align = 'between' }: StickyActionBarProps) {
  const reducedMotion = useReducedMotion() ?? false;
  if (!visible) return null;

  return (
    <motion.div
      initial={reducedMotion ? { opacity: 0 } : { opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={reducedMotion ? { opacity: 0 } : { opacity: 0, y: 12 }}
      transition={
        reducedMotion
          ? { duration: motionTokens.duration.instant }
          : motionTokens.spring.overlay
      }
      className={cn(
        'sticky z-30 mt-6 rounded-2xl border border-border bg-surface/95 px-4 py-3 shadow-clinical backdrop-blur-sm',
        // Bottom-nav + safe-area aware. On desktop `--bottom-nav-height` still resolves.
        'bottom-[calc(var(--bottom-nav-height,6rem)+env(safe-area-inset-bottom)+0.5rem)] lg:bottom-6',
        className,
      )}
    >
      <div
        className={cn(
          'flex flex-col gap-3 sm:flex-row sm:items-center',
          align === 'between' && 'sm:justify-between',
          align === 'right' && 'sm:justify-end',
          align === 'left' && 'sm:justify-start',
        )}
      >
        {description ? (
          <div className="text-sm text-muted">{description}</div>
        ) : null}
        <div className="flex flex-wrap items-center gap-2">{children}</div>
      </div>
    </motion.div>
  );
}
