'use client';

import Link from 'next/link';
import { ArrowRight, Globe, Landmark } from 'lucide-react';
import { cn } from '@/lib/utils';

export type PayRegion = 'global' | 'egypt';

interface EgyptMethod {
  key: string;
  label: string;
  detail: string;
}

// Mirrors the `inside_egypt` methods on /billing/manual-payment. Shown here as a
// preview; the manual-payment page fetches the live, admin-configured list and
// handles instructions, QR and proof upload.
const EGYPT_METHODS: EgyptMethod[] = [
  { key: 'instapay', label: 'InstaPay QR / link', detail: 'Scan the QR or use the InstaPay link, then upload your proof.' },
  { key: 'vodafone', label: 'Vodafone Cash / Fawry', detail: 'Send to the number shown, then upload the confirmation.' },
  { key: 'qnb', label: 'QNB Egypt bank transfer', detail: 'Transfer to the QNB account, then upload your proof.' },
];

interface CheckoutPayRegionProps {
  value: PayRegion;
  onChange: (value: PayRegion) => void;
  egyptHref: string;
  disabled?: boolean;
  children: React.ReactNode;
}

export function CheckoutPayRegion({ value, onChange, egyptHref, disabled, children }: CheckoutPayRegionProps) {
  return (
    <div className="mt-5">
      <p className="text-sm font-medium">How would you like to pay?</p>
      <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2">
        <RegionCard
          active={value === 'global'}
          onClick={() => onChange('global')}
          icon={<Globe className="h-4 w-4" />}
          title="Pay globally"
          subtitle="Credit or debit card"
        />
        <RegionCard
          active={value === 'egypt'}
          onClick={() => onChange('egypt')}
          icon={<Landmark className="h-4 w-4" />}
          title="Pay inside Egypt"
          subtitle="InstaPay · Vodafone Cash · bank"
        />
      </div>

      {value === 'global' ? (
        <div className="mt-4">{children}</div>
      ) : (
        <div className="mt-4">
          <ul className="space-y-2">
            {EGYPT_METHODS.map((m) => (
              <li key={m.key} className="rounded-lg border border-border bg-background-light px-3 py-2">
                <p className="text-sm font-medium text-navy">{m.label}</p>
                <p className="mt-0.5 text-xs text-muted">{m.detail}</p>
              </li>
            ))}
          </ul>
          <Link
            href={disabled ? '#' : egyptHref}
            aria-disabled={disabled}
            className={cn(
              'mt-3 inline-flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-5 py-3 text-sm font-semibold text-white transition hover:bg-primary/90',
              disabled && 'pointer-events-none opacity-50',
            )}
          >
            Continue to Egyptian payment <ArrowRight className="h-4 w-4" />
          </Link>
          <p className="mt-3 text-xs leading-5 text-muted">
            Pay with your chosen method and upload proof — we activate your access after a quick verification.
          </p>
        </div>
      )}
    </div>
  );
}

function RegionCard({ active, onClick, icon, title, subtitle }: {
  active: boolean; onClick: () => void; icon: React.ReactNode; title: string; subtitle: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-start gap-3 rounded-lg border px-3 py-3 text-left transition',
        active ? 'border-primary ring-1 ring-primary' : 'border-border hover:border-primary/50',
      )}
    >
      <span className="mt-0.5 text-muted">{icon}</span>
      <span>
        <span className="block text-sm font-medium text-navy">{title}</span>
        <span className="block text-xs text-muted">{subtitle}</span>
      </span>
    </button>
  );
}
