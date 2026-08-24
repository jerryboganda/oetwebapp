'use client';

import { useMemo } from 'react';
import type { AiPackageCreditBucket, AiPackageCreditSnapshot } from '@/lib/billing-types';
import { formatValidityWindow } from '@/lib/format-allowance';

const PRIMARY_ORDER = ['reading', 'listening', 'writing', 'speaking', 'shared'] as const;

function bucketSortKey(bucket: AiPackageCreditBucket): number {
  const primaryIndex = PRIMARY_ORDER.indexOf(bucket.key as (typeof PRIMARY_ORDER)[number]);
  if (primaryIndex >= 0) return primaryIndex;
  return bucket.key === 'flexible_ws' ? 5 : 6;
}

export function hasVisibleCreditActivity(snapshot: AiPackageCreditSnapshot): boolean {
  return (
    snapshot.readingTestsRemaining !== 0 ||
    snapshot.listeningTestsRemaining !== 0 ||
    snapshot.writingOnlyCredits > 0 ||
    snapshot.speakingOnlyCredits > 0 ||
    snapshot.flexibleCredits > 0 ||
    (snapshot.sharedCredits ?? 0) > 0 ||
    snapshot.mockExamsRemaining > 0 ||
    Boolean(snapshot.buckets && snapshot.buckets.length > 0)
  );
}

/**
 * Master Catalogue dashboard card: Reading / Listening / Writing / Speaking /
 * Shared credits plus conditional Flexible W/S and Full Mock attempts.
 * Each row shows Total / Used / Remaining (or Unlimited), source package,
 * validity window with days left. Credits/Attempts/Unlimited only — never
 * raw provider tokens.
 */
export function CreditBalanceCard({ snapshot }: { snapshot: AiPackageCreditSnapshot }) {
  const buckets = useMemo(() => {
    const source = snapshot.buckets && snapshot.buckets.length > 0 ? snapshot.buckets : null;
    if (!source) {
      // Fallback for API payloads predating the enriched snapshot.
      const legacy: AiPackageCreditBucket[] = [
        {
          key: 'reading',
          label: 'Reading Credits',
          unlimited: snapshot.readingTestsRemaining === null,
          totalGranted: snapshot.readingTestsRemaining ?? 0,
          used: 0,
          remaining: snapshot.readingTestsRemaining ?? 0,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        },
        {
          key: 'listening',
          label: 'Listening Credits',
          unlimited: snapshot.listeningTestsRemaining === null,
          totalGranted: snapshot.listeningTestsRemaining ?? 0,
          used: 0,
          remaining: snapshot.listeningTestsRemaining ?? 0,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        },
        {
          key: 'writing',
          label: 'Writing Credits',
          unlimited: snapshot.writingUnlimited === true,
          totalGranted: snapshot.writingOnlyCredits,
          used: 0,
          remaining: snapshot.writingUnlimited ? 0 : snapshot.writingOnlyCredits,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        },
        {
          key: 'speaking',
          label: 'Speaking Credits',
          unlimited: snapshot.speakingUnlimited === true,
          totalGranted: snapshot.speakingOnlyCredits,
          used: 0,
          remaining: snapshot.speakingUnlimited ? 0 : snapshot.speakingOnlyCredits,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        },
        {
          key: 'shared',
          label: 'Shared Credits',
          unlimited: false,
          totalGranted: snapshot.sharedCredits ?? 0,
          used: 0,
          remaining: snapshot.sharedCredits ?? 0,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        },
      ];
      if (snapshot.flexibleCredits > 0) {
        legacy.push({
          key: 'flexible_ws',
          label: 'Flexible W/S Credits',
          unlimited: false,
          totalGranted: snapshot.flexibleCredits,
          used: 0,
          remaining: snapshot.flexibleCredits,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        });
      }
      if (snapshot.mockExamsRemaining > 0) {
        legacy.push({
          key: 'mock',
          label: 'Full Mock Attempts',
          unlimited: false,
          totalGranted: snapshot.mockExamsRemaining,
          used: 0,
          remaining: snapshot.mockExamsRemaining,
          sourcePackages: null,
          validFrom: null,
          expiresAt: snapshot.expiresAt ?? null,
          daysLeft: -1,
          grants: [],
        });
      }
      return legacy;
    }
    return [...source].sort((a, b) => bucketSortKey(a) - bucketSortKey(b));
  }, [snapshot]);

  const expired = snapshot.expiredBecausePassed;

  return (
    <section aria-label="AI credits" className="rounded-2xl border border-black/10 bg-white p-4 shadow-sm">
      <header className="mb-3 flex items-baseline justify-between gap-2">
        <h2 className="text-base font-semibold text-gray-900">AI Credits</h2>
        {expired ? (
          <span className="rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
            Expired — passed OET
          </span>
        ) : (
          snapshot.expiresAt && (
            <span className="text-xs text-gray-500">{formatValidityWindow(null, snapshot.expiresAt)}</span>
          )
        )}
      </header>

      {buckets.length === 0 ? (
        <p className="text-sm text-gray-500">No active AI credit balances.</p>
      ) : (
        <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {buckets.map((bucket) => (
            <li
              key={bucket.key}
              className="rounded-xl border border-black/5 bg-gray-50/60 px-3 py-2"
              data-testid={`credit-bucket-${bucket.key}`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-gray-800">{bucket.label}</span>
                {bucket.unlimited ? (
                  <span className="text-sm font-semibold text-emerald-700">Unlimited</span>
                ) : (
                  <span className="text-sm font-semibold text-gray-900 tabular-nums">
                    {bucket.remaining} <span className="font-normal text-gray-500">remaining</span>
                  </span>
                )}
              </div>
              {!bucket.unlimited && (bucket.totalGranted > 0 || bucket.used > 0) && (
                <p className="mt-0.5 text-xs text-gray-500 tabular-nums">
                  Total {bucket.totalGranted} · Used {bucket.used}
                </p>
              )}
              {(bucket.sourcePackages || bucket.validFrom || bucket.daysLeft >= 0) && (
                <p className="mt-0.5 truncate text-xs text-gray-400" title={bucket.sourcePackages ?? undefined}>
                  {bucket.sourcePackages ? `${bucket.sourcePackages} · ` : ''}
                  {formatValidityWindow(bucket.validFrom, bucket.expiresAt)}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
