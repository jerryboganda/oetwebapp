export const SUPPORT_EMAIL = "support@edu80.app";

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

export function buildSupportWhatsAppLink(email?: string) {
  const message = [
    "Hello, I need help with my OET account setup.",
    email ? `Registered email: ${email}` : "",
  ]
    .filter(Boolean)
    .join(" ");

  return `https://wa.me/?text=${encodeURIComponent(message)}`;
}
