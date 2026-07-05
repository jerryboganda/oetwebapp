'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowRight, Banknote, ExternalLink, Landmark, QrCode, Smartphone } from 'lucide-react';
import { cn } from '@/lib/utils';
import { CopyField } from './copy-field';

type Field = { label: string; value: string; mono?: boolean };

interface EgyptMethod {
  id: string;
  tab: string;
  icon: React.ReactNode;
  title: string;
  badge?: string;
  fields: Field[];
  instructions: string;
  link?: { label: string; href: string };
  qrSrc?: string;
}

// Mirrors the seeded `inside_egypt` methods on /billing/manual-payment. Shown here
// with the real handles/numbers so learners can copy them without leaving checkout;
// proof upload still happens on the manual-payment page.
const METHODS: EgyptMethod[] = [
  {
    id: 'instapay',
    tab: 'InstaPay',
    icon: <QrCode className="h-4 w-4" />,
    title: 'InstaPay — QR or link',
    fields: [{ label: 'InstaPay address', value: 'drahmedhesham_work@instapay' }],
    instructions:
      'Open InstaPay or your banking app, scan the QR or use the link, pay the exact amount, then upload your receipt.',
    link: { label: 'Open InstaPay link', href: 'https://ipn.eg/S/drahmedhesham_work/instapay/2wqbVW' },
    qrSrc: '/payment/instapay-qr.jpg',
  },
  {
    id: 'vodafone',
    tab: 'Vodafone',
    icon: <Smartphone className="h-4 w-4" />,
    title: 'Vodafone Cash / Fawry',
    fields: [
      { label: 'Wallet number', value: '+201062365271' },
      { label: 'Account name', value: 'Ahmed Hesham Ibrahim Abdrabu', mono: false },
    ],
    instructions:
      'Send the exact amount to the number above via Vodafone Cash or Fawry, then upload the confirmation screenshot.',
  },
  {
    id: 'qnb',
    tab: 'Bank',
    icon: <Landmark className="h-4 w-4" />,
    title: 'QNB Egypt bank transfer',
    badge: 'Inside Egypt only',
    fields: [
      { label: 'Account name', value: 'AHMED HISHAM IBRAHIM ABDRABO IBRAHIM', mono: false },
      { label: 'QNB account number', value: '1002506251368' },
    ],
    instructions: 'Transfer the exact amount to the QNB account above, then upload your proof of payment.',
  },
];

export function EgyptPaymentMethods({ egyptHref, disabled }: { egyptHref: string; disabled?: boolean }) {
  const [activeId, setActiveId] = useState<string>(METHODS[0]!.id);
  const active = METHODS.find((m) => m.id === activeId) ?? METHODS[0]!;

  return (
    <div>
      {/* Method tabs — keeps every option in view without a long scroll */}
      <div className="grid grid-cols-3 gap-1.5 rounded-xl border border-border bg-surface p-1">
        {METHODS.map((m) => {
          const isActive = m.id === activeId;
          return (
            <button
              key={m.id}
              type="button"
              onClick={() => setActiveId(m.id)}
              aria-pressed={isActive}
              className={cn(
                'flex items-center justify-center gap-1.5 rounded-lg px-2 py-2 text-xs font-bold transition',
                isActive ? 'bg-primary text-white shadow-sm' : 'text-muted hover:text-navy',
              )}
            >
              {m.icon}
              {m.tab}
            </button>
          );
        })}
      </div>

      <div className="mt-3 rounded-2xl border border-border bg-background-light/50 p-4">
        <div className="flex flex-wrap items-center gap-2">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            {active.icon}
          </span>
          <p className="text-sm font-bold text-navy">{active.title}</p>
          {active.badge ? (
            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700">
              {active.badge}
            </span>
          ) : null}
        </div>

        <div className="mt-3 space-y-2">
          {active.fields.map((f) => (
            <CopyField key={`${active.id}-${f.label}`} label={f.label} value={f.value} mono={f.mono ?? true} />
          ))}
        </div>

        {active.qrSrc ? (
          <div className="mt-3 flex justify-center">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={active.qrSrc}
              alt={`${active.title} QR code`}
              className="h-40 w-40 rounded-xl border border-border bg-white object-contain p-2"
            />
          </div>
        ) : null}

        {active.link ? (
          <a
            href={active.link.href}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-xl border border-primary/30 bg-primary/5 px-4 py-2.5 text-sm font-semibold text-primary transition hover:bg-primary/10"
          >
            <ExternalLink className="h-4 w-4" /> {active.link.label}
          </a>
        ) : null}

        <p className="mt-3 flex items-start gap-2 text-xs leading-5 text-muted">
          <Banknote className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
          {active.instructions}
        </p>
      </div>

      <Link
        href={disabled ? '#' : egyptHref}
        aria-disabled={disabled}
        tabIndex={disabled ? -1 : undefined}
        onClick={(e) => {
          if (disabled) e.preventDefault();
        }}
        className={cn(
          'mt-4 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-primary px-5 py-3 text-sm font-semibold text-white transition hover:bg-primary/90',
          disabled && 'pointer-events-none opacity-50',
        )}
      >
        I&apos;ve paid — upload proof &amp; activate <ArrowRight className="h-4 w-4" />
      </Link>
      <p className="mt-3 text-xs leading-5 text-muted">
        After paying with any method above, upload your receipt and we&apos;ll activate your access after a quick
        verification.
      </p>
    </div>
  );
}
