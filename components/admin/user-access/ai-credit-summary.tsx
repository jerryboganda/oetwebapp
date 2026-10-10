'use client';

import { Fragment } from 'react';
import type {
  AiPackageCreditBucket,
  AiPackageCreditGrantSource,
  AiPackageCreditSnapshot,
} from '@/lib/billing-types';
import { describeCreditsAsActivities } from '@/lib/format-allowance';

const BUCKET_ORDER = ['reading', 'listening', 'writing', 'speaking', 'shared', 'flexible_ws', 'mock'] as const;

function sortBuckets(buckets: AiPackageCreditBucket[]): AiPackageCreditBucket[] {
  return [...buckets].sort(
    (a, b) =>
      BUCKET_ORDER.indexOf(a.key as (typeof BUCKET_ORDER)[number]) -
      BUCKET_ORDER.indexOf(b.key as (typeof BUCKET_ORDER)[number]),
  );
}

// The API clamps daysLeft to 0 once the validity end has passed (a still-open end
// is always >= 1 because it is rounded up), so 0 means "expired", never "today".
function daysLeftLabel(bucket: Pick<AiPackageCreditBucket, 'daysLeft' | 'expiresAt'>): string | null {
  if (bucket.daysLeft < 0 || !bucket.expiresAt) return null;
  if (bucket.daysLeft === 0) return 'expired';
  return `${bucket.daysLeft} day${bucket.daysLeft === 1 ? '' : 's'} left`;
}

function validityLabel(bucket: AiPackageCreditBucket): string | null {
  // Nothing live is left and the credits lapsed: the wallet-level window the API
  // falls back to would contradict the "expired unused" figure.
  if (!bucket.unlimited && bucket.remaining === 0 && (bucket.expired ?? 0) > 0) return 'expired';
  const start = bucket.validFrom ? new Date(bucket.validFrom).toLocaleDateString() : null;
  const end = bucket.expiresAt ? new Date(bucket.expiresAt).toLocaleDateString() : null;
  const left = daysLeftLabel(bucket);
  const parts = [start ? `from ${start}` : null, end ? `to ${end}` : null, left].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : null;
}

const GRANT_STATUS_LABEL: Record<NonNullable<AiPackageCreditGrantSource['status']>, string> = {
  active: 'Active',
  scheduled: 'Scheduled',
  expired: 'Expired',
  reversed: 'Reversed',
};

function grantValidityLabel(
  grant: Pick<AiPackageCreditGrantSource, 'validFrom' | 'expiresAt' | 'daysLeft' | 'status'>,
): string | null {
  const start = grant.validFrom ? new Date(grant.validFrom).toLocaleDateString() : null;
  const end = grant.expiresAt ? new Date(grant.expiresAt).toLocaleDateString() : null;
  const expired = grant.status === 'expired' || (grant.daysLeft === 0 && Boolean(grant.expiresAt));
  // A scheduled grant has not started and a reversed one is refunded, so "N days left" would
  // read as usable time for credits the learner cannot spend.
  const left =
    expired ||
    grant.status === 'scheduled' ||
    grant.status === 'reversed' ||
    grant.daysLeft == null ||
    grant.daysLeft < 0 ||
    !grant.expiresAt
      ? null
      : `${grant.daysLeft} day${grant.daysLeft === 1 ? '' : 's'} left`;
  const parts = [
    start ? `from ${start}` : null,
    end ? `${expired ? 'ended' : 'to'} ${end}` : null,
    left,
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : null;
}

function shortSource(value?: string | null): string | null {
  if (!value) return null;
  if (value.length <= 28) return value;
  return `${value.slice(0, 14)}…${value.slice(-10)}`;
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
    return <p className="text-sm text-admin-fg-muted">Loading credit ledger…</p>;
  }
  if (!snapshot) {
    return <p className="text-sm text-admin-fg-muted">No credit data available.</p>;
  }

  const credits = snapshot;
  const buckets =
    credits.buckets && credits.buckets.length > 0
      ? sortBuckets(credits.buckets)
      : fallbackBuckets(credits);

  return (
    <div className="space-y-3">
      {buckets.length === 0 ? (
        <p className="text-sm text-admin-fg-muted">No active credit buckets.</p>
      ) : (
        <div
          role="region"
          aria-label="AI credit balances"
          tabIndex={0}
          className="overflow-x-auto rounded-lg border border-admin-border focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-admin-primary"
        >
          <table className="w-full min-w-[40rem] text-left text-xs">
            <caption className="sr-only">
              AI credit balances by bucket: total granted, used, remaining, source package and validity.
            </caption>
            <thead className="bg-admin-bg-subtle text-2xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th scope="col" className="px-3 py-2 text-left font-medium">Balance</th>
                <th scope="col" className="px-3 py-2 text-left font-medium">Total</th>
                <th scope="col" className="px-3 py-2 text-left font-medium">Used</th>
                <th scope="col" className="px-3 py-2 text-left font-medium">Remaining</th>
                <th scope="col" className="px-3 py-2 text-left font-medium">Source</th>
                <th scope="col" className="px-3 py-2 text-left font-medium">Validity</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-admin-border">
              {buckets.map((bucket) => (
                <Fragment key={bucket.key}>
                  <tr data-testid={`admin-bucket-${bucket.key}`}>
                    <th scope="row" className="px-3 py-2 text-left font-medium text-admin-fg-strong">{bucket.label}</th>
                    {bucket.unlimited ? (
                      <>
                        <td className="px-3 py-2 text-admin-fg-muted">—</td>
                        <td className="px-3 py-2 text-admin-fg-muted">—</td>
                        <td className="px-3 py-2 font-semibold text-emerald-700 dark:text-emerald-300">Unlimited</td>
                      </>
                    ) : (
                      <>
                        <td className="px-3 py-2 tabular-nums">
                          {bucket.totalGranted + (bucket.expired ?? 0)}
                          {(bucket.expired ?? 0) > 0 ? (
                            <span
                              className="block text-2xs font-normal text-admin-fg-muted"
                              data-testid={`admin-bucket-${bucket.key}-total-expired`}
                            >
                              incl. {bucket.expired} expired
                            </span>
                          ) : null}
                        </td>
                        <td className="px-3 py-2 tabular-nums">{bucket.used}</td>
                        <td className="px-3 py-2 font-semibold tabular-nums">
                          {bucket.remaining}
                          {describeCreditsAsActivities(bucket.key, bucket.remaining) ? (
                            <span className="block text-2xs font-normal text-admin-fg-muted">
                              = {describeCreditsAsActivities(bucket.key, bucket.remaining)}
                            </span>
                          ) : null}
                          {(bucket.expired ?? 0) > 0 ? (
                            <span
                              className="block text-2xs font-normal text-amber-700 dark:text-amber-300"
                              data-testid={`admin-bucket-${bucket.key}-expired`}
                            >
                              {bucket.expired} expired unused
                            </span>
                          ) : null}
                        </td>
                      </>
                    )}
                    <td className="max-w-[16rem] truncate px-3 py-2 text-admin-fg-muted" title={bucket.sourcePackages ?? undefined}>
                      {bucket.sourcePackages ?? '—'}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-admin-fg-muted">{validityLabel(bucket) ?? '—'}</td>
                  </tr>
                  {bucket.grants.length > 0 ? (
                    <tr key={`${bucket.key}-grants`} data-testid={`admin-bucket-${bucket.key}-grants`}>
                      <td colSpan={6} className="bg-admin-bg-subtle/60 px-3 py-2">
                        <div className="space-y-1">
                          <p className="text-2xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                            Grants · {bucket.grants.length}
                          </p>
                          <ul className="space-y-1">
                            {bucket.grants.map((grant, index) => {
                              const status = grant.status ?? 'active';
                              const grantActivities = describeCreditsAsActivities(bucket.key, grant.totalGranted);
                              return (
                              <li
                                key={`${grant.sourceReferenceId ?? grant.packageId ?? grant.description}-${index}`}
                                data-testid={`admin-bucket-${bucket.key}-grant-${index}`}
                                className="flex flex-wrap items-center gap-x-3 gap-y-1 text-2xs leading-4 text-admin-fg-muted"
                              >
                                <span className={`font-medium text-admin-fg-default ${status === 'reversed' ? 'line-through' : ''}`}>
                                  {grant.description || grant.packageId || 'grant'} · {grant.totalGranted}
                                  {grantActivities ? ` (${grantActivities})` : ''}
                                </span>
                                {status !== 'active' ? (
                                  <span
                                    className="rounded-full border border-admin-border px-1.5 text-3xs font-semibold uppercase tracking-wide"
                                    data-testid={`admin-bucket-${bucket.key}-grant-${index}-status`}
                                  >
                                    {GRANT_STATUS_LABEL[status]}
                                  </span>
                                ) : null}
                                {grant.sourceReferenceId ? (
                                  <span
                                    className="max-w-[14rem] truncate font-mono text-3xs text-admin-fg-muted"
                                    title={grant.sourceReferenceId}
                                  >
                                    {shortSource(grant.sourceReferenceId)}
                                  </span>
                                ) : null}
                                <span className="text-admin-fg-muted">{grantValidityLabel(grant) ?? '—'}</span>
                                <span className="text-admin-fg-muted">
                                  granted {new Date(grant.grantedAt).toLocaleDateString()}
                                </span>
                              </li>
                              );
                            })}
                          </ul>
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <p className="text-xs text-admin-fg-muted">
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
