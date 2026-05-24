'use client';

import * as React from 'react';
import * as AvatarPrimitive from '@radix-ui/react-avatar';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

/* ─────────────────────────────────────────────────────────────────────
 * Sizing
 * ───────────────────────────────────────────────────────────────────── */

const avatarVariants = cva(
  'relative inline-flex shrink-0 overflow-hidden rounded-full select-none',
  {
    variants: {
      size: {
        xs: 'h-6 w-6 text-[10px]',
        sm: 'h-8 w-8 text-xs',
        md: 'h-10 w-10 text-sm',
        lg: 'h-12 w-12 text-base',
        xl: 'h-16 w-16 text-lg',
        '2xl': 'h-24 w-24 text-2xl',
      },
    },
    defaultVariants: { size: 'md' },
  },
);

export type AvatarSize = NonNullable<VariantProps<typeof avatarVariants>['size']>;

/* ─────────────────────────────────────────────────────────────────────
 * Status indicator
 * ───────────────────────────────────────────────────────────────────── */

export type AvatarStatus = 'online' | 'offline' | 'busy' | 'away';

const statusClass: Record<AvatarStatus, string> = {
  online: 'bg-[var(--admin-success)]',
  offline: 'bg-[var(--admin-secondary)]',
  busy: 'bg-[var(--admin-danger)]',
  away: 'bg-[var(--admin-warning)]',
};

const statusDotSize: Record<AvatarSize, string> = {
  xs: 'h-1.5 w-1.5',
  sm: 'h-2 w-2',
  md: 'h-2.5 w-2.5',
  lg: 'h-3 w-3',
  xl: 'h-3.5 w-3.5',
  '2xl': 'h-4 w-4',
};

/* ─────────────────────────────────────────────────────────────────────
 * Deterministic role color for fallback (hash → 6 brand roles)
 * ───────────────────────────────────────────────────────────────────── */

const FALLBACK_ROLES = ['primary', 'secondary', 'success', 'warning', 'danger', 'info'] as const;
type FallbackRole = (typeof FALLBACK_ROLES)[number];

function hashString(input: string): number {
  let h = 0;
  for (let i = 0; i < input.length; i += 1) {
    h = (h << 5) - h + input.charCodeAt(i);
    h |= 0;
  }
  return Math.abs(h);
}

function roleForName(name: string): FallbackRole {
  if (!name) return 'primary';
  return FALLBACK_ROLES[hashString(name) % FALLBACK_ROLES.length];
}

const fallbackBgClass: Record<FallbackRole, string> = {
  primary: 'bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]',
  secondary: 'bg-[var(--admin-secondary-tint)] text-[var(--admin-secondary)]',
  success: 'bg-[var(--admin-success-tint)] text-[var(--admin-success)]',
  warning: 'bg-[var(--admin-warning-tint)] text-[var(--admin-warning)]',
  danger: 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]',
  info: 'bg-[var(--admin-info-tint)] text-[var(--admin-info)]',
};

/** Derive initials from a display name — first letters of the first two words, uppercased. */
export function getInitials(name: string): string {
  if (!name) return '';
  const parts = name.trim().split(/\s+/).slice(0, 2);
  return parts.map((p) => p.charAt(0).toUpperCase()).join('');
}

/* ─────────────────────────────────────────────────────────────────────
 * Component
 * ───────────────────────────────────────────────────────────────────── */

export interface AvatarProps
  extends Omit<React.ComponentPropsWithoutRef<typeof AvatarPrimitive.Root>, 'children'>,
    VariantProps<typeof avatarVariants> {
  /** Image source URL */
  src?: string;
  /** Required for accessibility */
  alt: string;
  /** Display name — used to derive initials and deterministic fallback color */
  name?: string;
  /** Optional presence indicator dot, rendered bottom-right */
  status?: AvatarStatus;
}

const Avatar = React.forwardRef<React.ElementRef<typeof AvatarPrimitive.Root>, AvatarProps>(
  ({ className, size, src, alt, name = '', status, ...props }, ref) => {
    const role = roleForName(name);
    const initials = getInitials(name);

    return (
      <span className="relative inline-flex shrink-0">
        <AvatarPrimitive.Root
          ref={ref}
          className={cn(avatarVariants({ size }), className)}
          {...props}
        >
          {src ? (
            <AvatarPrimitive.Image
              src={src}
              alt={alt}
              className="aspect-square h-full w-full object-cover"
            />
          ) : null}
          <AvatarPrimitive.Fallback
            className={cn(
              'flex h-full w-full items-center justify-center font-semibold leading-none',
              fallbackBgClass[role],
            )}
            delayMs={src ? 200 : 0}
          >
            {initials || (
              <span aria-hidden className="opacity-60">
                ?
              </span>
            )}
          </AvatarPrimitive.Fallback>
        </AvatarPrimitive.Root>

        {status ? (
          <span
            aria-label={status}
            className={cn(
              'absolute bottom-0 right-0 block rounded-full ring-2 ring-[var(--admin-bg-surface)]',
              statusClass[status],
              statusDotSize[(size ?? 'md') as AvatarSize],
            )}
          />
        ) : null}
      </span>
    );
  },
);
Avatar.displayName = 'Avatar';

export { Avatar, avatarVariants };
