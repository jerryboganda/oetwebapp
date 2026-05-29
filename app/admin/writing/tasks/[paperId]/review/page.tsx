'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { CheckCircle2, ShieldAlert, XCircle } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Writing', href: '/admin/writing' },
  { label: 'Tasks', href: '/admin/content/writing' },
  { label: 'Review' },
];
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  approvePublishWritingPaper,
  getContentPaper,
  getWritingStructure,
  rejectWritingPaper,
  type ContentPaperDto,
  type WritingAuthoringStructure,
} from '@/lib/content-upload-api';
import { analytics } from '@/lib/analytics';

type LoadStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const LETTER_TYPE_LABELS: Record<string, string> = {
  routine_referral: 'Routine referral',
  urgent_referral: 'Urgent referral',
  non_medical_referral: 'Non-medical referral',
  update_discharge: 'Update and discharge',
  update_referral_specialist_to_gp: 'Specialist update / referral',
  transfer_letter: 'Transfer letter',
};

function letterTypeLabel(v: string | null): string {
  if (!v) return 'Missing';
  return LETTER_TYPE_LABELS[v] ?? v;
}

function professionLabel(v: string | null): string {
  if (!v) return '-';
  return v.replace(/-/g, ' ').replace(/\b\w/g, (m) => m.toUpperCase());
}

export default function AdminWritingReviewPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canApprove = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [status, setStatus] = useState<LoadStatus>('loading');
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [structure, setStructure] = useState<WritingAuthoringStructure | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [pending, setPending] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');

  const load = useCallback(async () => {
    if (!paperId) return;
    setStatus('loading');
    try {
      const [paperDto, structureRes] = await Promise.all([
        getContentPaper(paperId),
        getWritingStructure(paperId).catch(() => null),
      ]);
      setPaper(paperDto);
      setStructure(structureRes?.structure ?? null);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load paper: ${(e as Error).message}` });
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const approve = useCallback(async () => {
    if (!paper || !canApprove) return;
    if (!confirm('Approve and publish this writing task? Learners will see it immediately.')) return;
    setPending(true);
    try {
      await approvePublishWritingPaper(paper.id);
      analytics.track('admin_writing_task_approved', { paperId: paper.id });
      setToast({ variant: 'success', message: 'Task approved and published.' });
      router.push('/admin/content/writing');
    } catch (e) {
      const err = e as Error & { detail?: { error?: string } };
      setToast({ variant: 'error', message: err.detail?.error || err.message });
    } finally {
      setPending(false);
    }
  }, [canApprove, paper, router]);

  const reject = useCallback(async () => {
    if (!paper || !canApprove) return;
    const reason = rejectReason.trim();
    if (!reason) {
      setToast({ variant: 'error', message: 'Rejection reason is required.' });
      return;
    }
    setPending(true);
    try {
      await rejectWritingPaper(paper.id, reason);
      analytics.track('admin_writing_task_rejected', { paperId: paper.id });
      setRejectOpen(false);
      setRejectReason('');
      setToast({ variant: 'success', message: 'Task rejected.' });
      router.push('/admin/content/writing');
    } catch (e) {
      const err = e as Error & { detail?: { error?: string } };
      setToast({ variant: 'error', message: err.detail?.error || err.message });
    } finally {
      setPending(false);
    }
  }, [canApprove, paper, rejectReason, router]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Writing task review" breadcrumbs={BREADCRUMBS}>
        <p className="text-sm text-admin-fg-muted">Admin access required.</p>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Writing task review"
      description="Read the task end-to-end, confirm the integrity acknowledgement metadata, and either approve & publish or reject with a documented reason."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Writing"
      icon={<ShieldAlert className="h-5 w-5" />}
      backHref="/admin/content/writing"
    >
      {status === 'loading' && (
        <Skeleton className="h-44 w-full rounded-admin-lg" />
      )}

      {status === 'success' && paper && (
        <>
          <SettingsSection title="Task metadata">
            <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
              <MetadataRow label="Title" value={paper.title} />
              <MetadataRow label="Slug" value={paper.slug} />
              <MetadataRow label="Profession" value={professionLabel(paper.professionId)} />
              <MetadataRow label="Letter type" value={letterTypeLabel(paper.letterType)} />
              <MetadataRow label="Status" value={
                <Badge variant={(
                  paper.status === 'Published' ? 'success'
                    : paper.status === 'InReview' ? 'warning'
                    : paper.status === 'Rejected' ? 'danger' : 'default'
                ) as any}>{paper.status}</Badge>
              } />
              <MetadataRow label="Duration" value={`${paper.estimatedDurationMinutes} min`} />
              <MetadataRow label="Source provenance" value={paper.sourceProvenance ?? '-'} />
              <MetadataRow label="Created" value={new Date(paper.createdAt).toLocaleString()} />
            </dl>
          </SettingsSection>

          <SettingsSection title="Integrity acknowledgement">
            {paper.integrityAcknowledgedByAdminId ? (
              <div className="flex items-start gap-3 rounded-admin border border-admin-success-tint-strong bg-admin-success-tint p-4 text-sm">
                <CheckCircle2 className="mt-0.5 h-5 w-5 text-admin-success" />
                <div>
                  <div className="font-semibold text-admin-success">
                    Acknowledged by {paper.integrityAcknowledgedByAdminId}
                  </div>
                  <div className="text-xs text-admin-fg-muted">
                    {paper.integrityAcknowledgedAt
                      ? new Date(paper.integrityAcknowledgedAt).toLocaleString()
                      : '-'}
                  </div>
                </div>
              </div>
            ) : (
              <div className="flex items-start gap-3 rounded-admin border border-admin-warning-tint-strong bg-admin-warning-tint p-4 text-sm">
                <ShieldAlert className="mt-0.5 h-5 w-5 text-admin-warning" />
                <div>
                  <div className="font-semibold text-admin-warning">Integrity acknowledgement missing</div>
                  <div className="text-xs text-admin-fg-muted">
                    This task was created before the integrity gate or via an automated import path. Review the source provenance carefully before approving.
                  </div>
                </div>
              </div>
            )}
          </SettingsSection>

          {structure && (
            <>
              {structure.taskPrompt && (
                <SettingsSection title="Task prompt">
                  <p className="whitespace-pre-wrap text-sm text-admin-fg-strong">{structure.taskPrompt}</p>
                </SettingsSection>
              )}
              {structure.caseNotes && (
                <SettingsSection title="Case notes">
                  <pre className="whitespace-pre-wrap rounded-admin bg-admin-bg-subtle p-4 text-sm leading-6 text-admin-fg-strong">
                    {structure.caseNotes}
                  </pre>
                </SettingsSection>
              )}
              {structure.modelAnswerText && (
                <SettingsSection title="Model answer">
                  <pre className="whitespace-pre-wrap rounded-admin bg-admin-bg-subtle p-4 text-sm leading-6 text-admin-fg-strong">
                    {structure.modelAnswerText}
                  </pre>
                </SettingsSection>
              )}
            </>
          )}

          {paper.status === 'InReview' && canApprove && (
            <SettingsSection title="Actions">
              <div className="flex flex-wrap items-center gap-3">
                <Button variant="primary" disabled={pending} onClick={() => void approve()}>
                  <CheckCircle2 className="mr-1 h-4 w-4" /> Approve &amp; publish
                </Button>
                <Button variant="ghost" disabled={pending} onClick={() => setRejectOpen(true)}>
                  <XCircle className="mr-1 h-4 w-4" /> Reject
                </Button>
              </div>
            </SettingsSection>
          )}

          {paper.status !== 'InReview' && (
            <SettingsSection title="Status">
              <p className="text-sm text-admin-fg-muted">
                {paper.status === 'Draft'
                  ? 'This task is still a Draft. Use the listing page to submit it for review first.'
                  : paper.status === 'Published'
                    ? 'This task is already published. Archive it from the listing to remove from learners.'
                    : `This task is ${paper.status}. No review actions available.`}
              </p>
              {paper.status === 'Draft' && canWriteContent && (
                <Link
                  href="/admin/content/writing"
                  className="mt-3 inline-flex min-h-9 items-center rounded-admin bg-[var(--admin-primary)] px-3 py-2 text-sm font-semibold text-[var(--admin-primary-fg)] hover:bg-[var(--admin-primary-hover)]"
                >
                  Back to listing
                </Link>
              )}
            </SettingsSection>
          )}
        </>
      )}

      <Modal open={rejectOpen} onClose={() => setRejectOpen(false)} title="Reject writing task">
        <div className="space-y-3 p-4">
          <p className="text-sm text-muted">
            The reason is recorded on the audit trail and is visible to the author.
          </p>
          <textarea
            className="w-full min-h-[120px] rounded-lg border border-border p-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            placeholder="e.g. Case notes are incomplete; model answer needs to address the requested follow-up explicitly."
            maxLength={500}
          />
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setRejectOpen(false)}>Cancel</Button>
            <Button variant="destructive" disabled={pending} onClick={() => void reject()}>
              Reject task
            </Button>
          </div>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}

function MetadataRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1 rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
      <dt className="text-[10px] font-bold uppercase tracking-[0.14em] text-admin-fg-muted">{label}</dt>
      <dd className="text-sm text-admin-fg-strong">{value}</dd>
    </div>
  );
}
