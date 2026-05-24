'use client';

import { forwardRef, type HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';
import { Avatar, type AvatarSize, getInitials } from './avatar';

/* ─────────────────────────────────────────────────────────────────────
 * Sizing for the overflow chip (mirrors Avatar sizes exactly)
 * ───────────────────────────────────────────────────────────────────── */

const overflowSizeClass: Record<AvatarSize, string> = {
  xs: 'h-6 w-6 text-[10px]',
  sm: 'h-8 w-8 text-xs',
  md: 'h-10 w-10 text-sm',
  lg: 'h-12 w-12 text-base',
  xl: 'h-16 w-16 text-lg',
  '2xl': 'h-24 w-24 text-2xl',
};

/* ─────────────────────────────────────────────────────────────────────
 * Types
 * ───────────────────────────────────────────────────────────────────── */

export interface AvatarGroupItem {
  src?: string;
  name: string;
  /** Optional alt text — falls back to `name` if omitted */
  alt?: string;
}

export interface AvatarGroupProps extends HTMLAttributes<HTMLDivElement> {
  avatars: AvatarGroupItem[];
  /** Maximum number of avatars to render before showing the overflow chip. Default 3 */
  max?: number;
  /** Avatar size — applied to all children and the overflow chip */
  size?: AvatarSize;
  /** Tailwind negative-margin class controlling overlap. Default `-ml-2` */
  spacing?: string;
}

/* ─────────────────────────────────────────────────────────────────────
 * Component
 * ───────────────────────────────────────────────────────────────────── */

const AvatarGroup = forwardRef<HTMLDivElement, AvatarGroupProps>(
  (
    { avatars, max = 3, size = 'md', spacing = '-ml-2', className, ...props },
    ref,
  ) => {
    const visible = avatars.slice(0, max);
    const overflow = Math.max(0, avatars.length - max);
    const total = visible.length + (overflow > 0 ? 1 : 0);

    return (
      <div
        ref={ref}
        className={cn('inline-flex items-center', className)}
        {...props}
      >
        {visible.map((item, idx) => (
          <div
            key={`${item.name}-${idx}`}
            className={cn(
              'rounded-full ring-2 ring-[var(--admin-bg-surface)]',
              idx === 0 ? 'ml-0' : spacing,
            )}
            style={{ zIndex: total - idx }}
          >
            <Avatar
              src={item.src}
              alt={item.alt ?? item.name}
              name={item.name}
              size={size}
            />
          </div>
        ))}

        {overflow > 0 ? (
          <div
            className={cn(
              'inline-flex items-center justify-center rounded-full font-semibold leading-none ring-2 ring-[var(--admin-bg-surface)]',
              'bg-[var(--admin-bg-subtle)] text-[var(--admin-fg-muted)]',
              overflowSizeClass[size],
              spacing,
            )}
            style={{ zIndex: 0 }}
            aria-label={`${overflow} more`}
            title={avatars
              .slice(max)
              .map((a) => a.name)
              .join(', ')}
          >
            +{overflow}
          </div>
        ) : null}
      </div>
    );
  },
);
AvatarGroup.displayName = 'AvatarGroup';

export { AvatarGroup, getInitials };
