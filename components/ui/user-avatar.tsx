'use client';

import { useEffect, useState } from 'react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { cn } from '@/lib/utils';

function initialsFor(displayName: string | null | undefined): string {
  const trimmed = (displayName ?? '').trim();
  if (!trimmed) return '?';
  return trimmed
    .split(' ')
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();
}

export interface UserAvatarProps {
  /** Relative `/v1/media/{id}/content` path, or null/undefined for the initials fallback. */
  avatarUrl?: string | null;
  displayName: string | null | undefined;
  className?: string;
}

/**
 * Renders a learner/tutor avatar. `avatarUrl` is bearer-authenticated, so a
 * native <img src> would 401 — this fetches it as a blob and swaps in the
 * object URL, falling back to initials while loading, when absent, or on
 * fetch/decode failure. The object URL is revoked on unmount/change.
 */
export function UserAvatar({ avatarUrl, displayName, className }: UserAvatarProps) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
    // Drop the previous object URL from state as well as revoking it below —
    // otherwise a changed avatarUrl keeps rendering the old (now revoked) blob
    // until the new fetch resolves, which flashes a broken image.
    setObjectUrl(null);
    if (!avatarUrl) {
      return;
    }

    let cancelled = false;
    let createdUrl: string | null = null;
    fetchAuthorizedObjectUrl(avatarUrl)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        createdUrl = url;
        setObjectUrl(url);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (createdUrl) URL.revokeObjectURL(createdUrl);
    };
  }, [avatarUrl]);

  if (objectUrl && !failed) {
    return (
      // eslint-disable-next-line @next/next/no-img-element -- object URL, not a directly-loadable API src
      <img
        src={objectUrl}
        alt={displayName ? `${displayName}'s avatar` : 'User avatar'}
        onError={() => setFailed(true)}
        className={cn('h-9 w-9 shrink-0 rounded-full object-cover', className)}
      />
    );
  }

  return (
    <span
      className={cn(
        'flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-[12px] font-bold text-primary ring-1 ring-primary/10',
        className,
      )}
    >
      {initialsFor(displayName)}
    </span>
  );
}
