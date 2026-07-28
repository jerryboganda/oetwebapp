'use client';

import { useState } from 'react';
import { Apple } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * Self-authored recreations of the two familiar app-store badge shapes
 * (black rounded rect, eyebrow + title, leading brandmark) — not traced from
 * any third-party asset. The Google Play triangle below is an original
 * geometric approximation of the four-colour play mark, not a copy of
 * Google's exact vector.
 */
function badgeShellClasses(disabled?: boolean) {
  return cn(
    'inline-flex h-14 items-center gap-3 rounded-xl border border-black/40 bg-black px-4 text-white shadow-sm transition-colors',
    disabled ? 'cursor-default opacity-90' : 'hover:bg-black/85 hover:border-black/60',
  );
}

function GooglePlayGlyph() {
  return (
    <svg viewBox="0 0 24 24" className="h-6 w-6 shrink-0" aria-hidden="true">
      <path d="M5 3L5 12L14.7 9.1Z" fill="#00C2FF" />
      <path d="M14.7 9.1L5 12L19 12Z" fill="#00D67D" />
      <path d="M5 21L5 12L14.7 14.9Z" fill="#FF3D57" />
      <path d="M14.7 14.9L5 12L19 12Z" fill="#FFC400" />
    </svg>
  );
}

interface GooglePlayBadgeProps {
  href: string;
  className?: string;
}

export function GooglePlayBadge({ href, className }: GooglePlayBadgeProps) {
  return (
    <a href={href} className={cn(badgeShellClasses(), className)}>
      <GooglePlayGlyph />
      <span className="flex flex-col leading-none">
        <span className="text-[10px] font-medium uppercase tracking-wider text-white/75">Get it on</span>
        <span className="mt-0.5 text-lg font-semibold leading-tight">Google Play</span>
      </span>
    </a>
  );
}

interface AppStoreBadgeProps {
  className?: string;
  tooltip?: string;
}

export function AppStoreBadge({ className, tooltip = 'Coming soon to the App Store' }: AppStoreBadgeProps) {
  const [showTip, setShowTip] = useState(false);

  return (
    <span className="relative inline-block">
      <button
        type="button"
        aria-disabled="true"
        title={tooltip}
        onClick={(event) => event.preventDefault()}
        onMouseEnter={() => setShowTip(true)}
        onMouseLeave={() => setShowTip(false)}
        onFocus={() => setShowTip(true)}
        onBlur={() => setShowTip(false)}
        className={cn(badgeShellClasses(true), className)}
      >
        <Apple className="h-6 w-6 shrink-0 fill-current" aria-hidden="true" />
        <span className="flex flex-col items-start leading-none">
          <span className="text-[10px] font-medium uppercase tracking-wider text-white/75">Download on the</span>
          <span className="mt-0.5 text-lg font-semibold leading-tight">App Store</span>
        </span>
      </button>
      {showTip ? (
        <span
          role="tooltip"
          className="pointer-events-none absolute -top-9 left-1/2 -translate-x-1/2 whitespace-nowrap rounded-md bg-navy px-2.5 py-1 text-xs font-semibold text-white shadow-clinical"
        >
          {tooltip}
        </span>
      ) : null}
    </span>
  );
}
