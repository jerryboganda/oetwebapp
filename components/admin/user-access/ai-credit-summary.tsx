'use client';

import type { AiPackageCreditBucket, AiPackageCreditSnapshot } from '@/lib/billing-types';

const BUCKET_ORDER = ['reading', 'listening', 'writing', 'speaking', 'shared', 'flexible_ws', 'mock'] as const;

function sortBuckets(buckets: AiPackageCreditBucket[]): AiPackageCreditBucket[] {
  return [...buckets].sort(
    (a, b) =>
      BUCKET_ORDER.indexOf(a.key as (typeof BUCKET_ORDER)[number]) -
      BUCKET_ORDER.indexOf(b.key as (typeof BUCKET_ORDER)[number]),
  );
}

function daysLeftLabel(bucket: Pick<AiPackageCreditBucket, 'daysLeft' | 'expiresAt'>): string | null {
  if (bucket.daysLeft < 0 || !bucket.expiresAt) return null;
  if (bucket.daysLeft === 0) return 'expires today';
  return `${bucket.daysLeft} day${bucket.daysLeft === 1 ? '' : 's'} left`;
}

function validityLabel(bucket: AiPackageCreditBucket): string | null {
  const start = bucket.validFrom ? new Date(bucket.validFrom).toLocaleDateString() : null;
  const end = bucket.expiresAt ? new Date(bucket.expiresAt).toLocaleDateString() : null;
  const left = daysLeftLabel(bucket);
  const parts = [start ? `from ${start}` : null, end ? `to ${end}` : null, left].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : null;
}

/**
 * Admin > User Management > Candidate Profile credit ledger (Master Catalogue
 * §7). Reads the SAME authoritative package wallet the candidate sees and
 * shows Credits / Attempts / Unlimited only — raw provider tokens are never
 * mixed into a per-user candidate profile.
 */
export function AiCreditSummary({
  snapshot,
  loading = false,
}: {
  snapshot: AiPackageCreditSnapshot | null;
  loading?: boolean;
}) {
  if (loading) {
    return <p className="text-sm text-gray-400">Loading credit ledger…</p>;
  }
  if (!snapshot) {
    return <p className="text-sm text-gray-500">No credit data available.</p>;
  }

  const credits = snapshot;
  const buckets =
    credits.buckets && credits.buckets.length > 0
      ? sortBuckets(credits.buckets)
      : fallbackBuckets(credits);

  return (
    <div className="space-y-3">
      {buckets.length === 0 ? (
        <p className="text-sm text-gray-500">No active credit buckets.</p>
      ) : (
        <div className="overflow-hidden rounded-lg border border-gray-200">
          <table className="w-full text-left text-xs">
            <thead className="bg-gray-50 text-[11px] uppercase tracking-wide text-gray-500">
              <tr>
                <th className="px-3 py-2 font-medium">Balance</th>
                <th className="px-3 py-2 font-medium">Total</th>
                <th className="px-3 py-2 font-medium">Used</th>
                <th className="px-3 py-2 font-medium">Remaining</th>
                <th className="px-3 py-2 font-medium">Source</th>
                <th className="px-3 py-2 font-medium">Validity</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {buckets.map((bucket) => (
                <tr key={bucket.key} data-testid={`admin-bucket-${bucket.key}`}>
                  <td className="px-3 py-2 font-medium text-gray-800">{bucket.label}</td>
                  {bucket.unlimited ? (
                    <>
                      <td className="px-3 py-2 text-gray-400">—</td>
                      <td className="px-3 py-2 text-gray-400">—</td>
                      <td className="px-3 py-2 font-semibold text-emerald-700">Unlimited</td>
                    </>
                  ) : (
                    <>
                      <td className="px-3 py-2 tabular-nums">{bucket.totalGranted}</td>
                      <td className="px-3 py-2 tabular-nums">{bucket.used}</td>
                      <td className="px-3 py-2 font-semibold tabular-nums">{bucket.remaining}</td>
                    </>
                  )}
                  <td className="max-w-[16rem] truncate px-3 py-2 text-gray-500" title={bucket.sourcePackages ?? undefined}>
                    {bucket.sourcePackages ?? '—'}
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 text-gray-500">{validityLabel(bucket) ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <p className="text-xs text-gray-400">
        Wallet expiry:{' '}
        {credits.expiresAt ? new Date(credits.expiresAt).toLocaleString() : 'no expiry'}
        {credits.expiredBecausePassed ? ' · expired because candidate passed OET' : ''}
      </p>
    </div>
  );
}

/** Pre-enrichment API fallback: project flat fields into the same shape. */
function fallbackBuckets(credits: AiPackageCreditSnapshot): AiPackageCreditBucket[] {
  const rows: AiPackageCreditBucket[] = [];
  if (credits.readingTestsRemaining !== 0 || credits.buckets === null) {
    rows.push({
      key: 'reading',
      label: 'Reading Credits',
      unlimited: credits.readingTestsRemaining === null,
      totalGranted: credits.readingTestsRemaining ?? 0,
      used: 0,
      remaining: credits.readingTestsRemaining ?? 0,
      sourcePackages: null,
      validFrom: null,
      expiresAt: credits.expiresAt ?? null,
      daysLeft: -1,
      grants: [],
    });
  }
  if (credits.listeningTestsRemaining !== 0 || credits.buckets === null) {
    rows.push({
      key: 'listening',
      label: 'Listening Credits',
      unlimited: credits.listeningTestsRemaining === null,
      totalGranted: credits.listeningTestsRemaining ?? 0,
      used: 0,
      remaining: credits.listeningTestsRemaining ?? 0,
      sourcePackages: null,
      validFrom: null,
      expiresAt: credits.expiresAt ?? null,
      daysLeft: -1,
      grants: [],
    });
  }
  rows.push({
    key: 'writing',
    label: 'Writing Credits',
    unlimited: credits.writingUnlimited === true,
    totalGranted: credits.writingOnlyCredits,
    used: 0,
    remaining: credits.writingUnlimited ? 0 : credits.writingOnlyCredits,
    sourcePackages: null,
    validFrom: null,
    expiresAt: credits.expiresAt ?? null,
    daysLeft: -1,
    grants: [],
  });
  rows.push({
    key: 'speaking',
    label: 'Speaking Credits',
    unlimited: credits.speakingUnlimited === true,
    totalGranted: credits.speakingOnlyCredits,
    used: 0,
    remaining: credits.speakingUnlimited ? 0 : credits.speakingOnlyCredits,
    sourcePackages: null,
    validFrom: null,
    expiresAt: credits.expiresAt ?? null,
    daysLeft: -1,
    grants: [],
  });
  rows.push({
    key: 'shared',
    label: 'Shared Credits',
    unlimited: false,
    totalGranted: credits.sharedCredits ?? 0,
    used: 0,
    remaining: credits.sharedCredits ?? 0,
    sourcePackages: null,
    validFrom: null,
    expiresAt: credits.expiresAt ?? null,
    daysLeft: -1,
    grants: [],
  });
  if (credits.flexibleCredits > 0) {
    rows.push({
      key: 'flexible_ws',
      label: 'Flexible W/S Credits',
      unlimited: false,
      totalGranted: credits.flexibleCredits,
      used: 0,
      remaining: credits.flexibleCredits,
      sourcePackages: null,
      validFrom: null,
      expiresAt: credits.expiresAt ?? null,
      daysLeft: -1,
      grants: [],
    });
  }
  if (credits.mockExamsRemaining > 0) {
    rows.push({
      key: 'mock',
      label: 'Full Mock Attempts',
      unlimited: false,
      totalGranted: credits.mockExamsRemaining,
      used: 0,
      remaining: credits.mockExamsRemaining,
      sourcePackages: null,
      validFrom: null,
      expiresAt: credits.expiresAt ?? null,
      daysLeft: -1,
      grants: [],
    });
  }
  return rows;
}
