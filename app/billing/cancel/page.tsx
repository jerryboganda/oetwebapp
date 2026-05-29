'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { XCircle, Tag } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { apiClient } from '@/lib/api';

const REASONS = [
  { value: 'too_expensive', label: 'Too expensive' },
  { value: 'passed_exam', label: 'Passed my exam' },
  { value: 'not_useful', label: 'Not useful' },
  { value: 'technical', label: 'Technical issues' },
  { value: 'switching', label: 'Switching to another service' },
  { value: 'other', label: 'Other' },
];

interface IntentResponse {
  id: string;
  status: string;
  offeredCouponCode: string | null;
}

export default function CancelSubscriptionPage() {
  const router = useRouter();
  const [step, setStep] = useState<'reason' | 'deflection' | 'confirm'>('reason');
  const [reason, setReason] = useState('too_expensive');
  const [detail, setDetail] = useState('');
  const [intent, setIntent] = useState<IntentResponse | null>(null);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  async function submitReason() {
    setWorking(true);
    setError(null);
    try {
      const result = await apiClient.post<IntentResponse>('/v1/billing/subscription/cancel-intent', {
        reason,
        reasonDetail: detail || null,
      });
      setIntent(result);
      setStep(result.offeredCouponCode ? 'deflection' : 'confirm');
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Could not start cancellation.');
    } finally {
      setWorking(false);
    }
  }

  async function acceptOffer() {
    if (!intent) return;
    setWorking(true);
    try {
      await apiClient.post(`/v1/billing/subscription/cancel-intent/${intent.id}/retain`, {});
      setToast({ variant: 'success', message: 'Coupon applied. Welcome back!' });
      setTimeout(() => router.push('/billing'), 1500);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Could not apply coupon.');
    } finally {
      setWorking(false);
    }
  }

  async function confirmCancel() {
    if (!intent) return;
    setWorking(true);
    try {
      await apiClient.post(`/v1/billing/subscription/cancel-intent/${intent.id}/confirm`, {});
      setToast({ variant: 'success', message: 'Subscription cancelled. You keep access until the end of the current period.' });
      setTimeout(() => router.push('/billing'), 1500);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Could not cancel.');
    } finally {
      setWorking(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={<XCircle className="h-6 w-6" />}
        eyebrow="Billing"
        title="Cancel subscription"
        description="Tell us why so we can do better. You can change your mind at any step."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="space-y-4 rounded-2xl border border-border bg-surface p-6 shadow-sm">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {step === 'reason' && (
          <div className="space-y-3">
            <Select label="Why are you cancelling?" value={reason} options={REASONS} onChange={(e) => setReason(e.target.value)} />
            <Textarea value={detail} onChange={(e) => setDetail(e.target.value)} placeholder="Anything else? (optional)" />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => router.push('/billing')}>Never mind</Button>
              <Button onClick={submitReason} disabled={working}>Continue</Button>
            </div>
          </div>
        )}

        {step === 'deflection' && intent?.offeredCouponCode && (
          <div className="space-y-4">
            <div className="rounded-xl border border-success/30 bg-success/10 p-5">
              <Tag className="mb-2 h-5 w-5 text-success" aria-hidden="true" />
              <p className="text-lg font-semibold text-success">Wait, here’s a discount</p>
              <p className="mt-1 text-sm text-navy">
                Stay with us and we’ll apply the coupon code <strong>{intent.offeredCouponCode}</strong> to your next renewal.
              </p>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setStep('confirm')}>Cancel anyway</Button>
              <Button onClick={acceptOffer} disabled={working}>Apply discount &amp; stay</Button>
            </div>
          </div>
        )}

        {step === 'confirm' && (
          <div className="space-y-4">
            <div className="rounded-xl border border-danger/30 bg-danger/10 p-5">
              <p className="text-lg font-semibold text-danger">Confirm cancellation</p>
              <p className="mt-1 text-sm text-navy">
                Your subscription will be cancelled. You keep access until the end of your current billing period.
                After that, premium features lock; your account remains.
              </p>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => router.push('/billing')}>Take me back</Button>
              <Button variant="destructive" onClick={confirmCancel} disabled={working}>Confirm cancel</Button>
            </div>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
