import { redirect } from 'next/navigation';

// Wallet top-up tiers are now managed in the unified Pricing hub. This route is
// kept so existing bookmarks / deep links (e.g. audit-log references) still resolve.
export default function AdminWalletTiersRedirectPage() {
  return redirect('/admin/billing/pricing?tab=wallet');
}
