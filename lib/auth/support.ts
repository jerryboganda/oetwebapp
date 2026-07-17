export const SUPPORT_EMAIL = "support@oetwithdrhesham.co.uk";

export function buildSupportMailto(email?: string) {
  const subject = "Need help with my OET account";
  const lines = [
    "Hello Support Team,",
    "",
    "I have just completed the account registration flow and need assistance.",
    email ? `Registered email: ${email}` : "",
    "",
    "Please help me with my OET workspace setup.",
  ].filter(Boolean);

  return `mailto:${SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(
    lines.join("\n")
  )}`;
}

export const SUPPORT_PHONE = "+44 7961 725989";
export const SUPPORT_PHONE_E164 = "447961725989";

export function buildSupportWhatsAppLink(email?: string) {
  const message = [
    "Hello, I need help with my OET account setup.",
    email ? `Registered email: ${email}` : "",
  ]
    .filter(Boolean)
    .join(" ");

  return `https://wa.me/${SUPPORT_PHONE_E164}?text=${encodeURIComponent(message)}`;
}
