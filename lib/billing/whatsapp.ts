/**
 * Builds the "notify us" WhatsApp deep link shown after a manual (inside-Egypt)
 * payment proof is submitted. wa.me links can only pre-fill TEXT — the screenshot
 * cannot be attached programmatically, so the message asks the learner to attach
 * it before sending. The admin always has the uploaded copy in the panel.
 */

// Platform support WhatsApp number, digits only (no '+'). Single source of truth;
// can later move to RuntimeSettings if it needs to be admin-configurable.
export const PLATFORM_WHATSAPP = '447961725989';

export interface ManualPaymentWhatsAppDetails {
  name: string;
  email: string;
  course: string;
  amount: number;
  currency: string;
  reference: string;
}

export function buildManualPaymentWhatsAppLink(details: ManualPaymentWhatsAppDetails): string {
  const lines = [
    "Hello OET team, I've submitted my payment proof on the platform.",
    '',
    `Name: ${details.name}`,
    `Registered email: ${details.email}`,
    `Package: ${details.course}`,
    `Amount: ${details.amount} ${details.currency}`,
    `Transaction ID: ${details.reference}`,
    '',
    "I'm attaching my payment screenshot — please verify and activate my access.",
  ];
  return `https://wa.me/${PLATFORM_WHATSAPP}?text=${encodeURIComponent(lines.join('\n'))}`;
}
