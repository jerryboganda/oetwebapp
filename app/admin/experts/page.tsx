'use client';

import { useMemo, useState, useCallback } from 'react';
import { useDebouncedEffect } from '@/hooks/use-debounced-effect';
import Link from 'next/link';
import {
  GraduationCap,
  MailPlus,
  Mic,
  Search,
  ShieldAlert,
  ShieldCheck,
  UserCog,
  Users,
} from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Pagination } from '@/components/ui/pagination';
import {
  inviteAdminUser,
  updateAdminUserStatus,
  deleteAdminUser,
  restoreAdminUser,
  triggerAdminUserPasswordReset,
  fetchAdminPrivateSpeakingTutors,
  createAdminPrivateSpeakingTutor,
} from '@/lib/api';
import { getAdminUsersPageData, getAdminUserDetailData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminUserRow, AdminUserDetail } from '@/lib/types/admin';

/* ─────────────────────── types ─────────────────────── */

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type ActiveModal = 'invite' | 'detail' | 'status' | 'ps-tutor' | null;

interface TutorProfileRef {
  id: string;
  expertUserId: string;
  displayName: string;
  isActive: boolean;
  totalSessions: number;
  averageRating: number;
}

/* ─────────────────────── component ─────────────────────── */

export default function ExpertManagementPage() {
  const { isAuthenticated, role } = useAdminAuth();

  /* list state */
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [experts, setExperts] = useState<AdminUserRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<string[]>([]);

  /* PS tutor cross-reference */
  const [tutorProfiles, setTutorProfiles] = useState<TutorProfileRef[]>([]);

  /* detail state */
  const [selectedExpert, setSelectedExpert] = useState<AdminUserDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  /* modal state */
  const [activeModal, setActiveModal] = useState<ActiveModal>(null);
  const [toast, setToast] = useState<ToastState>(null);

  /* invite form */
  const [inviteForm, setInviteForm] = useState({ name: '', email: '', professionId: 'nursing' });
  const [isInviting, setIsInviting] = useState(false);

  /* status change form */
  const [statusAction, setStatusAction] = useState<'suspend' | 'activate' | 'delete' | 'restore'>('suspend');
  const [statusReason, setStatusReason] = useState('');
  const [isProcessingAction, setIsProcessingAction] = useState(false);

  /* PS tutor creation form */
  const [psTutorForm, setPsTutorForm] = useState({ displayName: '', timezone: 'UTC', bio: '' });
  const [isCreatingTutor, setIsCreatingTutor] = useState(false);

  const selectedStatus = statusFilter[0];

  /* ─── data loaders ─── */

  const loadExperts = useCallback(async () => {
    setPageStatus('loading');
    try {
      const result = await getAdminUsersPageData({
        role: 'expert',
        status: selectedStatus,
        search: searchQuery || undefined,
        page,
        pageSize,
      });
      setExperts(result.items);
      setTotal(result.total);
      setPage(result.page);
      setPageSize(result.pageSize);
      setPageStatus(result.total > 0 ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
      setToast({ variant: 'error', message: 'Unable to load experts.' });
    }
  }, [page, pageSize, selectedStatus, searchQuery]);

  const loadTutorProfiles = useCallback(async () => {
    try {
      const raw = await fetchAdminPrivateSpeakingTutors();
      const items = Array.isArray(raw) ? raw : (raw as Record<string, unknown>)?.items as Record<string, unknown>[] ?? [];
      setTutorProfiles(
        items.map((t: Record<string, unknown>) => ({
          id: String(t.id ?? ''),
          expertUserId: String(t.expertUserId ?? ''),
          displayName: String(t.displayName ?? ''),
          isActive: t.isActive !== false,
          totalSessions: Number(t.totalSessions ?? 0),
          averageRating: Number(t.averageRating ?? 0),
        })),
      );
    } catch {
      /* non-critical — just means PS module isn't set up yet */
    }
  }, []);

  useDebouncedEffect(async ({ cancelled }) => {
    await loadExperts();
    if (!cancelled) await loadTutorProfiles();
  }, [loadExperts, loadTutorProfiles]);

  /* ─── computed ─── */

  const tutorByExpert = useMemo(() => {
    const map = new Map<string, TutorProfileRef>();
    for (const t of tutorProfiles) map.set(t.expertUserId, t);
    return map;
  }, [tutorProfiles]);

  const counts = useMemo(() => {
    const active = experts.filter((e) => e.status === 'active').length;
    const suspended = experts.filter((e) => e.status === 'suspended').length;
    const psTutors = experts.filter((e) => tutorByExpert.has(e.id)).length;
    return { total, active, suspended, psTutors };
  }, [experts, total, tutorByExpert]);


  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'suspended', label: 'Suspended' },
        { id: 'deleted', label: 'Deleted' },
      ],
    },
  ];

  /* ─── actions ─── */

  async function openExpertDetail(expert: AdminUserRow) {
    setDetailLoading(true);
    setActiveModal('detail');
    try {
      const detail = await getAdminUserDetailData(expert.id);
      setSelectedExpert(detail);
    } catch {
      setToast({ variant: 'error', message: 'Unable to load expert details.' });
      setActiveModal(null);
    } finally {
      setDetailLoading(false);
    }
  }

  async function handleInviteExpert() {
    setIsInviting(true);
    try {
      const result = await inviteAdminUser({
        name: inviteForm.name,
        email: inviteForm.email,
        role: 'expert',
        professionId: inviteForm.professionId || undefined,
      });
      await loadExperts();
      setActiveModal(null);
      setInviteForm({ name: '', email: '', professionId: 'nursing' });
      setToast({
        variant: 'success',
        message: `Invitation sent to ${result.email}. Setup expires ${new Date((result as Record<string, Record<string, string>>).invitation?.expiresAt).toLocaleString()}.`,
      });
    } catch {
      setToast({ variant: 'error', message: 'Unable to send invitation.' });
    } finally {
      setIsInviting(false);
    }
  }

  function openStatusAction(expert: AdminUserDetail, action: typeof statusAction) {
    setSelectedExpert(expert);
    setStatusAction(action);
    setStatusReason('');
    setActiveModal('status');
  }

  async function handleStatusAction() {
    if (!selectedExpert) return;
    setIsProcessingAction(true);
    try {
      if (statusAction === 'delete') {
        await deleteAdminUser(selectedExpert.id, { reason: statusReason || undefined });
      } else if (statusAction === 'restore') {
        await restoreAdminUser(selectedExpert.id, { reason: statusReason || undefined });
      } else {
        const newStatus = statusAction === 'suspend' ? 'suspended' : 'active';
        await updateAdminUserStatus(selectedExpert.id, { status: newStatus, reason: statusReason || undefined });
      }
      await loadExperts();
      setActiveModal(null);
      setToast({ variant: 'success', message: `Expert ${statusAction}d successfully.` });
    } catch {
      setToast({ variant: 'error', message: `Failed to ${statusAction} expert.` });
    } finally {
      setIsProcessingAction(false);
    }
  }

  async function handlePasswordReset(userId: string) {
    try {
      await triggerAdminUserPasswordReset(userId);
      setToast({ variant: 'success', message: 'Password reset email sent.' });
    } catch {
      setToast({ variant: 'error', message: 'Unable to trigger password reset.' });
    }
  }

  function openPsTutorModal(expert: AdminUserDetail) {
    setSelectedExpert(expert);
    setPsTutorForm({ displayName: expert.name, timezone: 'UTC', bio: '' });
    setActiveModal('ps-tutor');
  }

  async function handleCreatePsTutor() {
    if (!selectedExpert) return;
    setIsCreatingTutor(true);
    try {
      await createAdminPrivateSpeakingTutor({
        expertUserId: selectedExpert.id,
        displayName: psTutorForm.displayName,
        timezone: psTutorForm.timezone,
        bio: psTutorForm.bio || undefined,
      });
      await loadTutorProfiles();
      setActiveModal(null);
      setToast({ variant: 'success', message: `${psTutorForm.displayName} is now a Private Speaking tutor.` });
    } catch {
      setToast({ variant: 'error', message: 'Unable to create tutor profile. The expert may already have one.' });
    } finally {
      setIsCreatingTutor(false);
    }
  }

  /* ─── table columns ─── */

  const columns: Column<AdminUserRow>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'Expert',
        render: (expert) => {
          const tutor = tutorByExpert.get(expert.id);
          return (
            <div className="space-y-1">
              <button
                type="button"
                onClick={() => openExpertDetail(expert)}
                className="text-left font-medium text-primary hover:underline"
              >
                {expert.name}
              </button>
              <p className="text-sm text-muted">{expert.email}</p>
              {tutor ? (
                <span className="inline-flex items-center gap-1 text-xs text-emerald-600">
                  <Mic className="h-3 w-3" />
                  PS Tutor
                  {tutor.totalSessions > 0 && (
                    <span className="text-muted">
                      &middot; {tutor.totalSessions} sessions &middot; ★{tutor.averageRating.toFixed(1)}
                    </span>
                  )}
                </span>
              ) : null}
            </div>
          );
        },
      },
      {
        key: 'status',
        header: 'Status',
        render: (expert) => (
          <Badge variant={expert.status === 'active' ? 'success' : expert.status === 'deleted' ? 'danger' : 'muted'}>
            {expert.status}
          </Badge>
        ),
      },
      {
        key: 'lastLogin',
        header: 'Last Login',
        render: (expert) => (
          <span className="text-sm text-muted">
            {expert.lastLogin ? new Date(expert.lastLogin).toLocaleString() : 'Never'}
          </span>
        ),
      },
      {
        key: 'actions',
        header: '',
        render: (expert) => (
          <Button variant="secondary" size="sm" onClick={() => openExpertDetail(expert)}>
            Manage
          </Button>
        ),
      },
    ],
    [tutorByExpert],
  );

  const mobileCardRender = (expert: AdminUserRow) => {
    const tutor = tutorByExpert.get(expert.id);
    return (
      <div className="space-y-3">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <button
              type="button"
              onClick={() => openExpertDetail(expert)}
              className="truncate font-semibold text-primary hover:underline"
            >
              {expert.name}
            </button>
            <p className="truncate text-sm text-muted">{expert.email}</p>
            {tutor ? (
              <span className="mt-1 inline-flex items-center gap-1 text-xs text-emerald-600">
                <Mic className="h-3 w-3" />
                PS Tutor &middot; {tutor.totalSessions} sessions
              </span>
            ) : null}
          </div>
          <Badge variant={expert.status === 'active' ? 'success' : expert.status === 'deleted' ? 'danger' : 'muted'}>
            {expert.status}
          </Badge>
        </div>
        <div className="grid grid-cols-2 gap-3 text-sm">
          <div className="rounded-2xl bg-background-light px-3 py-2">
            <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Status</p>
            <p className="mt-1 font-medium text-navy">{expert.status}</p>
          </div>
          <div className="rounded-2xl bg-background-light px-3 py-2">
            <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Last login</p>
            <p className="mt-1 font-medium text-navy">
              {expert.lastLogin ? new Date(expert.lastLogin).toLocaleString() : 'Never'}
            </p>
          </div>
        </div>
        <div className="flex justify-end">
          <Button variant="secondary" size="sm" onClick={() => openExpertDetail(expert)}>
            Manage
          </Button>
        </div>
      </div>
    );
  };

  /* ─── filter handlers ─── */

  function handleFilterChange(_groupId: string, optionId: string) {
    setPage(1);
    setStatusFilter((current) => (current.includes(optionId) ? [] : [optionId]));
  }

  /* ─── guard ─── */

  if (!isAuthenticated || role !== 'admin') return null;

  /* ─── render ─── */

  return (
    <AdminRouteWorkspace role="main" aria-label="Expert management">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Expert Management"
        description="Manage expert reviewers, assign Private Speaking tutor roles, and oversee expert operations."
        actions={
          <Button onClick={() => setActiveModal('invite')} className="gap-2">
            <MailPlus className="h-4 w-4" />
            Invite Expert
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<GraduationCap className="h-10 w-10 text-muted" />}
            title="No experts found"
            description="Invite the first expert to start operating the review and tutoring workflows."
            action={{ label: 'Invite Expert', onClick: () => setActiveModal('invite') }}
          />
        }
      >
        {/* ─── summary cards ─── */}
        <div className="grid gap-4 md:grid-cols-4">
          <AdminRouteSummaryCard label="Total Experts" value={counts.total} hint="All expert accounts on the platform." icon={Users} />
          <AdminRouteSummaryCard label="Active" value={counts.active} hint="Experts currently available for work." icon={ShieldCheck} accent="emerald" />
          <AdminRouteSummaryCard label="Suspended" value={counts.suspended} hint="Experts with temporarily restricted access." icon={ShieldAlert} accent="amber" />
          <AdminRouteSummaryCard label="PS Tutors" value={counts.psTutors} hint="Experts with Private Speaking tutor profiles." icon={Mic} accent="blue" />
        </div>

        {/* ─── directory ─── */}
        <AdminRoutePanel title="Expert Directory" description="Search, filter, and manage expert accounts and their roles.">
          <div className="max-w-sm">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
              <Input
                placeholder="Search by name, email, or ID"
                value={searchQuery}
                onChange={(e) => {
                  setPage(1);
                  setSearchQuery(e.target.value);
                }}
                className="pl-9"
              />
            </div>
          </div>
          <FilterBar
            groups={filterGroups}
            selected={{ status: statusFilter }}
            onChange={handleFilterChange}
            onClear={() => {
              setPage(1);
              setStatusFilter([]);
              setSearchQuery('');
            }}
          />
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            itemLabel="expert"
            itemLabelPlural="experts"
          />
          <DataTable columns={columns} data={experts} keyExtractor={(e) => e.id} mobileCardRender={mobileCardRender} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {/* ────────── Invite Expert Modal ────────── */}
      <Modal
        open={activeModal === 'invite'}
        onClose={() => setActiveModal(null)}
        title="Invite Expert"
      >
        <div className="space-y-4 p-1">
          <Input
            label="Full name"
            value={inviteForm.name}
            onChange={(e) => setInviteForm({ ...inviteForm, name: e.target.value })}
            placeholder="Dr. Jane Smith"
          />
          <Input
            label="Email address"
            type="email"
            value={inviteForm.email}
            onChange={(e) => setInviteForm({ ...inviteForm, email: e.target.value })}
            placeholder="jane@example.com"
          />
          <Select
            label="Profession"
            value={inviteForm.professionId}
            onChange={(e) => setInviteForm({ ...inviteForm, professionId: e.target.value })}
            options={[
              { label: 'Nursing', value: 'nursing' },
              { label: 'Medicine', value: 'medicine' },
              { label: 'Dentistry', value: 'dentistry' },
              { label: 'Pharmacy', value: 'pharmacy' },
              { label: 'Physiotherapy', value: 'physiotherapy' },
              { label: 'Radiography', value: 'radiography' },
              { label: 'Dietetics', value: 'dietetics' },
              { label: 'Occupational Therapy', value: 'occupational-therapy' },
              { label: 'Speech Pathology', value: 'speech-pathology' },
              { label: 'Veterinary Science', value: 'veterinary-science' },
              { label: 'Podiatry', value: 'podiatry' },
              { label: 'Optometry', value: 'optometry' },
            ]}
          />
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setActiveModal(null)}>
              Cancel
            </Button>
            <Button disabled={!inviteForm.name || !inviteForm.email || isInviting} onClick={handleInviteExpert}>
              {isInviting ? 'Sending…' : 'Send Invitation'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* ────────── Expert Detail Modal ────────── */}
      <Modal
        open={activeModal === 'detail'}
        onClose={() => {
          setActiveModal(null);
          setSelectedExpert(null);
        }}
        title="Expert Details"
        size="lg"
      >
        {detailLoading ? (
          <div className="flex items-center justify-center py-12">
            <div className="h-6 w-6 animate-spin rounded-full border-2 border-primary border-t-transparent" />
          </div>
        ) : selectedExpert ? (
          <div className="space-y-6 p-1">
            {/* header */}
            <div className="flex items-start justify-between gap-4">
              <div>
                <h3 className="text-lg font-semibold text-navy">{selectedExpert.name}</h3>
                <p className="text-sm text-muted">{selectedExpert.email}</p>
                {selectedExpert.profession ? (
                  <p className="mt-1 text-sm capitalize text-muted">{selectedExpert.profession}</p>
                ) : null}
              </div>
              <Badge
                variant={
                  selectedExpert.status === 'active' ? 'success' : selectedExpert.status === 'deleted' ? 'danger' : 'muted'
                }
              >
                {selectedExpert.status}
              </Badge>
            </div>

            {/* specialties */}
            {selectedExpert.specialties && selectedExpert.specialties.length > 0 ? (
              <div>
                <p className="text-xs uppercase tracking-wide text-muted">Specialties</p>
                <div className="mt-1 flex flex-wrap gap-2">
                  {selectedExpert.specialties.map((s) => (
                    <Badge key={s} variant="default">
                      {s}
                    </Badge>
                  ))}
                </div>
              </div>
            ) : null}

            {/* stats grid */}
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <div className="rounded-2xl bg-background-light p-3">
                <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Reviews Graded</p>
                <p className="mt-1 text-xl font-bold text-navy">{selectedExpert.tasksGraded ?? 0}</p>
              </div>
              <div className="rounded-2xl bg-background-light p-3">
                <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Tasks Done</p>
                <p className="mt-1 text-xl font-bold text-navy">{selectedExpert.tasksCompleted ?? 0}</p>
              </div>
              <div className="rounded-2xl bg-background-light p-3">
                <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Last Login</p>
                <p className="mt-1 text-sm font-medium text-navy">
                  {selectedExpert.lastLogin ? new Date(selectedExpert.lastLogin).toLocaleDateString() : 'Never'}
                </p>
              </div>
              <div className="rounded-2xl bg-background-light p-3">
                <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Joined</p>
                <p className="mt-1 text-sm font-medium text-navy">
                  {selectedExpert.createdAt ? new Date(selectedExpert.createdAt).toLocaleDateString() : '—'}
                </p>
              </div>
            </div>

            {/* private speaking tutor badge */}
            {(() => {
              const tutor = tutorByExpert.get(selectedExpert.id);
              if (tutor) {
                return (
                  <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4">
                    <div className="flex items-center gap-2">
                      <Mic className="h-5 w-5 text-emerald-600" />
                      <p className="font-medium text-emerald-800">Private Speaking Tutor</p>
                    </div>
                    <p className="mt-1 text-sm text-emerald-700">
                      {tutor.totalSessions} sessions &middot; ★{tutor.averageRating.toFixed(1)} avg rating &middot;{' '}
                      {tutor.isActive ? 'Active' : 'Inactive'}
                    </p>
                    <Link
                      href="/admin/private-speaking"
                      className="mt-2 inline-block text-sm font-medium text-emerald-700 hover:underline"
                    >
                      Manage in Private Speaking →
                    </Link>
                  </div>
                );
              }
              return (
                <div className="rounded-2xl border border-border/60 bg-surface p-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <p className="font-medium text-navy">Private Speaking</p>
                      <p className="text-sm text-muted">This expert is not yet a Private Speaking tutor.</p>
                    </div>
                    {selectedExpert.status === 'active' ? (
                      <Button variant="secondary" size="sm" onClick={() => openPsTutorModal(selectedExpert)}>
                        Make Tutor
                      </Button>
                    ) : null}
                  </div>
                </div>
              );
            })()}

            {/* actions */}
            <div className="flex flex-wrap gap-3 border-t border-border/40 pt-4">
              {selectedExpert.availableActions.canSuspend && selectedExpert.status === 'active' ? (
                <Button variant="secondary" size="sm" onClick={() => openStatusAction(selectedExpert, 'suspend')}>
                  <ShieldAlert className="mr-1.5 h-4 w-4" />
                  Suspend
                </Button>
              ) : null}
              {selectedExpert.status === 'suspended' ? (
                <Button variant="secondary" size="sm" onClick={() => openStatusAction(selectedExpert, 'activate')}>
                  <ShieldCheck className="mr-1.5 h-4 w-4" />
                  Reactivate
                </Button>
              ) : null}
              {selectedExpert.availableActions.canDelete ? (
                <Button variant="destructive" size="sm" onClick={() => openStatusAction(selectedExpert, 'delete')}>
                  Delete
                </Button>
              ) : null}
              {selectedExpert.availableActions.canRestore ? (
                <Button variant="secondary" size="sm" onClick={() => openStatusAction(selectedExpert, 'restore')}>
                  Restore
                </Button>
              ) : null}
              {selectedExpert.availableActions.canTriggerPasswordReset ? (
                <Button variant="secondary" size="sm" onClick={() => handlePasswordReset(selectedExpert.id)}>
                  Reset Password
                </Button>
              ) : null}
              <Link href={`/admin/users/${selectedExpert.id}`}>
                <Button variant="secondary" size="sm">
                  <UserCog className="mr-1.5 h-4 w-4" />
                  Full User Profile
                </Button>
              </Link>
            </div>
          </div>
        ) : null}
      </Modal>

      {/* ────────── Status Action Modal ────────── */}
      <Modal
        open={activeModal === 'status'}
        onClose={() => setActiveModal(null)}
        title={`${statusAction.charAt(0).toUpperCase() + statusAction.slice(1)} Expert`}
      >
        <div className="space-y-4 p-1">
          <p className="text-sm text-muted">
            {statusAction === 'suspend' && 'Suspending will temporarily restrict this expert from accessing the platform.'}
            {statusAction === 'activate' && 'This will restore full access for this expert.'}
            {statusAction === 'delete' && 'Deleting removes this expert from active operations. This can be reversed.'}
            {statusAction === 'restore' && 'Restoring will reinstate this expert account.'}
          </p>
          {selectedExpert ? (
            <div className="rounded-xl bg-background-light p-3">
              <p className="font-medium text-navy">{selectedExpert.name}</p>
              <p className="text-sm text-muted">{selectedExpert.email}</p>
            </div>
          ) : null}
          <Textarea
            label="Reason (optional)"
            value={statusReason}
            onChange={(e) => setStatusReason(e.target.value)}
            placeholder="Internal note for audit trail…"
            rows={2}
          />
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setActiveModal(null)}>
              Cancel
            </Button>
            <Button
              variant={statusAction === 'delete' ? 'destructive' : 'primary'}
              disabled={isProcessingAction}
              onClick={handleStatusAction}
            >
              {isProcessingAction ? 'Processing…' : `${statusAction.charAt(0).toUpperCase() + statusAction.slice(1)}`}
            </Button>
          </div>
        </div>
      </Modal>

      {/* ────────── Create PS Tutor Modal ────────── */}
      <Modal
        open={activeModal === 'ps-tutor'}
        onClose={() => setActiveModal(null)}
        title="Create Private Speaking Tutor"
      >
        <div className="space-y-4 p-1">
          <p className="text-sm text-muted">
            Create a Private Speaking tutor profile for{' '}
            <span className="font-medium text-navy">{selectedExpert?.name}</span>. This allows them to accept 1-on-1
            speaking session bookings from learners.
          </p>
          <Input
            label="Display name"
            value={psTutorForm.displayName}
            onChange={(e) => setPsTutorForm({ ...psTutorForm, displayName: e.target.value })}
          />
          <Select
            label="Timezone"
            value={psTutorForm.timezone}
            onChange={(e) => setPsTutorForm({ ...psTutorForm, timezone: e.target.value })}
            options={[
              { label: 'UTC', value: 'UTC' },
              { label: 'Australia/Sydney', value: 'Australia/Sydney' },
              { label: 'Australia/Melbourne', value: 'Australia/Melbourne' },
              { label: 'Australia/Perth', value: 'Australia/Perth' },
              { label: 'Europe/London', value: 'Europe/London' },
              { label: 'America/New_York', value: 'America/New_York' },
              { label: 'America/Los_Angeles', value: 'America/Los_Angeles' },
              { label: 'Asia/Kolkata', value: 'Asia/Kolkata' },
              { label: 'Asia/Manila', value: 'Asia/Manila' },
              { label: 'Asia/Dubai', value: 'Asia/Dubai' },
            ]}
          />
          <Textarea
            label="Bio (optional)"
            value={psTutorForm.bio}
            onChange={(e) => setPsTutorForm({ ...psTutorForm, bio: e.target.value })}
            placeholder="Brief tutor description for learners…"
            rows={3}
          />
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="secondary" onClick={() => setActiveModal(null)}>
              Cancel
            </Button>
            <Button disabled={!psTutorForm.displayName || isCreatingTutor} onClick={handleCreatePsTutor}>
              {isCreatingTutor ? 'Creating…' : 'Create Tutor Profile'}
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
