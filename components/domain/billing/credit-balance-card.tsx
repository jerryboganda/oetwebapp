'use client';

import { useMemo } from 'react';
import type {
  AiPackageCreditBucket,
  AiPackageCreditGrantSource,
  AiPackageCreditGrantStatus,
  AiPackageCreditSnapshot,
} from '@/lib/billing-types';
import {
  CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY,
  describeCreditsAsActivities,
  formatValidityWindow,
} from '@/lib/format-allowance';

const PRIMARY_ORDER = ['reading', 'listening', 'writing', 'speaking', 'shared'] as const;

// Whole letters/cards the learner can start across ALL pools (server-computed,
// greedy dedicated -> Flexible W/S -> Shared). A bucket row alone cannot say this,
// because Flexible and Shared credits also fund a letter. Those two pools feed BOTH
// counts, so when either holds credits the counts are alternatives ("or"), never a
// sum: 10 Flexible credits is 5 letters OR 5 cards, not 5 of each.
function startableActivitiesLabel(snapshot: AiPackageCreditSnapshot): string | null {
  const parts: string[] = [];
  const writing = snapshot.availableWritingActivities;
  if (!snapshot.writingUnlimited && writing != null && Number.isFinite(writing) && writing < 1_000_000) {
    parts.push(`${writing} Writing letter${writing === 1 ? '' : 's'}`);
  }
  const speaking = snapshot.availableSpeakingActivities;
  if (!snapshot.speakingUnlimited && speaking != null && Number.isFinite(speaking) && speaking < 1_000_000) {
    parts.push(`${speaking} Speaking card${speaking === 1 ? '' : 's'}`);
  }
  // Nothing startable: the per-bucket rows already show the zero balances, and a
  // Reading/Listening-only buyer has no use for "0 Writing letters and 0 cards".
  if ((writing ?? 0) <= 0 && (speaking ?? 0) <= 0) return null;
  if (parts.length === 0) return null;
  const sharedPool = snapshot.flexibleCredits > 0 || (snapshot.sharedCredits ?? 0) > 0;
  const cost = `${CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY} credits each`;
  return sharedPool && parts.length > 1
    ? `You can start up to ${parts.join(' or ')} now (${cost}). Flexible and Shared credits can only be spent once.`
    : `You can start ${parts.join(' and ')} now (${cost}).`;
}

function bucketSortKey(bucket: AiPackageCreditBucket): number {
  const primaryIndex = PRIMARY_ORDER.indexOf(bucket.key as (typeof PRIMARY_ORDER)[number]);
  if (primaryIndex >= 0) return primaryIndex;
  return bucket.key === 'flexible_ws' ? 5 : 6;
}

function hasPassed(value?: string | null): boolean {
  const ms = value ? new Date(value).getTime() : Number.NaN;
  return Number.isFinite(ms) && ms <= Date.now();
}

// The API stamps each grant's state; an absent/"active" one whose end date has passed
// is derived as expired so a lapsed grant never reads as live.
function grantState(grant: AiPackageCreditGrantSource): AiPackageCreditGrantStatus {
  if (grant.status && grant.status !== 'active') return grant.status;
  return hasPassed(grant.expiresAt) ? 'expired' : 'active';
}

function grantValidityLabel(grant: AiPackageCreditGrantSource): string {
  const state = grantState(grant);
  const range = formatValidityWindow(grant.validFrom, grant.expiresAt);
  // An exam-pass lapse ends a grant before its own end date: only a date already
  // behind us is printed, otherwise "N days left" would contradict the state.
  if (state === 'expired') return hasPassed(grant.expiresAt) ? range : 'Expired';
  return state === 'scheduled' ? `Scheduled · ${range}` : range;
}

// The validity line of one bucket, or null when nothing ever sourced it (the API then
// falls back to the wallet-level expiry, which belongs to a different package).
function bucketValidityLabel(bucket: AiPackageCreditBucket, grants: AiPackageCreditGrantSource[]): string | null {
  if (!bucket.unlimited && !bucket.sourcePackages && grants.length === 0) return null;
  const lapsed = grants.length > 0 && grants.every((grant) => grantState(grant) === 'expired');
  // Nothing live is left and the credits lapsed: that wallet-level window would
  // contradict the expired figure.
  if (!bucket.unlimited && bucket.remaining === 0 && ((bucket.expired ?? 0) > 0 || lapsed)) return 'Expired';
  return formatValidityWindow(bucket.validFrom, bucket.expiresAt);
}

function hasBucketActivity(bucket: AiPackageCreditBucket): boolean {
  return (
    bucket.unlimited ||
    bucket.remaining > 0 ||
    bucket.used > 0 ||
    (bucket.expired ?? 0) > 0 ||
    bucket.grants.some((grant) => grantState(grant) !== 'reversed')
  );
}

// A learner who never bought anything gets five all-zero buckets from the API, so the
// bucket list alone proves nothing: something must be non-zero, unlimited, lapsed or granted.
export function hasVisibleCreditActivity(snapshot: AiPackageCreditSnapshot): boolean {
  return (
    snapshot.readingTestsRemaining !== 0 ||
    snapshot.listeningTestsRemaining !== 0 ||
    snapshot.writingOnlyCredits > 0 ||
    snapshot.speakingOnlyCredits > 0 ||
    snapshot.flexibleCredits > 0 ||
    (snapshot.sharedCredits ?? 0) > 0 ||
    snapshot.mockExamsRemaining > 0 ||
    snapshot.writingUnlimited === true ||
    snapshot.speakingUnlimited === true ||
    snapshot.expiredBecausePassed ||
    Boolean(snapshot.buckets?.some(hasBucketActivity))
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
  const startableActivities = startableActivitiesLabel(snapshot);

  return (
    <section aria-label="AI credits" className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
      <header className="mb-3 flex items-baseline justify-between gap-2">
        <h2 className="text-base font-semibold text-navy">AI Credits</h2>
        {expired ? (
          <span className="rounded-full bg-danger/10 px-2 py-0.5 text-xs font-medium text-danger-strong">
            Expired — passed OET
          </span>
        ) : (
          snapshot.expiresAt && (
            <span className="text-xs text-muted">{formatValidityWindow(null, snapshot.expiresAt)}</span>
          )
        )}
      </header>

      {startableActivities ? (
        <p className="mb-3 text-xs text-muted" data-testid="credit-startable-activities">
          {startableActivities}
        </p>
      ) : null}

      {buckets.length === 0 ? (
        <p className="text-sm text-muted">No active AI credit balances.</p>
      ) : (
        <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {buckets.map((bucket) => {
            const grants = bucket.grants.filter((grant) => grantState(grant) !== 'reversed');
            const showGrants = grants.length > 1 || grants.some((grant) => grantState(grant) !== 'active');
            const activities = bucket.unlimited ? null : describeCreditsAsActivities(bucket.key, bucket.remaining);
            // Total includes the lapsed credits, so a fully expired purchase still
            // reads as what was bought (Total 52 / Used 0 / Remaining 0 / Expired 52).
            const expiredCredits = bucket.expired ?? 0;
            const total = bucket.totalGranted + expiredCredits;
            const validityLine = [bucket.sourcePackages, bucketValidityLabel(bucket, grants)].filter(Boolean).join(' · ');
            return (
              <li
                key={bucket.key}
                className="rounded-xl border border-black/5 bg-background-light px-3 py-2"
                data-testid={`credit-bucket-${bucket.key}`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-medium text-navy">{bucket.label}</span>
                  {bucket.unlimited ? (
                    <span className="text-sm font-semibold text-success-strong">Unlimited</span>
                  ) : (
                    <span className="text-sm font-semibold text-navy tabular-nums">
                      {bucket.remaining} <span className="font-normal text-muted">remaining</span>
                    </span>
                  )}
                </div>
                {activities ? <p className="mt-0.5 text-xs text-muted">= {activities}</p> : null}
                {!bucket.unlimited && (total > 0 || bucket.used > 0) && (
                  <p className="mt-0.5 text-xs text-muted tabular-nums">
                    Total {total}
                    {expiredCredits > 0 ? ` (incl. ${expiredCredits} expired)` : ''} · Used {bucket.used}
                  </p>
                )}
                {validityLine ? (
                  <p className="mt-0.5 truncate text-xs text-muted" title={validityLine}>
                    {validityLine}
                  </p>
                ) : null}
                {showGrants ? (
                  <ul className="mt-1 space-y-0.5" aria-label={`${bucket.label} grants`}>
                    {grants.map((grant, index) => (
                      <li
                        key={`${grant.sourceReferenceId ?? grant.packageId ?? grant.description}-${index}`}
                        className="flex items-baseline justify-between gap-2 text-2xs text-muted"
                      >
                        <span className="truncate" title={grant.description}>
                          {grant.totalGranted} · {grant.description || 'Package'}
                        </span>
                        <span className="shrink-0 tabular-nums">{grantValidityLabel(grant)}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
