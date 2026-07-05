'use client';

import { useState } from 'react';
import { ChevronDown, Globe2, Landmark, MapPin, ShieldCheck } from 'lucide-react';
import { cn } from '@/lib/utils';
import { CopyField } from './copy-field';

type Field = { label: string; value: string; mono?: boolean };

interface Bank {
  id: string;
  /** Short tab label. */
  tag: string;
  /** Brand accent used for the little colour dot. */
  swatch: string;
  insideUk: Field[];
  international: Field[];
}

// Owner-supplied UK receiving accounts. Kept static (not admin-configurable) so the
// details are always available on the checkout page without an extra fetch.
const BANKS: Bank[] = [
  {
    id: 'hsbc',
    tag: 'HSBC',
    swatch: 'bg-[#db0011]',
    insideUk: [
      { label: 'Account name', value: 'Ahmed Hesham Ibrahim Abdrabu Ibrahim', mono: false },
      { label: 'Account number', value: '64686063' },
      { label: 'Sort code', value: '40-16-64' },
    ],
    international: [
      { label: 'Account name', value: 'Ahmed Hesham Ibrahim Abdrabu Ibrahim', mono: false },
      { label: 'IBAN', value: 'GB57HBUK40166464686063' },
      { label: 'SWIFT / BIC', value: 'HBUKGB4196Y' },
    ],
  },
  {
    id: 'lloyds',
    tag: 'Lloyds',
    swatch: 'bg-[#006a4d]',
    insideUk: [
      { label: 'Account name', value: 'Ahmed Ibrahim', mono: false },
      { label: 'Account number', value: '84744968' },
      { label: 'Sort code', value: '77-48-17' },
    ],
    international: [
      { label: 'Account name', value: 'Ahmed Ibrahim', mono: false },
      { label: 'IBAN', value: 'GB22LOYD77481784744968' },
      { label: 'SWIFT / BIC', value: 'LOYDGB21W78' },
    ],
  },
  {
    id: 'barclays',
    tag: 'Barclays',
    swatch: 'bg-[#00aeef]',
    insideUk: [
      { label: 'Account name', value: 'AHMED IBRAHIM', mono: false },
      { label: 'Account number', value: '10274178' },
      { label: 'Sort code', value: '20-25-44' },
    ],
    international: [
      { label: 'Account name', value: 'AHMED IBRAHIM', mono: false },
      { label: 'Account number', value: '10274178' },
      { label: 'Sort code', value: '20-25-44' },
      { label: 'IBAN', value: 'GB90BUKB20254410274178' },
      { label: 'SWIFT / BIC', value: 'BUKBGB22' },
    ],
  },
];

type Scope = 'insideUk' | 'international';

export function UkBankTransfer() {
  const [open, setOpen] = useState(false);
  const [bankId, setBankId] = useState<string>(BANKS[0]!.id);
  const [scope, setScope] = useState<Scope>('insideUk');

  const bank = BANKS.find((b) => b.id === bankId) ?? BANKS[0]!;
  const fields = scope === 'insideUk' ? bank.insideUk : bank.international;

  return (
    <div className="overflow-hidden rounded-2xl border border-border bg-background-light/50">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center gap-3 px-4 py-3.5 text-left transition hover:bg-background-light"
      >
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <Landmark className="h-5 w-5" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-bold text-navy">Pay by UK bank transfer</span>
          <span className="block text-xs text-muted">HSBC · Lloyds · Barclays — inside UK &amp; international</span>
        </span>
        <ChevronDown className={cn('h-5 w-5 shrink-0 text-muted transition-transform', open && 'rotate-180')} />
      </button>

      {open ? (
        <div className="border-t border-border px-4 pb-4 pt-3.5">
          {/* Bank selector */}
          <div className="flex gap-1.5 rounded-xl border border-border bg-surface p-1">
            {BANKS.map((b) => {
              const active = b.id === bankId;
              return (
                <button
                  key={b.id}
                  type="button"
                  onClick={() => setBankId(b.id)}
                  className={cn(
                    'flex flex-1 items-center justify-center gap-1.5 rounded-lg px-2 py-2 text-xs font-bold transition',
                    active ? 'bg-primary text-white shadow-sm' : 'text-muted hover:text-navy',
                  )}
                >
                  <span className={cn('h-2 w-2 rounded-full', active ? 'bg-white/90' : b.swatch)} />
                  {b.tag}
                </button>
              );
            })}
          </div>

          {/* Inside-UK vs International */}
          <div className="mt-3 grid grid-cols-2 gap-1.5 rounded-xl border border-border bg-surface p-1">
            <ScopeButton
              active={scope === 'insideUk'}
              onClick={() => setScope('insideUk')}
              icon={<MapPin className="h-3.5 w-3.5" />}
              label="Inside the UK"
            />
            <ScopeButton
              active={scope === 'international'}
              onClick={() => setScope('international')}
              icon={<Globe2 className="h-3.5 w-3.5" />}
              label="International"
            />
          </div>

          <div className="mt-3 space-y-2">
            {fields.map((f) => (
              <CopyField key={`${bank.id}-${scope}-${f.label}`} label={f.label} value={f.value} mono={f.mono ?? true} />
            ))}
          </div>

          <p className="mt-3 flex items-start gap-2 rounded-xl bg-primary/5 px-3 py-2.5 text-xs leading-5 text-navy/80">
            <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
            <span>
              Use your <strong className="font-semibold text-navy">full name + course</strong> as the transfer reference,
              then email your receipt to activate access.
            </span>
          </p>
        </div>
      ) : null}
    </div>
  );
}

function ScopeButton({
  active,
  onClick,
  icon,
  label,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        'flex items-center justify-center gap-1.5 rounded-lg px-2 py-2 text-xs font-bold transition',
        active ? 'bg-primary text-white shadow-sm' : 'text-muted hover:text-navy',
      )}
    >
      {icon}
      {label}
    </button>
  );
}
