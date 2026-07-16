/**
 * Canonical profession taxonomy client.
 *
 * The backend `SignupProfessionCatalog` table is the single source of truth
 * (spec §3). `GET /v1/professions/catalog` is anonymous — every surface that
 * offers or labels a profession reads it here instead of hardcoding a list.
 * The static fallback in `lib/auth/enrollment.ts` covers SSR and offline.
 */

import { PROFESSION_CATALOG } from '@/lib/auth/enrollment';
import { env } from '@/lib/env';
import { fetchWithTimeout } from '@/lib/network/fetch-with-timeout';

export interface ProfessionCatalogEntry {
  id: string;
  label: string;
  description: string;
  /** Archived entries are returned so old learners' professions still render; never offer them as a choice. */
  isActive: boolean;
}

interface ProfessionCatalogResponse {
  professions: ProfessionCatalogEntry[];
}

export const PROFESSION_CATALOG_FALLBACK: ProfessionCatalogEntry[] = PROFESSION_CATALOG.map(
  ({ id, label, description, isActive }) => ({ id, label, description, isActive }),
);

const CACHE_TTL_MS = 5 * 60 * 1000;

let cached: { entries: ProfessionCatalogEntry[]; at: number } | null = null;
let inFlight: Promise<ProfessionCatalogEntry[]> | null = null;

function isEntry(value: unknown): value is ProfessionCatalogEntry {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<ProfessionCatalogEntry>;
  return typeof item.id === 'string' && item.id.length > 0 && typeof item.label === 'string';
}

async function loadCatalog(signal?: AbortSignal): Promise<ProfessionCatalogEntry[]> {
  const response = await fetchWithTimeout(
    `${env.apiBaseUrl}/v1/professions/catalog`,
    { headers: { Accept: 'application/json' }, signal },
    10_000,
  );
  if (!response.ok) throw new Error(`profession catalog ${response.status}`);

  const payload = (await response.json()) as ProfessionCatalogResponse;
  const entries = Array.isArray(payload?.professions) ? payload.professions.filter(isEntry) : [];
  if (entries.length === 0) throw new Error('profession catalog empty');

  return entries.map((item) => ({
    id: item.id,
    label: item.label,
    description: item.description ?? '',
    isActive: item.isActive !== false,
  }));
}

/**
 * Canonical catalog, 5-minute cached. Never throws: an unreachable API falls
 * back to the static list so a dropdown always renders something selectable.
 */
export async function fetchProfessionCatalog(options?: {
  signal?: AbortSignal;
  force?: boolean;
}): Promise<ProfessionCatalogEntry[]> {
  if (!options?.force && cached && Date.now() - cached.at < CACHE_TTL_MS) {
    return cached.entries;
  }

  if (!inFlight) {
    inFlight = loadCatalog(options?.signal)
      .then((entries) => {
        cached = { entries, at: Date.now() };
        return entries;
      })
      .catch(() => cached?.entries ?? PROFESSION_CATALOG_FALLBACK)
      .finally(() => {
        inFlight = null;
      });
  }

  return inFlight;
}

/** Selectable options for a dropdown — archived entries are filtered out. */
export function professionCatalogOptions(
  entries: ProfessionCatalogEntry[],
): { value: string; label: string }[] {
  return entries.filter((item) => item.isActive).map((item) => ({ value: item.id, label: item.label }));
}
