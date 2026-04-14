'use client';

import { useEffect, useState, useCallback } from 'react';
import { Gift, Copy, CheckCircle2, Users, DollarSign, Share2, RefreshCw } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';

/* ── types ─────────────────────────────────────── */
interface ReferralInfo {
  referralCode: string | null;
  referralsMade: number;
  creditsEarned: number;
  referrerCreditAmount: number;
  referredDiscountPercent: number;
}

/* ── api helper ───────────────────────────────── */
async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ReferralPage() {
  /* state */
  const [info, setInfo] = useState<ReferralInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [copied, setCopied] = useState(false);

  /* load referral info */
  useEffect(() => {
    analytics.track('referral_page_viewed');
    apiRequest<ReferralInfo>('/v1/learner/referral')
      .then(setInfo)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  /* generate code */
  const generateCode = useCallback(async () => {
    setGenerating(true);
    try {
      const data = await apiRequest<{ referralCode: string }>('/v1/learner/referral/generate', { method: 'POST' });
      setInfo(prev => prev ? { ...prev, referralCode: data.referralCode } : prev);
      analytics.track('referral_code_generated');
    } catch { /* */ }
    setGenerating(false);
  }, []);

  /* copy to clipboard */
  const copyCode = useCallback(async () => {
    if (!info?.referralCode) return;
    try {
      await navigator.clipboard.writeText(info.referralCode);
      setCopied(true);
      analytics.track('referral_code_copied');
      setTimeout(() => setCopied(false), 2000);
    } catch { /* */ }
  }, [info]);

  /* share */
  const shareCode = useCallback(async () => {
    if (!info?.referralCode) return;
    const shareData = {
      title: 'Join OET Prep',
      text: `Use my referral code ${info.referralCode} to get ${info.referredDiscountPercent}% off your first subscription!`,
    };
    try {
      if (navigator.share) {
        await navigator.share(shareData);
        analytics.track('referral_shared_native');
      } else {
        await navigator.clipboard.writeText(shareData.text);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }
    } catch { /* user cancelled share */ }
  }, [info]);

  /* ── render ────────────────────────────────── */
  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-2xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-48" /><Skeleton className="h-4 w-80" />
          <Skeleton className="h-48" /><Skeleton className="h-32" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Referral Program"
        description="Invite friends and earn credits — they get a discount, you get rewarded"
      />

      <div className="max-w-2xl mx-auto px-4 pb-12 space-y-6">

        {/* how it works */}
        <Card className="p-6 bg-gradient-to-br from-primary/5 to-primary/10 border-primary/20">
          <h2 className="font-semibold mb-4 flex items-center gap-2"><Gift className="h-5 w-5 text-primary" />How It Works</h2>
          <div className="grid sm:grid-cols-3 gap-4">
            {[
              { step: '1', title: 'Share Your Code', desc: 'Send your unique referral code to friends' },
              { step: '2', title: 'They Subscribe', desc: `They get ${info?.referredDiscountPercent ?? 10}% off their first plan` },
              { step: '3', title: 'You Earn Credits', desc: `You receive ${info?.referrerCreditAmount ?? 10} credits per referral` },
            ].map(s => (
              <div key={s.step} className="text-center">
                <div className="h-10 w-10 rounded-full bg-primary text-primary-foreground font-bold flex items-center justify-center mx-auto mb-2 text-sm">
                  {s.step}
                </div>
                <p className="font-medium text-sm">{s.title}</p>
                <p className="text-xs text-muted-foreground mt-1">{s.desc}</p>
              </div>
            ))}
          </div>
        </Card>

        {/* referral code */}
        <Card className="p-6">
          <h2 className="font-semibold mb-4">Your Referral Code</h2>

          {info?.referralCode ? (
            <div className="space-y-4">
              {/* code display */}
              <div className="flex items-center gap-3">
                <div className="flex-1 bg-muted/50 rounded-lg px-4 py-3 font-mono text-lg font-bold tracking-widest text-center select-all">
                  {info.referralCode}
                </div>
                <Button variant="outline" size="sm" onClick={copyCode} className="h-12 w-12 shrink-0">
                  {copied ? <CheckCircle2 className="h-5 w-5 text-green-600" /> : <Copy className="h-5 w-5" />}
                </Button>
              </div>
              {copied && <p className="text-xs text-green-600 text-center">Copied to clipboard!</p>}

              {/* share buttons */}
              <div className="flex gap-3">
                <Button className="flex-1" onClick={shareCode}>
                  <Share2 className="h-4 w-4 mr-2" />Share Code
                </Button>
                <Button variant="outline" className="flex-1" onClick={copyCode}>
                  <Copy className="h-4 w-4 mr-2" />Copy Code
                </Button>
              </div>
            </div>
          ) : (
            <div className="text-center py-6">
              <Gift className="h-10 w-10 mx-auto mb-3 text-muted-foreground opacity-40" />
              <p className="text-sm text-muted-foreground mb-4">Generate your unique referral code to start inviting friends</p>
              <Button onClick={generateCode} disabled={generating}>
                {generating ? <RefreshCw className="h-4 w-4 mr-2 animate-spin" /> : <Gift className="h-4 w-4 mr-2" />}
                {generating ? 'Generating…' : 'Generate My Code'}
              </Button>
            </div>
          )}
        </Card>

        {/* stats */}
        {info && (
          <div className="grid grid-cols-3 gap-3">
            <Card className="p-4 text-center">
              <Users className="h-5 w-5 mx-auto mb-1.5 text-blue-500" />
              <p className="text-2xl font-bold">{info.referralsMade}</p>
              <p className="text-xs text-muted-foreground">Friends Referred</p>
            </Card>
            <Card className="p-4 text-center">
              <DollarSign className="h-5 w-5 mx-auto mb-1.5 text-green-500" />
              <p className="text-2xl font-bold">{info.creditsEarned}</p>
              <p className="text-xs text-muted-foreground">Credits Earned</p>
            </Card>
            <Card className="p-4 text-center">
              <Gift className="h-5 w-5 mx-auto mb-1.5 text-primary" />
              <p className="text-2xl font-bold">{info.referrerCreditAmount}</p>
              <p className="text-xs text-muted-foreground">Per Referral</p>
            </Card>
          </div>
        )}

        {/* terms */}
        <div className="text-xs text-muted-foreground bg-muted/30 rounded-lg p-4 space-y-1">
          <p className="font-medium">Referral Terms</p>
          <ul className="list-disc list-inside space-y-0.5">
            <li>Credits are awarded after the referred user completes their first paid subscription.</li>
            <li>The referred user receives a {info?.referredDiscountPercent ?? 10}% discount on their first plan.</li>
            <li>Self-referrals are not permitted and will be voided.</li>
            <li>Credits do not expire and can be used toward any plan or add-on.</li>
          </ul>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
