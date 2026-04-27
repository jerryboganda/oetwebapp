'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import { professions as fallbackProfessions } from '@/lib/auth/enrollment';
import type { SignupProfession } from '@/lib/types/auth';

/**
 * Centralised profession list, sourced from the backend signup catalog
 * (`/v1/auth/catalog/signup`). The list is managed by admins via
 * `/admin/taxonomy` (Profession Taxonomy) and is the single source of
 * truth for every profession dropdown in the app.
 *
 * Falls back to the static enrollment list if the API is unreachable so
 * SSR/offline still render something.
 */
export function useProfessions(): {
  professions: SignupProfession[];
  isLoading: boolean;
  options: { value: string; label: string }[];
} {
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const catalog = await fetchSignupCatalog();
        if (cancelled) return;
        if (Array.isArray(catalog.professions) && catalog.professions.length > 0) {
          setProfessions(catalog.professions);
        }
      } catch {
        // keep fallback
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const options = professions.map((p) => ({ value: p.id, label: p.label }));

  return { professions, isLoading, options };
}
