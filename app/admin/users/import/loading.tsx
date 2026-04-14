import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Skeleton } from '@/components/ui/skeleton';

export default function BulkImportLoadingSkeleton() {
  return (
    <AdminRouteWorkspace>
      <Skeleton className="mb-6 h-5 w-32" />
      <AdminRouteSectionHeader title="Bulk Import Users" description="" />
      <AdminRoutePanel>
        <div className="space-y-6">
          <Skeleton className="h-16 w-full rounded-2xl" />
          <Skeleton className="h-40 w-full rounded-2xl" />
          <div className="flex justify-end">
            <Skeleton className="h-10 w-32 rounded-2xl" />
          </div>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
