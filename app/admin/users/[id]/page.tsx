'use client';

import Link from 'next/link';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, Coins, Mail, Shield, User as UserIcon, UserLock } from 'lucide-react';
import {
  AdminRouteSectionHeader,
  AdminRouteSummaryCard,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { adjustAdminUserCredits, deleteAdminUser, restoreAdminUser, triggerAdminUserPasswordReset, updateAdminUserStatus } from '@/lib/api';
import { getAdminUserDetailData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminUserDetail } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function UserDetailPage() {
  const params = useParams<{ id: string }>();
  const userId = params?.id;
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [user, setUser] = useState<AdminUserDetail | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [isCreditModalOpen, setIsCreditModalOpen] = useState(false);
  const [isLifecycleModalOpen, setIsLifecycleModalOpen] = useState(false);
  const [lifecycleAction, setLifecycleAction] = useState<'delete' | 'restore' | null>(null);
  const [creditAmount, setCreditAmount] = useState('0');
  const [creditReason, setCreditReason] = useState('');
  const [lifecycleReason, setLifecycleReason] = useState('');
  const [isMutating, setIsMutating] = useState(false);
  const adjustCreditsButtonRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (!userId) return;
    let cancelled = false;

    async function loadUser() {
      setPageStatus('loading');
      try {
        const detail = await getAdminUserDetailData(userId);
        if (cancelled) return;

        setUser(detail);
        setPageStatus('success');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load this user profile.' });
        }
      }
    }

    loadUser();
    return () => {
      cancelled = true;
    };
  }, [userId]);

  const closeCreditModal = useCallback(() => {
    setIsCreditModalOpen(false);
    window.setTimeout(() => {
      adjustCreditsButtonRef.current?.focus();
      if (document.activeElement !== adjustCreditsButtonRef.current) {
        requestAnimationFrame(() => adjustCreditsButtonRef.current?.focus());
      }
    }, 50);
  }, []);

  async function reloadUser() {
    if (!userId) return;
    const detail = await getAdminUserDetailData(userId);
    setUser(detail);
    setPageStatus('success');
  }

  async function handlePasswordReset() {
    if (!user) return;
    setIsMutating(true);
    try {
      await triggerAdminUserPasswordReset(user.id);
      setToast({ variant: 'success', message: `Password reset initiated for ${user.email}.` });
      await reloadUser();
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to trigger a password reset.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleToggleStatus() {
    if (!user) return;
    setIsMutating(true);
    try {
      const nextStatus = user.status === 'active' ? 'suspended' : 'active';
      await updateAdminUserStatus(user.id, {
        status: nextStatus,
        reason: `Changed by admin from user detail view (${user.status} -> ${nextStatus})`,
      });
      await reloadUser();
      setToast({ variant: 'success', message: `Account ${nextStatus === 'active' ? 'reactivated' : 'suspended'} successfully.` });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to update account status.' });
    } finally {
      setIsMutating(false);
    }
  }

  function openLifecycleModal(action: 'delete' | 'restore') {
    setLifecycleAction(action);
    setLifecycleReason('');
    setIsLifecycleModalOpen(true);
  }

  function closeLifecycleModal() {
    setIsLifecycleModalOpen(false);
    setLifecycleAction(null);
    setLifecycleReason('');
  }

  async function handleLifecycleAction() {
    if (!user || !lifecycleAction) return;

    setIsMutating(true);
    try {
      const payload = lifecycleReason.trim().length > 0 ? { reason: lifecycleReason.trim() } : undefined;
      if (lifecycleAction === 'delete') {
        await deleteAdminUser(user.id, payload);
      } else {
        await restoreAdminUser(user.id, payload);
      }

      await reloadUser();
      closeLifecycleModal();
      setToast({
        variant: 'success',
        message: lifecycleAction === 'delete'
          ? 'Account deleted successfully.'
          : 'Account restored successfully.',
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: lifecycleAction === 'delete' ? 'Unable to delete this account.' : 'Unable to restore this account.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleAdjustCredits() {
    if (!user) return;
    setIsMutating(true);
    try {
      await adjustAdminUserCredits(user.id, {
        amount: Number(creditAmount || 0),
        reason: creditReason || undefined,
      });
      await reloadUser();
      closeCreditModal();
      setCreditAmount('0');
      setCreditReason('');
      setToast({ variant: 'success', message: 'Credit balance updated successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to adjust credits for this user.' });
    } finally {
      setIsMutating(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="User operations detail">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <Link href="/admin/users" className="inline-flex items-center gap-2 text-sm text-muted hover:text-primary">
        <ArrowLeft className="h-4 w-4" />
        Back to users
      </Link>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {user ? (
          <>
            <AdminRouteSectionHeader
              title={user.name}
              description="This operational profile is driven by the live admin user detail endpoint. Actions below mutate real account state and produce audit events."
              meta={user.id}
              actions={
                <>
                  {user.availableActions.canTriggerPasswordReset ? (
                    <Button variant="outline" onClick={handlePasswordReset} loading={isMutating}>
                      Reset Password
                    </Button>
                  ) : null}
                  {user.availableActions.canAdjustCredits ? (
                    <Button ref={adjustCreditsButtonRef} variant="outline" onClick={() => setIsCreditModalOpen(true)}>
                      Adjust Credits
                    </Button>
                  ) : null}
                  {user.availableActions.canSuspend ? (
                    <Button variant={user.status === 'active' ? 'destructive' : 'primary'} onClick={handleToggleStatus} loading={isMutating}>
                      {user.status === 'active' ? 'Suspend Account' : 'Reactivate Account'}
                    </Button>
                  ) : null}
                  <Link
                    href={`/admin/freeze?userId=${encodeURIComponent(user.id)}`}
                    className="inline-flex items-center justify-center rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-navy transition-colors hover:bg-gray-50"
                  >
                    Freeze Controls
                  </Link>
                  {user.availableActions.canDelete ? (
                    <Button variant="destructive" onClick={() => openLifecycleModal('delete')} loading={isMutating}>
                      Delete Account
                    </Button>
                  ) : null}
                  {user.availableActions.canRestore ? (
                    <Button onClick={() => openLifecycleModal('restore')} loading={isMutating}>
                      Restore Account
                    </Button>
                  ) : null}
                </>
              }
            />

            <div className="grid gap-6 lg:grid-cols-[280px,1fr]">
              <AdminRoutePanel title="Identity" description="Primary account identity and role context.">
                <div className="flex flex-col items-center gap-4 text-center">
                  <div className="flex h-20 w-20 items-center justify-center rounded-full bg-blue-50 text-blue-600">
                    <UserIcon className="h-8 w-8" />
                  </div>
                  <div className="space-y-2">
                    <p className="text-lg font-semibold text-navy">{user.name}</p>
                    <div className="flex flex-wrap items-center justify-center gap-2">
                      <Badge variant={user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default'}>
                        {user.role}
                      </Badge>
                      <Badge variant={user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'muted'}>
                        {user.status}
                      </Badge>
                    </div>
                  </div>
                  <div className="w-full space-y-3 text-left text-sm text-muted">
                    <div className="flex items-start gap-3">
                      <Mail className="mt-0.5 h-4 w-4 text-muted" />
                      <span>{user.email}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <UserLock className="mt-0.5 h-4 w-4 text-muted" />
                      <span>{user.authAccountId ?? 'No linked auth account'}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <Shield className="mt-0.5 h-4 w-4 text-muted" />
                      <span>{user.createdAt ? `Created ${new Date(user.createdAt).toLocaleString()}` : 'Creation date unavailable'}</span>
                    </div>
                  </div>
                </div>
              </AdminRoutePanel>

              <div className="space-y-6">
                <div className="grid gap-4 md:grid-cols-3">
                  <AdminRouteSummaryCard
                    label={user.role === 'expert' ? 'Tasks Graded' : 'Tasks Completed'}
                    value={user.role === 'expert' ? user.tasksGraded ?? 0 : user.tasksCompleted ?? 0}
                    icon={<Shield className="h-5 w-5" />}
                  />
                  <AdminRouteSummaryCard
                    label="Credit Balance"
                    value={user.creditBalance ?? 0}
                    icon={<Coins className="h-5 w-5" />}
                    tone={(user.creditBalance ?? 0) > 0 ? 'success' : 'default'}
                  />
                  <AdminRouteSummaryCard
                    label="Last Login"
                    value={user.lastLogin ? new Date(user.lastLogin).toLocaleDateString() : 'Never'}
                    icon={<UserLock className="h-5 w-5" />}
                  />
                </div>

                <AdminRoutePanel title="Operational Context" description="Only data the backend currently knows about this user is shown here.">
                  <div className="grid gap-4 md:grid-cols-2">
                    <div className="space-y-2">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Profession</p>
                      <p className="text-sm text-muted">{user.profession ?? 'Not assigned'}</p>
                    </div>
                    <div className="space-y-2">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Specialties</p>
                      <div className="flex flex-wrap gap-2">
                        {user.specialties && user.specialties.length > 0 ? (
                          user.specialties.map((specialty) => (
                            <Badge key={specialty} variant="info">
                              {specialty}
                            </Badge>
                          ))
                        ) : (
                          <span className="text-sm text-muted">No specialties recorded</span>
                        )}
                      </div>
                    </div>
                  </div>
                </AdminRoutePanel>

                <AdminRoutePanel title="Access Controls" description="Actions hidden here are not available in the current backend account model.">
                  <div className="grid gap-3 md:grid-cols-4">
                    <div className="rounded-[20px] border border-gray-200 bg-background-light p-4 shadow-sm">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Suspend / Reactivate</p>
                      <p className="mt-2 text-sm text-muted">{user.availableActions.canSuspend ? 'Supported' : 'Not supported for this account type'}</p>
                    </div>
                    <div className="rounded-[20px] border border-gray-200 bg-background-light p-4 shadow-sm">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Delete / Restore</p>
                      <p className="mt-2 text-sm text-muted">{user.availableActions.canDelete || user.availableActions.canRestore ? 'Supported' : 'Not supported for this account type'}</p>
                    </div>
                    <div className="rounded-[20px] border border-gray-200 bg-background-light p-4 shadow-sm">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Credit Adjustment</p>
                      <p className="mt-2 text-sm text-muted">{user.availableActions.canAdjustCredits ? 'Supported' : 'Not applicable for this account type'}</p>
                    </div>
                    <div className="rounded-[20px] border border-gray-200 bg-background-light p-4 shadow-sm">
                      <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Password Reset</p>
                      <p className="mt-2 text-sm text-muted">{user.availableActions.canTriggerPasswordReset ? 'Supported' : 'No linked auth account available'}</p>
                    </div>
                  </div>
                </AdminRoutePanel>
              </div>
            </div>
          </>
        ) : null}
      </AsyncStateWrapper>

      <Modal open={isCreditModalOpen} onClose={closeCreditModal} title="Adjust Credits">
        <div className="space-y-4 py-2">
          <Input
            label="Credit Adjustment"
            type="number"
            value={creditAmount}
            onChange={(event) => setCreditAmount(event.target.value)}
            hint="Use a negative number to remove credits when the current balance allows it."
          />
          <Input label="Reason" value={creditReason} onChange={(event) => setCreditReason(event.target.value)} />
          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <Button variant="outline" onClick={() => setIsCreditModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleAdjustCredits} loading={isMutating}>
              Save Adjustment
            </Button>
          </div>
        </div>
      </Modal>

      <Modal
        open={isLifecycleModalOpen}
        onClose={closeLifecycleModal}
        title={lifecycleAction === 'delete' ? 'Delete Account' : 'Restore Account'}
      >
        <div className="space-y-4 py-2">
          <div className="rounded-[20px] border border-gray-200 bg-background-light p-3 text-sm text-muted">
            {lifecycleAction === 'delete'
              ? 'Deleting the account removes it from active operation, blocks sign-in, and can be reversed later from this screen.'
              : 'Restoring the account removes the deleted marker and returns it to active operation.'}
          </div>
          <Input
            label="Reason"
            value={lifecycleReason}
            onChange={(event) => setLifecycleReason(event.target.value)}
            hint="Optional, but helpful for the audit trail."
          />
          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <Button variant="outline" onClick={closeLifecycleModal}>
              Cancel
            </Button>
            <Button variant={lifecycleAction === 'delete' ? 'destructive' : 'primary'} onClick={handleLifecycleAction} loading={isMutating}>
              {lifecycleAction === 'delete' ? 'Delete Account' : 'Restore Account'}
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
