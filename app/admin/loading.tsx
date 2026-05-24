/**
 * Admin route-level loading boundary.
 * Shown by Next.js while any descendant of `/admin/*` is suspended (e.g.
 * during initial server-render or RSC streaming). Spec reference:
 * docs/admin-redesign/axelit-study/19-ERROR-EMPTY-STATES.md §2.20.
 *
 * Generic-shape skeleton — individual pages can render their own richer
 * loading state inside their tree.
 */

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { TableSkeleton } from '@/components/admin/ui/skeleton';

export default function AdminLoading() {
  return (
    <AdminPageShell>
      <TableSkeleton rows={5} columns={4} />
    </AdminPageShell>
  );
}
