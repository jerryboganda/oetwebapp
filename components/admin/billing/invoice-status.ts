import type { BadgeTone } from '@/components/admin/ui/badge';

/**
 * Badge tone + label for an invoice's status, source-aware: a "paid" invoice
 * backed only by manual proof or an admin grant (no gateway evidence) needs
 * to read differently from one confirmed by an actual payment gateway
 * webhook, so admins don't mistake "Paid" for "gateway-confirmed paid".
 *
 * Shared by app/admin/billing/page.tsx (Invoices tab) and
 * components/admin/billing/invoice-evidence-drawer.tsx so the two surfaces
 * can never disagree on how a given invoice is badged.
 */
export function invoiceStatusBadge(
  status: string,
  source?: string | null,
): { variant: BadgeTone; label: string } {
  const normalized = status.trim().toLowerCase();

  if (normalized === 'failed') return { variant: 'danger', label: status };
  if (normalized !== 'paid') return { variant: 'warning', label: status };

  if (source === 'manual_proof') return { variant: 'info', label: 'Paid (Manual proof)' };
  if (source === 'admin_grant') return { variant: 'warning', label: 'Paid (Admin grant)' };

  return { variant: 'success', label: 'Paid' };
}
