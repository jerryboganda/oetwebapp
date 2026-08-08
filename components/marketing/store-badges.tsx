'use client';

import { useState } from 'react';
import { Apple, MonitorDown } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * Self-authored recreations of the two familiar app-store badge shapes
 * (black rounded rect, eyebrow + title, leading brandmark) — not traced from
 * any third-party asset. The Google Play triangle below is an original
 * geometric approximation of the four-colour play mark, not a copy of
 * Google's exact vector.
 */
function badgeShellClasses(disabled?: boolean, compact = false) {
  return cn(
    'inline-flex items-center rounded-xl border border-black/40 bg-black text-white shadow-sm transition-colors',
    compact ? 'h-12 gap-2 px-3' : 'h-14 gap-3 px-4',
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
  compact?: boolean;
}

export function GooglePlayBadge({ href, className, compact = false }: GooglePlayBadgeProps) {
  return (
    <a
      href={href}
      aria-label="Get the OET app on Google Play"
      className={cn(badgeShellClasses(false, compact), className)}
    >
      <span className={compact ? '[&>svg]:h-5 [&>svg]:w-5' : undefined}>
        <GooglePlayGlyph />
      </span>
      <span className="flex flex-col leading-none">
        <span className={cn('font-medium uppercase tracking-wider text-white/75', compact ? 'text-[8px]' : 'text-[10px]')}>Get it on</span>
        <span className={cn('font-semibold leading-tight', compact ? 'text-sm' : 'mt-0.5 text-lg')}>Google Play</span>
      </span>
    </a>
  );
}

interface AppStoreBadgeProps {
  className?: string;
  compact?: boolean;
  href?: string | null;
  tooltip?: string;
  directDownload?: boolean;
}

export function AppStoreBadge({
  className,
  compact = false,
  href,
  tooltip = 'The temporary iOS download is not published yet',
  directDownload = false,
}: AppStoreBadgeProps) {
  const [showTip, setShowTip] = useState(false);
  const badgeClassName = cn(badgeShellClasses(!href, compact), className);
  const accessibleLabel = directDownload
    ? 'Download the OET iOS app for iPhone and iPad'
    : 'Download the OET app for iPhone and iPad';
  const updateTooltip = (visible: boolean) => {
    if (!href) setShowTip(visible);
  };
  const badgeContent = (
    <>
      <Apple className={cn('shrink-0 fill-current', compact ? 'h-5 w-5' : 'h-6 w-6')} aria-hidden="true" />
      <span className="flex flex-col items-start leading-none">
        <span className={cn('font-medium uppercase tracking-wider text-white/75', compact ? 'text-[8px]' : 'text-[10px]')}>
          {directDownload ? 'Download directly' : 'Download on the'}
        </span>
        <span className={cn('font-semibold leading-tight', compact ? 'text-sm' : 'mt-0.5 text-lg')}>
          {directDownload ? 'iOS app' : 'App Store'}
        </span>
      </span>
    </>
  );

  return (
    <span className="relative inline-block">
      {href ? (
        <a
          href={href}
          aria-label={accessibleLabel}
          onMouseEnter={() => updateTooltip(true)}
          onMouseLeave={() => updateTooltip(false)}
          onFocus={() => updateTooltip(true)}
          onBlur={() => updateTooltip(false)}
          className={badgeClassName}
        >
          {badgeContent}
        </a>
      ) : (
        <button
          type="button"
          aria-disabled="true"
          title={tooltip}
          onClick={(event) => event.preventDefault()}
          onMouseEnter={() => updateTooltip(true)}
          onMouseLeave={() => updateTooltip(false)}
          onFocus={() => updateTooltip(true)}
          onBlur={() => updateTooltip(false)}
          className={badgeClassName}
        >
          {badgeContent}
        </button>
      )}
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

interface DesktopAppBadgeProps {
  href: string;
  label?: string;
  className?: string;
  compact?: boolean;
}

export function DesktopAppBadge({ href, label = 'Windows & Mac', className, compact = false }: DesktopAppBadgeProps) {
  return (
    <a
      href={href}
      aria-label={`Download the OET app for ${label}`}
      className={cn(badgeShellClasses(false, compact), className)}
    >
      <MonitorDown className={cn('shrink-0', compact ? 'h-5 w-5' : 'h-6 w-6')} aria-hidden="true" />
      <span className="flex flex-col items-start leading-none">
        <span className={cn('font-medium uppercase tracking-wider text-white/75', compact ? 'text-[8px]' : 'text-[10px]')}>Download for</span>
        <span className={cn('font-semibold leading-tight', compact ? 'text-sm' : 'mt-0.5 text-lg')}>{label}</span>
      </span>
    </a>
  );
}
