'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, BookOpenText } from 'lucide-react';
import { adminCreateStrategyGuide } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  AdminStrategyGuideEditor,
  emptyStrategyGuideDraft,
} from '@/components/domain/strategies/admin-strategy-guide-editor';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import type { StrategyGuideUpsertPayload } from '@/lib/types/strategies';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function NewAdminStrategyPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const saveGuide = useCallback(async (payload: StrategyGuideUpsertPayload) => {
    setSaving(true);
    try {
      const guide = await adminCreateStrategyGuide(payload);
      setToast({ variant: 'success', message: 'Strategy guide created.' });
      setTimeout(() => router.push(`/admin/content/strategies/${encodeURIComponent(guide.id)}`), 300);
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Create failed.' });
    } finally {
      setSaving(false);
    }
  }, [router]);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="New strategy guide">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="New Strategy Guide"
        description="Author a guided article for authenticated learners."
        icon={BookOpenText}
        actions={
          <Link href="/admin/content/strategies">
            <Button variant="outline" className="gap-2">
              <ArrowLeft className="h-4 w-4" />
              Back
            </Button>
          </Link>
        }
      />

      <AdminRoutePanel>
        <AdminStrategyGuideEditor
          initial={emptyStrategyGuideDraft()}
          saving={saving}
          submitLabel="Create guide"
          onSave={saveGuide}
        />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
