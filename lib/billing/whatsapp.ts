/**
 * Builds the "send your proof" WhatsApp deep link shown next to every package.
 * wa.me links can only pre-fill TEXT — the screenshot cannot be attached
 * programmatically, so the message asks the learner to attach it before sending.
 * The admin always has the uploaded copy in the panel.
 */

import { apiClient } from '@/lib/api';

/**
 * Fallback support number, digits only (no '+'). The live value is admin-editable
 * via RuntimeSettings (`support.whatsAppNumber`, seeded with this same number by
 * migration 20260729092000) and read through {@link fetchSupportWhatsApp}; this
 * constant only covers the case where that read fails, so the proof CTA never
 * renders as a dead link.
 */
export const PLATFORM_WHATSAPP = '447961725989';

/** `GET /v1/support/whatsapp` — anonymous; the number is printed publicly. */
export interface SupportWhatsAppSettings {
  whatsAppNumber: string | null;
  whatsAppProofTemplate: string | null;
}

export interface ManualPaymentWhatsAppDetails {
  name: string;
  email: string;
  course: string;
  /** Order total. Omitted before payment (the review screen quotes it separately). */
  amount?: number;
  currency?: string;
  /** Transaction id, or the order/quote reference when no transaction exists yet. */
  reference?: string;
}

export interface WhatsAppLinkOptions {
  /** Admin-configured number; falls back to {@link PLATFORM_WHATSAPP} when empty. */
  number?: string | null;
  /** Admin-configured message template; falls back to the built-in copy when empty. */
  template?: string | null;
}

/**
 * Placeholders an admin may use in `support.whatsAppProofTemplate`. Anything else is
 * left verbatim, so a template without placeholders is still a valid fixed message.
 */
const TEMPLATE_TOKENS = /\{(name|email|course|amount|currency|reference)\}/g;

/**
 * wa.me accepts digits only — an admin pasting "+44 7961 725989" would otherwise
 * produce a dead link. The backend normalises on write; this repeats it on read so a
 * value stored before that validation landed still dials.
 */
export function normalizeWhatsAppNumber(value: string | null | undefined): string | null {
  const digits = (value ?? '').replace(/\D/g, '');
  return digits.length > 0 ? digits : null;
}

let supportSettingsPromise: Promise<SupportWhatsAppSettings> | null = null;

/**
 * Reads the admin-configured support channel. Cached for the page session — the
 * number is rendered on several surfaces and changes at most a few times a year.
 * Never rejects: an unreachable settings read must not hide the proof CTA.
 */
export function fetchSupportWhatsApp(): Promise<SupportWhatsAppSettings> {
  supportSettingsPromise ??= apiClient
    .get<SupportWhatsAppSettings>('/v1/support/whatsapp')
    .catch(() => ({ whatsAppNumber: null, whatsAppProofTemplate: null }));
  return supportSettingsPromise;
}

/** Drops the cached read so a settings change is picked up without a reload. */
export function clearSupportWhatsAppCache(): void {
  supportSettingsPromise = null;
}

function defaultMessage(details: ManualPaymentWhatsAppDetails): string {
  return [
    "Hello OET team, I've submitted my payment proof on the platform.",
    '',
    `Name: ${details.name}`,
    `Registered email: ${details.email}`,
    `Package: ${details.course}`,
    details.amount != null ? `Amount: ${details.amount} ${details.currency ?? ''}`.trim() : '',
    details.reference ? `Transaction ID: ${details.reference}` : '',
    '',
    "I'm attaching my payment screenshot — please verify and activate my access.",
  ]
    .filter((line, index, lines) => line !== '' || lines[index - 1] !== '')
    .join('\n');
}

function renderTemplate(template: string, details: ManualPaymentWhatsAppDetails): string {
  return template.replace(TEMPLATE_TOKENS, (_match, token: string) => {
    switch (token) {
      case 'name': return details.name;
      case 'email': return details.email;
      case 'course': return details.course;
      case 'amount': return details.amount != null ? String(details.amount) : '';
      case 'currency': return details.currency ?? '';
      case 'reference': return details.reference ?? '';
      default: return '';
    }
  });
}

export function buildManualPaymentWhatsAppLink(
  details: ManualPaymentWhatsAppDetails,
  options?: WhatsAppLinkOptions,
): string {
  const number = normalizeWhatsAppNumber(options?.number) ?? PLATFORM_WHATSAPP;
  const template = options?.template?.trim();
  const text = template ? renderTemplate(template, details) : defaultMessage(details);
  return `https://wa.me/${number}?text=${encodeURIComponent(text)}`;
}
