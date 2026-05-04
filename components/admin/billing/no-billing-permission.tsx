'use client';

import { ShieldAlert } from 'lucide-react';
import { EmptyState } from '@/components/ui/empty-error';

export interface NoBillingPermissionProps {
  /** Optional override for the title (defaults to "Billing access required"). */
  title?: string;
  /** Required permission key, displayed for support troubleshooting. */
  requiredPermission?: string;
}

/**
 * 403-friendly empty state shown when the signed-in admin does not hold the
 * `billing:read` (or whichever) permission required to view a billing surface.
 *
 * We intentionally do NOT redirect — admins frequently land here via deep links
 * and a redirect would mask the missing-permission cause.
 */
export function NoBillingPermission({
  title = 'Billing access required',
  requiredPermission = 'billing:read',
}: NoBillingPermissionProps) {
  return (
    <div data-testid="no-billing-permission">
      <EmptyState
        icon={<ShieldAlert className="h-10 w-10 text-muted" aria-hidden="true" />}
        title={title}
        description={`Your admin account is missing the "${requiredPermission}" permission. Ask a system administrator to grant it from the Permissions page, then reload.`}
      />
    </div>
  );
}
