import { cn } from '@/lib/utils';

export type PlatformKey = 'windows' | 'mac' | 'android' | 'ios';

export const PLATFORM_LABELS: Record<PlatformKey, string> = {
  windows: 'Windows',
  mac: 'Mac',
  android: 'Google Play',
  // Short label everywhere, whether the badge currently resolves to the App
  // Store listing or the temporary direct-.ipa route — see IOS_DOWNLOAD_URL.
  ios: 'iOS',
};

export const PLATFORM_ARIA_LABELS: Record<PlatformKey, string> = {
  windows: 'Download the OET app for Windows',
  mac: 'Download the OET app for Mac',
  android: 'Get the OET app on Google Play',
  ios: 'Download the OET app on the App Store',
};

export const PLATFORM_ORDER: PlatformKey[] = ['windows', 'mac', 'android', 'ios'];

export type PlatformGlyphProps = {
  platform: PlatformKey;
  className?: string;
};

export function PlatformGlyph({ platform, className }: PlatformGlyphProps) {
  const commonProps = {
    className: cn('h-8 w-8 shrink-0', className),
    viewBox: '0 0 24 24',
    'aria-hidden': true,
  } as const;

  switch (platform) {
    case 'windows':
      return (
        <svg {...commonProps}>
          <path fill="currentColor" d="M3 4.2 10.2 3v8H3V4.2Zm8.3-1.4L21 1.4V11h-9.7V2.8ZM3 13h7.2v8L3 19.8V13Zm8.3 0H21v9.6l-9.7-1.4V13Z" />
        </svg>
      );
    case 'mac':
      return (
        <svg {...commonProps}>
          <path fill="currentColor" d="M17.05 12.54c-.02-2.23 1.82-3.31 1.9-3.36a4.1 4.1 0 0 0-3.23-1.75c-1.36-.14-2.68.8-3.38.8-.71 0-1.8-.78-2.96-.76a4.36 4.36 0 0 0-3.66 2.23c-1.58 2.74-.4 6.77 1.11 8.98.75 1.08 1.64 2.29 2.81 2.24 1.13-.05 1.56-.72 2.93-.72 1.37 0 1.75.72 2.94.69 1.22-.02 1.99-1.1 2.71-2.19a8.98 8.98 0 0 0 1.23-2.54 3.92 3.92 0 0 1-2.4-3.62ZM14.83 5.98A3.8 3.8 0 0 0 15.7 3a3.9 3.9 0 0 0-2.78 1.44 3.62 3.62 0 0 0-.9 2.87 3.24 3.24 0 0 0 2.81-1.33Z" />
        </svg>
      );
    case 'android':
      return (
        <svg {...commonProps} viewBox="0 0 28 28">
          <path fill="#00d7ff" d="M3 3.2v21.6L15.3 14 3 3.2Z" />
          <path fill="#00f076" d="m3 3.2 15.8 8.9-3.5 1.9L3 3.2Z" />
          <path fill="#ff3d68" d="m3 24.8 12.3-10.9 3.5 1.9L3 24.8Z" />
          <path fill="#ffd400" d="m15.3 14 3.5-1.9 5.1 2.9-5.1 2.8-3.5-1.9Z" />
        </svg>
      );
    case 'ios':
      return (
        <svg {...commonProps}>
          <path d="m8 19 4-14m4 14L12 5m-5.5 9.5h11M12 5l4 7" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.4" />
          <circle cx="6.8" cy="19" r="1.25" fill="currentColor" />
        </svg>
      );
  }
}

export interface PlatformDownloadBadgeProps {
  platform: PlatformKey;
  href: string;
  compact?: boolean;
  className?: string;
}

const badgeBaseClassName = 'inline-flex w-full items-center justify-center rounded-2xl border border-black/40 bg-black text-white shadow-sm transition-[background-color,border-color,transform] duration-200 hover:border-black/60 hover:bg-black/85 active:scale-[0.99] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 motion-reduce:transition-none motion-reduce:active:scale-100';

export function PlatformDownloadBadge({ platform, href, compact = false, className }: PlatformDownloadBadgeProps) {
  const label = PLATFORM_LABELS[platform];
  const ariaLabel = PLATFORM_ARIA_LABELS[platform];
  return (
    <a
      href={href}
      aria-label={ariaLabel}
      className={cn(
        badgeBaseClassName,
        // Full h-20 buttons only from sm: up — every real phone in portrait
        // (360-430px) is below that, so 4 of these stacked (see
        // AppDownloadGrid's 1-column fallback) must stay compact enough to
        // fit a mobile viewport without being clipped by an ancestor.
        compact ? 'h-16 gap-3 px-5' : 'h-14 gap-3 px-4 sm:h-20 sm:gap-4 sm:px-6',
        className
      )}
    >
      <PlatformGlyph
        platform={platform}
        className={cn(compact ? 'h-7 w-7' : 'h-8 w-8 sm:h-9 sm:w-9', 'shrink-0')}
      />
      <span
        className={cn(
          'font-semibold leading-tight text-center sm:leading-none whitespace-nowrap',
          compact ? 'text-sm' : 'text-xs min-[400px]:text-sm sm:text-base',
        )}
      >
        {label}
      </span>
    </a>
  );
}

export type AppDownloadLinks = Record<PlatformKey, string>;

interface AppDownloadGridProps {
  links: AppDownloadLinks;
  compact?: boolean;
  className?: string;
}

export function AppDownloadGrid({ links, compact = false, className }: AppDownloadGridProps) {
  return (
    <div className={cn('grid w-full grid-cols-1 gap-3 min-[520px]:grid-cols-2', className)}>
      {PLATFORM_ORDER.map((platform) => (
        <PlatformDownloadBadge key={platform} platform={platform} href={links[platform]} compact={compact} />
      ))}
    </div>
  );
}
