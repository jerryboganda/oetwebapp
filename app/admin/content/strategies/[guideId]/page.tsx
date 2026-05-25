'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { Archive, ArrowLeft, BookOpenText, CheckCircle2, Rocket } from 'lucide-react';
import {
  adminArchiveStrategyGuide,
  adminGetStrategyGuide,
  adminPublishStrategyGuide,
  adminUpdateStrategyGuide,
  adminValidateStrategyGuidePublish,
} from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type {
  StrategyGuideAdminItem,
  StrategyGuidePublishValidation,
  StrategyGuideUpsertPayload,
} from '@/lib/types/strategies';
import {
  AdminStrategyGuideEditor,
  strategyGuideToDraft,
} from '@/components/domain/strategies/admin-strategy-guide-editor';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge, type BadgeProps } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminStrategyDetailPage() {
  const params = useParams<{ guideId?: string | string[] }>();
  const guideIdParam = params?.guideId;
  const guideId = Array.isArray(guideIdParam) ? (guideIdParam[0] ?? '') : (guideIdParam ?? '');
  const { isAuthenticated, role } = useAdminAuth();

  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [guide, setGuide] = useState<StrategyGuideAdminItem | null>(null);
  const [validation, setValidation] = useState<StrategyGuidePublishValidation | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [archiving, setArchiving] = useState(false);

  const loadGuide = useCallback(async () => {
    if (!guideId) {
      setPageStatus('empty');
      return;
    }

    setPageStatus('loading');
    try {
      const [guideData, validationData] = await Promise.all([
        adminGetStrategyGuide(guideId),
        adminValidateStrategyGuidePublish(guideId).catch(() => null),
      ]);
      setGuide(guideData);
      setValidation(validationData);
      setPageStatus('success');
    } catch (error) {
      setPageStatus('error');
      setToast({ variant: 'error', message: (error as Error).message || 'Failed to load strategy guide.' });
    }
  }, [guideId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => void loadGuide());
  }, [isAuthenticated, loadGuide, role]);

  const saveGuide = useCallback(async (payload: StrategyGuideUpsertPayload) => {
    if (!guideId) return;
    setSaving(true);
    try {
      const updated = await adminUpdateStrategyGuide(guideId, payload);
      const nextValidation = await adminValidateStrategyGuidePublish(guideId).catch(() => null);
      setGuide(updated);
      setValidation(nextValidation);
      setToast({ variant: 'success', message: 'Strategy guide saved.' });
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Save failed.' });
    } finally {
      setSaving(false);
    }
  }, [guideId]);

  async function publishGuide() {
    if (!guideId) return;
    setPublishing(true);
    try {
      const result = await adminPublishStrategyGuide(guideId);
      setValidation(result.validation);
      if (!result.published) {
        const firstError = result.validation.errors[0]?.message ?? 'Publish validation failed.';
        setToast({ variant: 'error', message: firstError });
        return;
      }
      if (result.guide) setGuide(result.guide);
      setToast({ variant: 'success', message: 'Strategy guide published.' });
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Publish failed.' });
    } finally {
      setPublishing(false);
    }
  }

  async function archiveGuide() {
    if (!guideId || !window.confirm('Archive this strategy guide? Learners will no longer see it.')) return;
    setArchiving(true);
    try {
      const archived = await adminArchiveStrategyGuide(guideId);
      setGuide(archived);
      setToast({ variant: 'success', message: 'Strategy guide archived.' });
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Archive failed.' });
    } finally {
      setArchiving(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <>
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminSettingsLayout
        title={guide?.title ?? 'Strategy Guide'}
        description={guide?.summary ?? 'Edit strategy guide content and publish readiness.'}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Strategies', href: '/admin/content/strategies' },
          { label: guide?.title ?? 'Guide' },
        ]}
        icon={<BookOpenText className="h-5 w-5" />}
        actions={
          <>
            <Button variant="outline" asChild startIcon={<ArrowLeft className="h-4 w-4" />}>
              <Link href="/admin/content/strategies">Back</Link>
            </Button>
            <Button
              type="button"
              loading={publishing}
              disabled={!guide || guide.status === 'active' || guide.status === 'archived'}
              onClick={() => void publishGuide()}
              startIcon={!publishing ? <Rocket className="h-4 w-4" /> : undefined}
            >
              Publish
            </Button>
            <Button
              type="button"
              variant="outline"
              loading={archiving}
              disabled={!guide || guide.status === 'archived'}
              onClick={() => void archiveGuide()}
              startIcon={!archiving ? <Archive className="h-4 w-4" /> : undefined}
            >
              Archive
            </Button>
          </>
        }
      >
        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => void loadGuide()}
          errorMessage="Could not load this strategy guide."
          emptyContent={
            <InlineAlert variant="warning">
              Strategy guide route parameters are missing.
            </InlineAlert>
          }
        >
          {guide ? (
            <div className="space-y-6">
              <KpiStrip>
                <KpiTile label="State" value={statusLabel(guide.status)} tone={statusTone(guide.status) === 'success' ? 'success' : guide.status === 'archived' ? 'danger' : 'warning'} />
                <KpiTile label="Subtest" value={guide.subtestCode ?? 'All'} />
                <KpiTile label="Read Time" value={`${guide.readingTimeMinutes} min`} />
                <KpiTile label="Preview" value={guide.isPreviewEligible ? 'Eligible' : 'Locked'} />
              </KpiStrip>

              {validation?.canPublish ? (
                <InlineAlert variant="success" title="Ready to publish">
                  <span className="inline-flex items-center gap-2">
                    <CheckCircle2 className="h-4 w-4" />
                    Required fields are complete.
                  </span>
                </InlineAlert>
              ) : null}

              <SettingsSection
                title="Guide Editor"
                description="Updates save as draft metadata until the guide is published."
                actions={<Badge variant={statusTone(guide.status)}>{statusLabel(guide.status)}</Badge>}
              >
                <AdminStrategyGuideEditor
                  key={`${guide.id}-${guide.updatedAt}`}
                  initial={strategyGuideToDraft(guide)}
                  saving={saving}
                  validation={validation}
                  onSave={saveGuide}
                />
              </SettingsSection>
            </div>
          ) : null}
        </AsyncStateWrapper>
      </AdminSettingsLayout>
    </>
  );
}

function statusLabel(status: string) {
  if (status === 'active') return 'Published';
  return status.charAt(0).toUpperCase() + status.slice(1);
}

function statusTone(status: string): BadgeProps['variant'] {
  if (status === 'active') return 'success';
  if (status === 'archived') return 'danger';
  return 'warning';
}
