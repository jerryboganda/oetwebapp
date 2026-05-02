/**
 * Fixed target-country list for sign-up.
 *
 * Per PRD Phase 2, registration must no longer derive target countries from
 * profession/session catalog data. Keep this list aligned with the backend
 * `TargetCountryOptions` allowlist.
 */
export const TARGET_COUNTRY_OPTIONS = [
  'United Kingdom',
  'Ireland',
  'Scotland',
  'USA',
  'Australia',
  'New Zealand',
  'Canada',
  'Gulf Countries',
  'Other Countries',
] as const;

export type TargetCountry = (typeof TARGET_COUNTRY_OPTIONS)[number];

export function isTargetCountry(value: string): value is TargetCountry {
  return (TARGET_COUNTRY_OPTIONS as readonly string[]).includes(value);
}