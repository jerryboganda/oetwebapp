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
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
    <AdminRouteWorkspace role="main" aria-label="Strategy guide editor">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title={guide?.title ?? 'Strategy Guide'}
        description={guide?.summary ?? 'Edit strategy guide content and publish readiness.'}
        icon={BookOpenText}
        actions={
          <>
            <Link href="/admin/content/strategies">
              <Button variant="outline" className="gap-2">
                <ArrowLeft className="h-4 w-4" />
                Back
              </Button>
            </Link>
            <Button
              type="button"
              className="gap-2"
              loading={publishing}
              disabled={!guide || guide.status === 'active' || guide.status === 'archived'}
              onClick={() => void publishGuide()}
            >
              <Rocket className="h-4 w-4" />
              Publish
            </Button>
            <Button
              type="button"
              variant="outline"
              className="gap-2"
              loading={archiving}
              disabled={!guide || guide.status === 'archived'}
              onClick={() => void archiveGuide()}
            >
              <Archive className="h-4 w-4" />
              Archive
            </Button>
          </>
        }
      />

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
            <div className="grid gap-4 md:grid-cols-4">
              <AdminRouteSummaryCard label="State" value={statusLabel(guide.status)} tone={statusTone(guide.status) === 'success' ? 'success' : guide.status === 'archived' ? 'danger' : 'warning'} />
              <AdminRouteSummaryCard label="Subtest" value={guide.subtestCode ?? 'All'} />
              <AdminRouteSummaryCard label="Read Time" value={`${guide.readingTimeMinutes} min`} />
              <AdminRouteSummaryCard label="Preview" value={guide.isPreviewEligible ? 'Eligible' : 'Locked'} />
            </div>

            {validation?.canPublish ? (
              <InlineAlert variant="success" title="Ready to publish">
                <span className="inline-flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4" />
                  Required fields are complete.
                </span>
              </InlineAlert>
            ) : null}

            <AdminRoutePanel
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
            </AdminRoutePanel>
          </div>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
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
