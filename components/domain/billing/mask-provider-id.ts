/**
 * Mask a third-party payment-provider identifier (Stripe customer /
 * subscription / invoice / payment-intent ID, PayPal order ID, …) before
 * it is rendered to a learner.
 *
 * Provider IDs leak the gateway's internal namespace, so we never show
 * them in full on the learner surfaces. Admin / expert console keep the
 * raw IDs because they may need to reconcile with the dashboard, but
 * `app/billing/**` always passes them through `maskProviderId` first.
 *
 * Examples:
 *   "cus_NfFq2HxLkTjuPo"        → "cus_***juPo"
 *   "sub_1NXyHk2eZvKYlo2C..."   → "sub_***lo2C"
 *   "PAYID-ABC123XYZ987"        → "PAYI***Z987"
 *   ""                          → ""
 *   undefined                    → ""
 */
export function maskProviderId(value: string | null | undefined): string {
  if (!value) return '';
  const trimmed = String(value).trim();
  if (trimmed.length <= 8) {
    // Too short to mask meaningfully — render as `***` to avoid leaking
    // the whole token while still indicating that something is present.
    return '***';
  }

  // Preserve the conventional `cus_` / `sub_` / `pi_` / `in_` prefix when
  // present so admins can still tell what kind of object it is at a glance.
  const underscoreIdx = trimmed.indexOf('_');
  if (underscoreIdx > 0 && underscoreIdx <= 4) {
    const prefix = trimmed.slice(0, underscoreIdx + 1);
    const tail = trimmed.slice(-4);
    return `${prefix}***${tail}`;
  }

  const head = trimmed.slice(0, 4);
  const tail = trimmed.slice(-4);
  return `${head}***${tail}`;
}
