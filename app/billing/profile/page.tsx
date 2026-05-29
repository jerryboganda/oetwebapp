'use client';

import { useCallback, useEffect, useState } from 'react';
import { Globe, Save } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchBillingProfile,
  updateBillingProfile,
  type BillingProfile,
  type BillingRegion,
} from '@/lib/api';

const COUNTRY_OPTIONS = [
  { value: '', label: 'Auto-detect' },
  { value: 'GB', label: 'United Kingdom (GBP)' },
  { value: 'AE', label: 'United Arab Emirates (AED)' },
  { value: 'SA', label: 'Saudi Arabia (SAR)' },
  { value: 'OM', label: 'Oman (OMR)' },
  { value: 'QA', label: 'Qatar (QAR)' },
  { value: 'KW', label: 'Kuwait (KWD)' },
  { value: 'BH', label: 'Bahrain (BHD)' },
  { value: 'EG', label: 'Egypt (EGP)' },
  { value: 'PK', label: 'Pakistan (PKR)' },
  { value: 'AU', label: 'Australia (AUD)' },
  { value: 'US', label: 'United States (USD)' },
];

const REGION_OPTIONS = [
  { value: '', label: 'Auto-detect' },
  { value: 'UK', label: 'United Kingdom' },
  { value: 'GULF', label: 'Gulf states' },
  { value: 'EGYPT', label: 'Egypt' },
  { value: 'PK', label: 'Pakistan' },
  { value: 'ROW', label: 'Rest of world' },
];

const CURRENCY_OPTIONS = [
  { value: '', label: 'Auto-detect' },
  ...['GBP', 'AED', 'SAR', 'OMR', 'QAR', 'KWD', 'BHD', 'EGP', 'PKR', 'AUD', 'USD', 'EUR'].map((c) => ({ value: c, label: c })),
];

export default function BillingProfilePage() {
  const [profile, setProfile] = useState<BillingProfile | null>(null);
  const [country, setCountry] = useState('');
  const [currency, setCurrency] = useState('');
  const [region, setRegion] = useState<BillingRegion | ''>('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    try {
      const data = await fetchBillingProfile();
      setProfile(data);
      setCountry(data.country ?? '');
      setCurrency(data.preferredCurrency ?? '');
      setRegion(data.preferredRegion ?? '');
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load billing profile.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const updated = await updateBillingProfile({
        country: country || null,
        preferredCurrency: currency || null,
        preferredRegion: (region || null) as BillingRegion | null,
      });
      setProfile(updated);
      setToast({ variant: 'success', message: 'Billing profile saved.' });
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to save billing profile.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={<Globe className="h-6 w-6" />}
        eyebrow="Billing"
        title="Region & currency"
        description="Set your country and currency so checkout, taxes, and payment options match where you live."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="space-y-4 rounded-2xl border border-border bg-surface p-6 shadow-sm">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {profile === null ? (
          <div className="space-y-3">
            <Skeleton className="h-6 w-1/3" />
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : (
          <>
            <div className="rounded-lg bg-muted/40 p-4 text-sm">
              <p className="font-medium">Detected</p>
              <p className="text-muted">
                {profile.detectedCountry || '-'} · {profile.detectedRegion} · {profile.detectedCurrency}{' '}
                <span className="text-xs">({profile.detectionSource})</span>
              </p>
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <Select label="Country" value={country} options={COUNTRY_OPTIONS} onChange={(e) => setCountry(e.target.value)} />
              <Select label="Region" value={region} options={REGION_OPTIONS} onChange={(e) => setRegion(e.target.value as BillingRegion | '')} />
              <Select label="Currency" value={currency} options={CURRENCY_OPTIONS} onChange={(e) => setCurrency(e.target.value)} />
            </div>

            <p className="text-xs text-muted">
              Leave any field on “Auto-detect” to let the system pick based on your IP, browser, or default.
            </p>

            <div className="flex justify-end">
              <Button onClick={handleSave} disabled={saving}>
                <Save className="mr-2 h-4 w-4" />
                {saving ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
