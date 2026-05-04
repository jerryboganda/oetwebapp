'use client';

import { useEffect, useState } from 'react';
import { CheckCircle2, XCircle, Sparkles, Star, Zap, Crown } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';
import { useRouter } from 'next/navigation';
import { fetchBilling } from '@/lib/api';

interface TierFeature {
  label: string;
  included: boolean;
  highlight?: string;
}

interface OetTier {
  id: string;
  code: string;
  name: string;
  description: string;
  priceMonthly: number;
  priceAnnual: number;
  currency: string;
  icon: typeof Sparkles;
  badge?: string;
  features: TierFeature[];
  cta: string;
  popular?: boolean;
}

const OET_TIERS: OetTier[] = [
  {
    id: 'tier-free',
    code: 'free',
    name: 'Free',
    description: 'Get started with core diagnostics and limited practice.',
    priceMonthly: 0,
    priceAnnual: 0,
    currency: 'AUD',
    icon: Sparkles,
    features: [
      { label: 'Diagnostic test (1 per skill)', included: true },
      { label: 'Limited practice questions', included: true },
      { label: 'AI writing evaluation (1 per month)', included: true },
      { label: 'Community forum access', included: true },
      { label: 'Expert review', included: false },
      { label: 'Mock exams', included: false },
      { label: 'Priority support', included: false },
    ],
    cta: 'Get Started Free',
  },
  {
    id: 'tier-core',
    code: 'core',
    name: 'Core',
    description: 'Full practice engine with AI feedback across all four skills.',
    priceMonthly: 29,
    priceAnnual: 290,
    currency: 'AUD',
    icon: Zap,
    badge: 'Most Popular',
    popular: true,
    features: [
      { label: 'Unlimited practice questions', included: true },
      { label: 'AI writing & speaking evaluation', included: true, highlight: 'Unlimited' },
      { label: 'Full mock exams', included: true, highlight: '2 per month' },
      { label: 'Reading & listening auto-scoring', included: true },
      { label: 'Study plan generator', included: true },
      { label: 'Progress tracking', included: true },
      { label: 'Expert review', included: false },
      { label: 'Priority support', included: false },
    ],
    cta: 'Start Core Plan',
  },
  {
    id: 'tier-plus',
    code: 'plus',
    name: 'Plus',
    description: 'Everything in Core plus expert review credits and advanced analytics.',
    priceMonthly: 59,
    priceAnnual: 590,
    currency: 'AUD',
    icon: Star,
    features: [
      { label: 'Everything in Core', included: true },
      { label: 'Expert review credits', included: true, highlight: '4 per month' },
      { label: 'Mock exams', included: true, highlight: 'Unlimited' },
      { label: 'Compare-attempt analytics', included: true },
      { label: 'Readiness blockers & insights', included: true },
      { label: 'Priority email support', included: true },
      { label: 'Score guarantee eligibility', included: true },
    ],
    cta: 'Upgrade to Plus',
  },
  {
    id: 'tier-review',
    code: 'review',
    name: 'Review',
    description: 'Maximum expert attention with unlimited reviews and 1-on-1 coaching.',
    priceMonthly: 149,
    priceAnnual: 1490,
    currency: 'AUD',
    icon: Crown,
    features: [
      { label: 'Everything in Plus', included: true },
      { label: 'Unlimited expert reviews', included: true },
      { label: '1-on-1 speaking coaching sessions', included: true, highlight: '2 per month' },
      { label: 'Personal study coach', included: true },
      { label: 'Guaranteed 48-hour review turnaround', included: true },
      { label: 'Custom mock exam scheduling', included: true },
      { label: 'WhatsApp / phone support', included: true },
    ],
    cta: 'Get Maximum Support',
  },
];

export default function BillingPlansPage() {
  const router = useRouter();
  const [billing, setBilling] = useState<{ currentPlanId: string; currentPlan: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [interval, setInterval] = useState<'monthly' | 'annual'>('monthly');
  const [checkingOut, setCheckingOut] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('page_viewed', { page: 'billing-plans' });
    fetchBilling()
      .then((data) => setBilling({ currentPlanId: data.currentPlanId, currentPlan: data.currentPlan }))
      .catch(() => setError('Unable to load your billing status.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleCheckout(_tierCode: string) {
    // Navigate to the existing billing page which has the full plan change/checkout flow.
    // The billing page already handles plan upgrades, Stripe/PayPal checkout, and wallet.
    router.push('/billing');
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Plans">
        <div className="space-y-8">
          <Skeleton className="h-32 rounded-2xl" />
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-96 rounded-2xl" />
            ))}
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  const currentPlanCode = billing?.currentPlan?.toLowerCase() ?? '';

  return (
    <LearnerDashboardShell pageTitle="Plans & Pricing">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="OET Preparation"
          icon={Sparkles}
          accent="primary"
          title="Choose the plan that fits your goals"
          description="From free diagnostics to unlimited expert review, every tier is built around the OET scoring system and profession-specific coaching."
          highlights={[
            { icon: CheckCircle2, label: 'AI evaluation', value: 'Instant' },
            { icon: Star, label: 'Expert review', value: '48h turnaround' },
            { icon: Zap, label: 'Mock exams', value: 'Full OET format' },
          ]}
        />

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {/* Interval toggle */}
        <div className="flex justify-center">
          <div className="inline-flex bg-background-light rounded-xl p-1 border border-border">
            <button
              onClick={() => setInterval('monthly')}
              className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                interval === 'monthly' ? 'bg-surface text-navy shadow-sm' : 'text-muted hover:text-navy'
              }`}
            >
              Monthly
            </button>
            <button
              onClick={() => setInterval('annual')}
              className={`px-4 py-2 rounded-lg text-sm font-semibold transition-colors ${
                interval === 'annual' ? 'bg-surface text-navy shadow-sm' : 'text-muted hover:text-navy'
              }`}
            >
              Annual <span className="text-success text-xs ml-1">Save ~17%</span>
            </button>
          </div>
        </div>

        <LearnerSurfaceSectionHeader
          eyebrow="OET Tiers"
          title="Four levels of preparation depth"
          description="All plans use the same OET scoring engine. Upgrading unlocks more expert time, mock exams, and personal coaching."
        />

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {OET_TIERS.map((tier, index) => {
            const Icon = tier.icon;
            const isCurrent = currentPlanCode.includes(tier.code);
            const price = interval === 'annual' ? tier.priceAnnual : tier.priceMonthly;
            const priceLabel = price === 0 ? 'Free' : `$${price} ${tier.currency}`;
            const intervalLabel = price === 0 ? '' : interval === 'annual' ? '/year' : '/month';

            return (
              <MotionItem key={tier.id} delayIndex={index}>
                <Card className={`h-full flex flex-col border ${tier.popular ? 'border-primary/30 shadow-md' : 'border-border'} bg-surface p-6`}>
                  {tier.badge && (
                    <Badge variant="success" size="sm" className="mb-3 self-start">
                      {tier.badge}
                    </Badge>
                  )}
                  <div className="flex items-center gap-3 mb-4">
                    <div className={`w-10 h-10 rounded-xl flex items-center justify-center ${tier.popular ? 'bg-primary/10 text-primary' : 'bg-muted/10 text-muted'}`}>
                      <Icon className="w-5 h-5" />
                    </div>
                    <div>
                      <h3 className="text-lg font-bold text-navy">{tier.name}</h3>
                      <p className="text-xs text-muted">{tier.description}</p>
                    </div>
                  </div>

                  <div className="mb-6">
                    <span className="text-3xl font-black text-navy">{priceLabel}</span>
                    <span className="text-sm text-muted ml-1">{intervalLabel}</span>
                  </div>

                  <ul className="space-y-3 flex-1 mb-6">
                    {tier.features.map((feature) => (
                      <li key={feature.label} className="flex items-start gap-2 text-sm">
                        {feature.included ? (
                          <CheckCircle2 className="w-4 h-4 text-success shrink-0 mt-0.5" />
                        ) : (
                          <XCircle className="w-4 h-4 text-muted/40 shrink-0 mt-0.5" />
                        )}
                        <span className={feature.included ? 'text-navy' : 'text-muted/60'}>
                          {feature.label}
                          {feature.highlight && feature.included && (
                            <span className="text-success text-xs font-semibold ml-1">({feature.highlight})</span>
                          )}
                        </span>
                      </li>
                    ))}
                  </ul>

                  {isCurrent ? (
                    <Button variant="outline" disabled fullWidth>
                      Current Plan
                    </Button>
                  ) : (
                    <Button
                      variant={tier.popular ? 'primary' : 'outline'}
                      fullWidth
                      loading={checkingOut === tier.code}
                      onClick={() => handleCheckout(tier.code)}
                    >
                      {tier.cta}
                    </Button>
                  )}
                </Card>
              </MotionItem>
            );
          })}
        </div>

        <div className="rounded-2xl border border-border bg-background-light p-6">
          <h3 className="text-sm font-bold text-navy mb-2">Add-ons available on all paid plans</h3>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-sm text-muted">
            <div className="flex items-center gap-2">
              <Star className="w-4 h-4 text-primary" />
              <span>Extra review credit packs</span>
            </div>
            <div className="flex items-center gap-2">
              <Zap className="w-4 h-4 text-primary" />
              <span>Priority turnaround (24h)</span>
            </div>
            <div className="flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-primary" />
              <span>Wallet top-up for flexibility</span>
            </div>
          </div>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
