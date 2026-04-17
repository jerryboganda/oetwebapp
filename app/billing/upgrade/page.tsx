'use client';

import { useEffect, useState } from 'react';
import { CreditCard, ArrowUpRight, Sparkles, Check, X } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface PlanInfo {
  planId: string; planCode: string; planName: string; description: string; price: number; currency: string; interval: string;
  includedCredits: number; trialDays: number; isCurrent: boolean; isUpgrade: boolean; isDowngrade: boolean;
  entitlements: Record<string, unknown>;
}

interface UpgradeData {
  currentPlan: { planId: string; planName: string; price: number; includedCredits: number } | null;
  usage: { reviewsUsedThisMonth: number; creditsRemaining: number; subscriptionStarted: string | null; subscriptionEnds: string | null };
  plans: PlanInfo[];
  recommendation: string;
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function BillingUpgradePage() {
  const [data, setData] = useState<UpgradeData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('billing_upgrade_path_viewed');
    apiRequest<UpgradeData>('/v1/learner/billing/upgrade-path').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Plan Comparison" description="Compare plans and find the best fit for your OET preparation goals." />

      <MotionSection className="space-y-6 max-w-5xl mx-auto">
        {loading ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-64 rounded-xl" />)}</div>
        ) : data ? (
          <>
            {/* Usage summary */}
            <MotionItem>
              <Card className="p-5">
                <div className="flex items-center gap-3 mb-3"><CreditCard className="w-5 h-5 text-primary" /><h3 className="font-semibold">Your Usage</h3></div>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                  <div><p className="text-2xl font-bold">{data.usage.reviewsUsedThisMonth}</p><p className="text-xs text-muted-foreground">Reviews this month</p></div>
                  <div><p className="text-2xl font-bold">{data.usage.creditsRemaining}</p><p className="text-xs text-muted-foreground">Credits remaining</p></div>
                  <div><p className="text-sm font-medium">{data.currentPlan?.planName ?? 'No plan'}</p><p className="text-xs text-muted-foreground">Current plan</p></div>
                  <div><p className="text-sm font-medium">{data.currentPlan ? `$${data.currentPlan.price}` : '--'}</p><p className="text-xs text-muted-foreground">Monthly cost</p></div>
                </div>
                <p className="text-sm text-muted-foreground mt-3 bg-muted/50 rounded-lg px-3 py-2"><Sparkles className="w-4 h-4 inline mr-1" />{data.recommendation}</p>
              </Card>
            </MotionItem>

            <LearnerSurfaceSectionHeader title="Available Plans" />
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {data.plans.map(plan => (
                <MotionItem key={plan.planId}>
                  <Card className={`p-5 relative ${plan.isCurrent ? 'ring-2 ring-primary' : ''}`}>
                    {plan.isCurrent && <Badge className="absolute -top-2 left-4 bg-primary text-primary-foreground">Current</Badge>}
                    <h3 className="text-lg font-bold mt-1">{plan.planName}</h3>
                    <p className="text-3xl font-bold mt-2">${plan.price}<span className="text-sm font-normal text-muted-foreground">/{plan.interval}</span></p>
                    <p className="text-sm text-muted-foreground mt-2">{plan.description}</p>
                    <div className="mt-4 space-y-2 text-sm">
                      <div className="flex items-center gap-2"><Check className="w-4 h-4 text-emerald-500" /> {plan.includedCredits} review credits</div>
                      {plan.trialDays > 0 && <div className="flex items-center gap-2"><Check className="w-4 h-4 text-emerald-500" /> {plan.trialDays}-day free trial</div>}
                    </div>
                    {!plan.isCurrent && (
                      <Button variant={plan.isUpgrade ? 'secondary' : 'outline'} size="sm" className="w-full mt-4">
                        {plan.isUpgrade ? <><ArrowUpRight className="w-4 h-4 mr-1" /> Upgrade</> : 'Switch Plan'}
                      </Button>
                    )}
                  </Card>
                </MotionItem>
              ))}
            </div>
          </>
        ) : (
          <Card className="p-8 text-center text-muted-foreground"><p>Unable to load plan information.</p></Card>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
