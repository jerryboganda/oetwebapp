'use client';

import { useEffect, useState } from 'react';
import { MessageCircle } from 'lucide-react';

import { useAuth } from '@/contexts/auth-context';
import {
  buildManualPaymentWhatsAppLink,
  fetchSupportWhatsApp,
  type SupportWhatsAppSettings,
} from '@/lib/billing/whatsapp';
import { cn } from '@/lib/utils';

/**
 * The proof-of-payment WhatsApp CTA. Every package carries one (spec 2026-07-15 §7),
 * whichever way it was paid for, so a learner always has a human channel for their
 * receipt. The number and message template are admin-configurable; the component
 * renders immediately against the built-in fallback and swaps in the configured
 * values when the settings read lands, so the link is never missing or dead.
 */
export interface SendProofOnWhatsAppButtonProps {
  /** Package / order name, prefilled into the message. */
  course: string;
  amount?: number | null;
  currency?: string | null;
  /** Transaction id, or the quote/order reference when payment has not happened yet. */
  reference?: string | null;
  /** Overrides the signed-in learner's name / email (e.g. a guest checkout). */
  name?: string | null;
  email?: string | null;
  label?: string;
  className?: string;
  /** `solid` is the WhatsApp-green CTA; `outline` sits next to a primary action. */
  variant?: 'solid' | 'outline';
}

export function SendProofOnWhatsAppButton({
  course,
  amount,
  currency,
  reference,
  name,
  email,
  label = 'Send proof on WhatsApp',
  className,
  variant = 'solid',
}: SendProofOnWhatsAppButtonProps) {
  const { user } = useAuth();
  const [settings, setSettings] = useState<SupportWhatsAppSettings | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetchSupportWhatsApp().then((value) => {
      if (!cancelled) setSettings(value);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const resolvedEmail = email ?? user?.email ?? '';
  const href = buildManualPaymentWhatsAppLink(
    {
      name: name ?? user?.displayName?.trim() ?? resolvedEmail,
      email: resolvedEmail,
      course: course || 'OET package',
      amount: amount ?? undefined,
      currency: currency ?? undefined,
      reference: reference ?? undefined,
    },
    { number: settings?.whatsAppNumber, template: settings?.whatsAppProofTemplate },
  );

  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className={cn(
        'inline-flex items-center justify-center gap-2 rounded-xl px-5 py-2.5 text-sm font-semibold transition',
        variant === 'solid'
          ? 'bg-[#25D366] text-white hover:brightness-95'
          : 'border border-[#25D366]/40 bg-[#25D366]/10 text-[#128C7E] hover:bg-[#25D366]/20',
        className,
      )}
    >
      <MessageCircle className="h-4 w-4" /> {label}
    </a>
  );
}
