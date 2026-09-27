/**
 * "Is the signed-in admin the platform owner?" — drives the owner-only nav
 * entry (Agent Console) in app/admin/layout.tsx.
 *
 * - Asked once per page session and user (GET /v1/owner-agent/me, which returns
 *   `{ isOwner:false }` with 200 for non-owners). The answer is cached in module
 *   memory only — never in web storage.
 * - Only system_admin accounts ask; everyone else is not the owner by
 *   definition (the API policy requires system_admin too).
 * - Fail-closed: any error (feature off, network, 403) hides the entry and is
 *   not cached, so a later mount can retry.
 * - This is presentation only. The API enforces the owner allow-list on every
 *   /v1/owner-agent route regardless of what the nav shows.
 */

import { useEffect, useState } from 'react';
import { getMe } from './api';

const resolved = new Map<string, boolean>();
const inflight = new Map<string, Promise<boolean>>();

export function fetchIsOwnerAgentOwner(userId: string): Promise<boolean> {
  const known = resolved.get(userId);
  if (known !== undefined) return Promise.resolve(known);
  const pending = inflight.get(userId);
  if (pending) return pending;

  const request = getMe()
    .then((me) => {
      const isOwner = Boolean(me && me.isOwner === true);
      resolved.set(userId, isOwner);
      return isOwner;
    })
    .catch(() => false)
    .finally(() => {
      inflight.delete(userId);
    });
  inflight.set(userId, request);
  return request;
}

/** Owner flag for nav rendering; false until confirmed. */
export function useIsOwnerAgentOwner(userId: string | null | undefined, eligible: boolean): boolean {
  const key = eligible && userId ? userId : null;
  const [state, setState] = useState<{ key: string | null; isOwner: boolean }>(() => ({
    key,
    isOwner: key ? resolved.get(key) === true : false,
  }));

  useEffect(() => {
    if (!key) return;
    let cancelled = false;
    void fetchIsOwnerAgentOwner(key).then((isOwner) => {
      if (!cancelled) setState({ key, isOwner });
    });
    return () => {
      cancelled = true;
    };
  }, [key]);

  if (!key) return false;
  if (state.key === key) return state.isOwner;
  return resolved.get(key) === true;
}

/** Drop `ownerOnly` items unless the viewer is the owner. */
export function filterOwnerOnlyNavItems<T extends { ownerOnly?: boolean }>(items: readonly T[], isOwner: boolean): T[] {
  return isOwner ? items.slice() : items.filter((item) => !item.ownerOnly);
}

/** Test hook. */
export function resetOwnerFlagCacheForTests(): void {
  resolved.clear();
  inflight.clear();
}
