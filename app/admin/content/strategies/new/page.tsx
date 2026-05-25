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
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
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
    <>
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminSettingsLayout
        title="New Strategy Guide"
        description="Author a guided article for authenticated learners."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Strategies', href: '/admin/content/strategies' },
          { label: 'New' },
        ]}
        icon={<BookOpenText className="h-5 w-5" />}
        actions={
          <Button variant="outline" asChild startIcon={<ArrowLeft className="h-4 w-4" />}>
            <Link href="/admin/content/strategies">Back</Link>
          </Button>
        }
      >
        <SettingsSection title="Guide editor" description="Required fields must be completed before the guide can be published.">
          <AdminStrategyGuideEditor
            initial={emptyStrategyGuideDraft()}
            saving={saving}
            submitLabel="Create guide"
            onSave={saveGuide}
          />
        </SettingsSection>
      </AdminSettingsLayout>
    </>
  );
}
