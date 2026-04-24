'use client';

import { useEffect, useState } from 'react';
import { Users, Copy, Gift, Check, ExternalLink } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { getReferralData } from '@/lib/learner-data';
import { generateReferralCode } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ReferralInfo } from '@/lib/types/learner';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'danger' | 'outline' }> = {
  pending: { label: 'Pending', variant: 'outline' },
  activated: { label: 'Activated', variant: 'default' },
  rewarded: { label: 'Rewarded', variant: 'success' },
  expired: { label: 'Expired', variant: 'danger' },
};

export default function ReferralPage() {
  const [referral, setReferral] = useState<ReferralInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [isMutating, setIsMutating] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    analytics.track('content_view', { page: 'referral' });
    getReferralData()
      .then(setReferral)
      .catch(() => setError('Unable to load referral data.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleGenerate() {
    setIsMutating(true);
    try {
      await generateReferralCode();
      const refreshed = await getReferralData();
      setReferral(refreshed);
      setToast({ variant: 'success', message: 'Referral code generated!' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to generate referral code.' });
    } finally {
      setIsMutating(false);
    }
  }

  function handleCopy() {
    if (!referral?.referralCode) return;
    const url = `${window.location.origin}/signup?ref=${referral.referralCode}`;
    navigator.clipboard.writeText(url).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-56" />
          <Skeleton className="h-48 w-full rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <LearnerPageHero
        title="Refer a Friend"
        description="Share OET Prep with colleagues and earn credits for each signup."
        icon={<Users className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* No referral code yet */}
      {referral && !referral.referralCode && (
        <MotionSection>
          <Card className="p-6 text-center">
            <Gift className="w-12 h-12 text-primary mx-auto mb-4" />
            <h3 className="text-lg font-semibold text-navy mb-2">Start Referring</h3>
            <p className="text-sm text-muted mb-4 max-w-md mx-auto">
              Generate your unique referral link. Your friend gets 10% off, and you earn $10 credit for each activated referral.
            </p>
            <Button onClick={handleGenerate} disabled={isMutating} size="lg">
              {isMutating ? 'Generating…' : 'Generate Referral Link'}
            </Button>
          </Card>
        </MotionSection>
      )}

      {/* Referral code + stats */}
      {referral?.referralCode && (
        <>
          {/* Share Card */}
          <MotionSection>
            <Card className="p-6">
              <LearnerSurfaceSectionHeader
                icon={<ExternalLink className="w-5 h-5" />}
                title="Your Referral Link"
              />
              <div className="mt-3 flex items-center gap-2 bg-background-light rounded-lg p-3">
                <code className="flex-1 text-sm text-navy truncate">
                  {typeof window !== 'undefined' ? `${window.location.origin}/signup?ref=${referral.referralCode}` : referral.referralCode}
                </code>
                <Button size="sm" variant="outline" onClick={handleCopy}>
                  {copied ? <Check className="w-4 h-4 text-success" /> : <Copy className="w-4 h-4" />}
                  {copied ? 'Copied' : 'Copy'}
                </Button>
              </div>
            </Card>
          </MotionSection>

          {/* Stats */}
          <MotionSection className="mt-6">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <MotionItem>
                <Card className="p-4 text-center">
                  <p className="text-xs text-muted">Total Referrals</p>
                  <p className="text-2xl font-bold text-navy">{referral.totalReferrals}</p>
                </Card>
              </MotionItem>
              <MotionItem>
                <Card className="p-4 text-center">
                  <p className="text-xs text-muted">Activated</p>
                  <p className="text-2xl font-bold text-success">{referral.activatedReferrals}</p>
                </Card>
              </MotionItem>
              <MotionItem>
                <Card className="p-4 text-center">
                  <p className="text-xs text-muted">Credits Earned</p>
                  <p className="text-2xl font-bold text-primary">${referral.totalCreditsEarned}</p>
                </Card>
              </MotionItem>
            </div>
          </MotionSection>

          {/* Referral History */}
          {referral.referrals.length > 0 && (
            <MotionSection className="mt-6">
              <LearnerSurfaceSectionHeader
                icon={<Users className="w-5 h-5" />}
                title="Referral History"
              />
              <div className="overflow-x-auto rounded-xl border border-border mt-3">
                <table className="min-w-full divide-y divide-border">
                  <thead className="bg-background-light">
                    <tr>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-muted">User</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-muted">Status</th>
                      <th className="px-4 py-3 text-left text-xs font-semibold text-muted">Date</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {referral.referrals.map((r) => (
                      <tr key={r.id} className="hover:bg-background-light">
                        <td className="px-4 py-3 text-sm text-navy">{r.referredUserId.slice(0, 8)}…</td>
                        <td className="px-4 py-3">
                          <Badge variant={STATUS_BADGE[r.status]?.variant ?? 'default'}>
                            {STATUS_BADGE[r.status]?.label ?? r.status}
                          </Badge>
                        </td>
                        <td className="px-4 py-3 text-sm text-muted">{new Date(r.createdAt).toLocaleDateString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </MotionSection>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
