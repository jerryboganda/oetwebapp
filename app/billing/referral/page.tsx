'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  CheckCircle2,
  Copy,
  DollarSign,
  Gift,
  Percent,
  RefreshCw,
  Share2,
  Users,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { fetchFreezeStatus, fetchReferralInfo, generateReferralCode } from '@/lib/api';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import {
  BackToBillingLink,
  FREEZE_BLOCKED_MESSAGE,
  FREEZE_UNVERIFIED_MESSAGE,
  isFreezeEffective,
} from '@/components/domain/billing';

interface ReferralInfo {
  referralCode: string | null;
  referralsMade: number;
  creditsEarned: number;
  referrerCreditAmount: number;
  referredDiscountPercent: number;
}

export default function ReferralPage() {
  const [info, setInfo] = useState<ReferralInfo | null>(null);
  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [freezeLoadFailed, setFreezeLoadFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [copied, setCopied] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('referral_page_viewed');
    Promise.allSettled([fetchReferralInfo(), fetchFreezeStatus()])
      .then(([referralResult, freezeResult]) => {
        if (referralResult.status === 'fulfilled') {
          setInfo(referralResult.value as ReferralInfo);
        } else {
          setLoadError(
            referralResult.reason instanceof Error
              ? referralResult.reason.message
              : 'Unable to load referral information.',
          );
        }
        if (freezeResult.status === 'fulfilled') {
          setFreezeState(freezeResult.value as LearnerFreezeStatus);
          setFreezeLoadFailed(false);
        } else {
          setFreezeState(null);
          setFreezeLoadFailed(true);
        }
      })
      .finally(() => setLoading(false));
  }, []);

  const isFrozen = isFreezeEffective(freezeState);
  const mutationsBlocked = freezeLoadFailed || isFrozen;
  const blockedMessage = freezeLoadFailed ? FREEZE_UNVERIFIED_MESSAGE : FREEZE_BLOCKED_MESSAGE;

  const generateCode = useCallback(async () => {
    if (mutationsBlocked) return;
    setGenerating(true);
    try {
      const data = (await generateReferralCode()) as { referralCode: string };
      setInfo((prev) => (prev ? { ...prev, referralCode: data.referralCode } : prev));
      analytics.track('referral_code_generated');
    } catch {
      /* swallow — surfaced via UI absence of code */
    } finally {
      setGenerating(false);
    }
  }, [mutationsBlocked]);

  const copyCode = useCallback(async () => {
    if (!info?.referralCode) return;
    try {
      await navigator.clipboard.writeText(info.referralCode);
      setCopied(true);
      analytics.track('referral_code_copied');
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard unavailable */
    }
  }, [info]);

  const shareCode = useCallback(async () => {
    if (!info?.referralCode) return;
    const shareText = `Use my referral code ${info.referralCode} for any referral benefit currently published by OET Prep.`;
    try {
      if (typeof navigator !== 'undefined' && navigator.share) {
        await navigator.share({ title: 'Join OET Prep', text: shareText });
        analytics.track('referral_shared_native');
      } else {
        await navigator.clipboard.writeText(shareText);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }
    } catch {
      /* user cancelled share */
    }
  }, [info]);

  const heroHighlights = useMemo(() => {
    if (!info) return [];
    return [
      { icon: Users, label: 'Friends referred', value: `${info.referralsMade}` },
      { icon: DollarSign, label: 'Credits earned', value: `${info.creditsEarned}` },
      { icon: Gift, label: 'Per referral', value: `${info.referrerCreditAmount} credits` },
      { icon: Percent, label: 'Friend discount', value: `${info.referredDiscountPercent}%` },
    ];
  }, [info]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Referral program" backHref="/billing">
        <div className="space-y-6">
          <BackToBillingLink />
          <Skeleton className="h-44 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Referral program" backHref="/billing">
      <div className="space-y-6">
        <BackToBillingLink />

        <LearnerPageHero
          eyebrow="Billing"
          icon={Gift}
          accent="purple"
          title="Referral program"
          description="Invite colleagues and classmates to OET Prep. Referral benefits are applied only when the backend confirms the current terms and a qualifying paid subscription clears."
          highlights={heroHighlights}
        />

        {isFrozen ? (
          <InlineAlert variant="warning">
            Your account is frozen, so generating new referral codes is paused. Existing codes still work for friends.
          </InlineAlert>
        ) : null}
        {freezeLoadFailed ? (
          <InlineAlert variant="error">{FREEZE_UNVERIFIED_MESSAGE}</InlineAlert>
        ) : null}
        {loadError ? <InlineAlert variant="error">{loadError}</InlineAlert> : null}

        <section
          aria-labelledby="referral-how-heading"
          className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
        >
          <LearnerSurfaceSectionHeader
            eyebrow="How it works"
            icon={Gift}
            title="Three steps to earn credits"
          />
          <h2 id="referral-how-heading" className="sr-only">
            How the referral program works
          </h2>
          <ol className="mt-5 grid gap-4 sm:grid-cols-3" role="list">
            {[
              {
                step: '1',
                title: 'Share your code',
                desc: 'Send your unique referral code to friends preparing for the OET.',
              },
              {
                step: '2',
                title: 'They subscribe',
                desc: info
                  ? `They get the backend-published ${info.referredDiscountPercent}% referral discount on their first paid plan.`
                  : 'Any friend discount is shown only after referral terms load from the backend.',
              },
              {
                step: '3',
                title: 'You earn credits',
                desc: info
                  ? `You receive ${info.referrerCreditAmount} backend-published credits per successful referral.`
                  : 'Credit amounts are shown only after referral terms load from the backend.',
              },
            ].map((s) => (
              <li
                key={s.step}
                className="rounded-2xl border border-border bg-background-light p-4 text-center"
              >
                <div className="mx-auto mb-2 flex h-10 w-10 items-center justify-center rounded-full bg-primary text-sm font-bold text-white">
                  {s.step}
                </div>
                <p className="text-sm font-bold text-navy">{s.title}</p>
                <p className="mt-1 text-xs text-muted">{s.desc}</p>
              </li>
            ))}
          </ol>
        </section>

        <section
          aria-labelledby="referral-code-heading"
          className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
        >
          <LearnerSurfaceSectionHeader
            eyebrow="Your code"
            icon={Share2}
            title="Your referral code"
            action={
              info?.referralCode ? <Badge variant="success">Active</Badge> : null
            }
          />
          <h2 id="referral-code-heading" className="sr-only">
            Your referral code
          </h2>
          {info?.referralCode ? (
            <div className="mt-5 space-y-4">
              <div className="flex items-center gap-3">
                <div
                  aria-label="Your referral code"
                  className="flex-1 select-all rounded-xl border border-border bg-background-light px-4 py-3 text-center font-mono text-lg font-bold tracking-widest text-navy"
                >
                  {info.referralCode}
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={copyCode}
                  aria-label={copied ? 'Copied to clipboard' : 'Copy referral code to clipboard'}
                  className="h-12 w-12 shrink-0"
                >
                  {copied ? (
                    <CheckCircle2 className="h-5 w-5 text-success" aria-hidden="true" />
                  ) : (
                    <Copy className="h-5 w-5" aria-hidden="true" />
                  )}
                </Button>
              </div>
              {copied ? (
                <p className="text-center text-xs text-success" role="status">
                  Copied to clipboard.
                </p>
              ) : null}
              <div className="flex flex-wrap gap-3">
                <Button
                  variant="primary"
                  onClick={shareCode}
                  className="flex-1"
                  aria-label="Share referral code"
                >
                  <Share2 className="h-4 w-4" aria-hidden="true" />
                  Share code
                </Button>
                <Button
                  variant="outline"
                  onClick={copyCode}
                  className="flex-1"
                  aria-label="Copy referral code"
                >
                  <Copy className="h-4 w-4" aria-hidden="true" />
                  Copy code
                </Button>
              </div>
            </div>
          ) : (
            <div className="mt-5 rounded-2xl border border-dashed border-border bg-background-light p-6 text-center">
              <Gift className="mx-auto mb-3 h-10 w-10 text-muted/40" aria-hidden="true" />
              <p className="mb-4 text-sm text-muted">
                Generate your unique referral code to start inviting friends.
              </p>
              <Button
                variant="primary"
                onClick={generateCode}
                disabled={generating || mutationsBlocked}
                aria-label={
                  mutationsBlocked
                    ? `Generate referral code (unavailable: ${blockedMessage})`
                    : 'Generate my referral code'
                }
              >
                {generating ? (
                  <RefreshCw className="h-4 w-4 animate-spin" aria-hidden="true" />
                ) : (
                  <Gift className="h-4 w-4" aria-hidden="true" />
                )}
                {generating ? 'Generating…' : 'Generate my code'}
              </Button>
            </div>
          )}
        </section>

        {info ? (
          <section
            aria-labelledby="referral-stats-heading"
            className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
          >
            <LearnerSurfaceSectionHeader
              eyebrow="Stats"
              icon={Users}
              title="Your referral stats"
            />
            <h2 id="referral-stats-heading" className="sr-only">
              Your referral statistics
            </h2>
            <dl className="mt-5 grid gap-3 sm:grid-cols-3">
              <div className="rounded-2xl border border-border bg-background-light p-4 text-center">
                <Users className="mx-auto mb-1.5 h-5 w-5 text-info" aria-hidden="true" />
                <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                  Friends referred
                </dt>
                <dd className="mt-1 text-2xl font-black text-navy">{info.referralsMade}</dd>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4 text-center">
                <DollarSign
                  className="mx-auto mb-1.5 h-5 w-5 text-success"
                  aria-hidden="true"
                />
                <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                  Credits earned
                </dt>
                <dd className="mt-1 text-2xl font-black text-navy">{info.creditsEarned}</dd>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4 text-center">
                <Gift className="mx-auto mb-1.5 h-5 w-5 text-primary" aria-hidden="true" />
                <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                  Per referral
                </dt>
                <dd className="mt-1 text-2xl font-black text-navy">{info.referrerCreditAmount}</dd>
              </div>
            </dl>
          </section>
        ) : null}

        <section
          aria-labelledby="referral-terms-heading"
          className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
        >
          <LearnerSurfaceSectionHeader
            eyebrow="Fine print"
            title="Referral terms"
          />
          <h2 id="referral-terms-heading" className="sr-only">
            Referral program terms
          </h2>
          <ul className="mt-4 list-disc space-y-1 pl-5 text-sm text-muted">
            <li>
              Credits are awarded after the referred user completes their first paid subscription.
            </li>
            <li>
               Any referred-user discount must match the terms returned by the backend for your account.
            </li>
            <li>Self-referrals are not permitted and will be voided.</li>
            <li>Referral abuse, duplicate accounts, payment reversals, or suspicious attribution can pause credits pending support review.</li>
            <li>Credits are wallet credits only; expert-review credits remain separate and are not interchangeable.</li>
            <li>Contact support if a referral attribution needs manual investigation or privacy deletion handling.</li>
          </ul>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
