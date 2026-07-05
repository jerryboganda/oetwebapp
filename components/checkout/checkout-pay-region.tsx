'use client';

import { Globe, Landmark } from 'lucide-react';
import { cn } from '@/lib/utils';
import { EgyptPaymentMethods } from './egypt-payment-methods';
import { UkBankTransfer } from './uk-bank-transfer';

export type PayRegion = 'global' | 'egypt';

interface CheckoutPayRegionProps {
  value: PayRegion;
  onChange: (value: PayRegion) => void;
  egyptHref: string;
  disabled?: boolean;
  /** The card / PayPal flow rendered inside the "Pay globally" route. */
  children: React.ReactNode;
}

export function CheckoutPayRegion({ value, onChange, egyptHref, disabled, children }: CheckoutPayRegionProps) {
  return (
    <div>
      {/* Two prominent, mutually-exclusive payment routes */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <RegionCard
          active={value === 'global'}
          onClick={() => onChange('global')}
          icon={<Globe className="h-5 w-5" />}
          title="Pay globally"
          subtitle="Card, PayPal or UK bank transfer"
        />
        <RegionCard
          active={value === 'egypt'}
          onClick={() => onChange('egypt')}
          icon={<Landmark className="h-5 w-5" />}
          title="Pay inside Egypt"
          subtitle="InstaPay · Vodafone Cash · bank"
        />
      </div>

      <div className="mt-5">
        {value === 'global' ? (
          <div className="space-y-4">
            {children}
            <Divider label="Or pay by bank transfer" />
            <UkBankTransfer />
          </div>
        ) : (
          <EgyptPaymentMethods egyptHref={egyptHref} disabled={disabled} />
        )}
      </div>
    </div>
  );
}

function RegionCard({
  active,
  onClick,
  icon,
  title,
  subtitle,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  title: string;
  subtitle: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        'group relative flex items-center gap-3.5 rounded-2xl border-2 p-4 text-left transition',
        active
          ? 'border-primary bg-primary/5 shadow-sm'
          : 'border-border bg-surface hover:border-primary/40 hover:bg-background-light',
      )}
    >
      <span
        className={cn(
          'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl transition',
          active ? 'bg-primary text-white' : 'bg-background-light text-muted group-hover:text-primary',
        )}
      >
        {icon}
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-base font-bold text-navy">{title}</span>
        <span className="block text-xs text-muted">{subtitle}</span>
      </span>
      <span
        className={cn(
          'flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 transition',
          active ? 'border-primary' : 'border-border',
        )}
      >
        {active ? <span className="h-2.5 w-2.5 rounded-full bg-primary" /> : null}
      </span>
    </button>
  );
}

function Divider({ label }: { label: string }) {
  return (
    <div className="flex items-center gap-3">
      <span className="h-px flex-1 bg-border" />
      <span className="text-xs font-medium uppercase tracking-wide text-muted">{label}</span>
      <span className="h-px flex-1 bg-border" />
    </div>
  );
}
