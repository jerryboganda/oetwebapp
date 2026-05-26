'use client';

/**
 * AccentBadge — tiny pill component displaying an accent flag + label.
 *
 * Consumed by:
 *   - app/listening/diagnostic — per-question accent indicator (§6.2)
 *   - app/listening/results — accent column header (§6.4)
 *   - app/listening/dashboard — accent-progress chart legend
 *
 * Accepts both BCP-47-style codes (`en-GB`, `en-AU`, `en-US`, `en-XX`) and the
 * backend `AccentProgress.accent` codes (`british`, `australian`, `us`,
 * `non_native`). Unknown codes fall through to a neutral "🎧 {code}" pill.
 */

import type { CSSProperties } from 'react';

export interface AccentBadgeProps {
  /**
   * Accent code. Supports BCP-47 (`en-GB`, `en-AU`, `en-US`, `en-XX`) and
   * snake_case backend codes (`british`, `australian`, `us`, `non_native`).
   */
  accent: 'en-GB' | 'en-AU' | 'en-US' | 'en-XX' | string;
  size?: 'sm' | 'md';
  className?: string;
  style?: CSSProperties;
}

interface AccentDisplay {
  flag: string;
  label: string;
}

function mapAccent(accent: string): AccentDisplay {
  switch (accent) {
    case 'en-GB':
    case 'british':
      return { flag: '🇬🇧', label: 'British' };
    case 'en-AU':
    case 'australian':
      return { flag: '🇦🇺', label: 'Australian' };
    case 'en-US':
    case 'us':
      return { flag: '🇺🇸', label: 'North American' };
    case 'en-XX':
    case 'non_native':
      return { flag: '🌍', label: 'Non-native' };
    default:
      return { flag: '🎧', label: accent };
  }
}

export function AccentBadge({ accent, size = 'md', className, style }: AccentBadgeProps) {
  const { flag, label } = mapAccent(accent);
  const sizeClasses =
    size === 'sm'
      ? 'px-2 py-0.5 text-[10px] gap-1'
      : 'px-2.5 py-0.5 text-xs gap-1.5';

  return (
    <span
      className={[
        'inline-flex items-center rounded-full font-medium',
        'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-200',
        sizeClasses,
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
      style={style}
      aria-label={`Accent: ${label}`}
    >
      <span aria-hidden="true">{flag}</span>
      <span>{label}</span>
    </span>
  );
}

export default AccentBadge;
