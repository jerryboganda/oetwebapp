'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  Coins,
  KeyRound,
  Lock,
  LogOut,
  Mail,
  Mic,
  RefreshCcw,
  Shield,
  ShieldCheck,
  User as UserIcon,
  UserLock,
} from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AdminQuickAction } from '@/components/domain/admin-quick-action';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import {
  adjustAdminUserCredits,
  deleteAdminUser,
  fetchAdminPermissions,
  resendAdminUserInvite,
  restoreAdminUser,
  revokeAdminUserSessions,
  triggerAdminUserPasswordReset,
  unlockAdminUser,
  updateAdminUserStatus,
} from '@/lib/api';
import { getAdminUserDetailData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminPermissionGrant, AdminUserDetail } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

function formatDate(value: string | null | undefined, fallback = '-') {
  if (!value) return fallback;
  try {
    return new Date(value).toLocaleString();
  } catch {
    return fallback;
  }
}

// UI translation: backend role "expert" → operator-facing "tutor".
function uiRoleLabel(role: string) {
  return role === 'expert' ? 'tutor' : role;
}

export default function UserDetailPage() {
  const params = useParams<{ id: string }>();
  const userId = params?.id ?? '';
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
  const [adminPermissions, setAdminPermissions] = useState<AdminPermissionGrant[] | null>(null);

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

        // For admins, also load granular permissions for the inline summary card.
        if (detail.role === 'admin') {
          try {
            const perms = (await fetchAdminPermissions(detail.id)) as { permissions?: AdminPermissionGrant[] };
            if (!cancelled) setAdminPermissions(perms.permissions ?? []);
          } catch {
            if (!cancelled) setAdminPermissions([]);
          }
        } else {
          setAdminPermissions(null);
        }
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
    setCreditAmount('0');
    setCreditReason('');
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

  async function handleResendInvite() {
    if (!user) return;
    setIsMutating(true);
    try {
      await resendAdminUserInvite(user.id);
      setToast({ variant: 'success', message: `Invitation resent to ${user.email}.` });
      await reloadUser();
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to resend the invitation.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleForceSignOut() {
    if (!user) return;
    setIsMutating(true);
    try {
      const result = await revokeAdminUserSessions(user.id);
      const revokedCount = result.revoked;
      setToast({
        variant: 'success',
        message: revokedCount > 0
          ? `Revoked ${revokedCount} active session(s) for ${user.email}.`
          : 'No active sessions to revoke.',
      });
      await reloadUser();
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to revoke sessions.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleUnlock() {
    if (!user) return;
    setIsMutating(true);
    try {
      await unlockAdminUser(user.id);
      setToast({ variant: 'success', message: 'Account lockout cleared.' });
      await reloadUser();
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to unlock this account.' });
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
      setToast({ variant: 'success', message: 'Credit balance updated successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to adjust credits for this user.' });
    } finally {
      setIsMutating(false);
    }
  }

  const subscriptionLabel = useMemo(() => {
    if (!user?.subscription) return null;
    const sub = user.subscription;
    return `${sub.planName} - ${sub.priceAmount} ${sub.currency}/${sub.interval}`;
  }, [user?.subscription]);

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
            <header className="flex flex-col gap-3 border-b border-border/60 pb-4 md:flex-row md:items-end md:justify-between">
              <div className="space-y-1">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">User profile</p>
                <h1 className="text-2xl font-semibold text-navy">{user.name}</h1>
                <p className="font-mono text-xs text-muted">{user.id}</p>
                <div className="flex flex-wrap items-center gap-2 pt-1">
                  <Badge variant={user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default'}>{uiRoleLabel(user.role)}</Badge>
                  <Badge variant={user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'muted'}>{user.status}</Badge>
                  {user.security?.lockedOut ? <Badge variant="danger">Locked</Badge> : null}
                  {user.security?.mfaEnabled ? <Badge variant="success">MFA</Badge> : null}
                  {user.security?.emailVerifiedAt ? <Badge variant="info">Email verified</Badge> : <Badge variant="muted">Email unverified</Badge>}
                </div>
              </div>
              <div className="flex flex-wrap gap-2">
                {user.availableActions.canTriggerPasswordReset ? (
                  <Button variant="outline" onClick={handlePasswordReset} loading={isMutating} className="gap-2">
                    <KeyRound className="h-4 w-4" />
                    Reset Password
                  </Button>
                ) : null}
                {user.availableActions.canResendInvite ? (
                  <Button variant="outline" onClick={handleResendInvite} loading={isMutating} className="gap-2">
                    <RefreshCcw className="h-4 w-4" />
                    Resend Invite
                  </Button>
                ) : null}
                {user.availableActions.canForceSignOut ? (
                  <Button variant="outline" onClick={handleForceSignOut} loading={isMutating} className="gap-2">
                    <LogOut className="h-4 w-4" />
                    Force Sign-out
                  </Button>
                ) : null}
                {user.availableActions.canUnlock ? (
                  <Button variant="outline" onClick={handleUnlock} loading={isMutating} className="gap-2">
                    <Lock className="h-4 w-4" />
                    Unlock Account
                  </Button>
                ) : null}
                {user.availableActions.canAdjustCredits ? (
                  <Button ref={adjustCreditsButtonRef} variant="outline" onClick={() => setIsCreditModalOpen(true)} className="gap-2">
                    <Coins className="h-4 w-4" />
                    Adjust Credits
                  </Button>
                ) : null}
                {user.availableActions.canSuspend ? (
                  <Button variant={user.status === 'active' ? 'destructive' : 'primary'} onClick={handleToggleStatus} loading={isMutating}>
                    {user.status === 'active' ? 'Suspend' : 'Reactivate'}
                  </Button>
                ) : null}
                <AdminQuickAction
                  href={`/admin/freeze?userId=${encodeURIComponent(user.id)}`}
                  label="Freeze Controls"
                  className="w-auto"
                />
                {user.availableActions.canDelete ? (
                  <Button variant="destructive" onClick={() => openLifecycleModal('delete')} loading={isMutating}>Delete</Button>
                ) : null}
                {user.availableActions.canRestore ? (
                  <Button onClick={() => openLifecycleModal('restore')} loading={isMutating}>Restore</Button>
                ) : null}
              </div>
            </header>

            <div className="grid gap-6 lg:grid-cols-[280px,1fr]">
              <AdminRoutePanel title="Identity" description="Primary account identity and role context.">
                <div className="flex flex-col items-center gap-4 text-center">
                  <div className="flex h-20 w-20 items-center justify-center rounded-full bg-lavender text-primary">
                    <UserIcon className="h-8 w-8" />
                  </div>
                  <div className="w-full space-y-3 text-left text-sm text-muted">
                    <div className="flex items-start gap-3">
                      <Mail className="mt-0.5 h-4 w-4 text-muted" />
                      <span className="break-all">{user.email}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <UserLock className="mt-0.5 h-4 w-4 text-muted" />
                      <span className="break-all">{user.authAccountId ?? 'No linked auth account'}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <Shield className="mt-0.5 h-4 w-4 text-muted" />
                      <span>Created {formatDate(user.createdAt, 'unknown')}</span>
                    </div>
                    {user.profession ? (
                      <div className="flex items-start gap-3">
                        <ShieldCheck className="mt-0.5 h-4 w-4 text-muted" />
                        <span>Profession: {user.profession}</span>
                      </div>
                    ) : null}
                    {user.specialties && user.specialties.length > 0 ? (
                      <div className="flex flex-wrap gap-1.5 pt-1">
                        {user.specialties.map((s) => (
                          <Badge key={s} variant="info">{s}</Badge>
                        ))}
                      </div>
                    ) : null}
                  </div>
                </div>
              </AdminRoutePanel>

              <div className="space-y-6">
                <div className="grid gap-4 md:grid-cols-3">
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{user.role === 'expert' ? 'Tasks Graded' : 'Tasks Completed'}</p>
                    <p className="mt-1 text-2xl font-semibold text-navy">{(user.role === 'expert' ? user.tasksGraded : user.tasksCompleted) ?? 0}</p>
                  </div>
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Credit Balance</p>
                    <p className="mt-1 text-2xl font-semibold text-navy">{user.creditBalance ?? 0}</p>
                  </div>
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Last Login</p>
                    <p className="mt-1 text-sm font-semibold text-navy">{formatDate(user.lastLogin, 'Never')}</p>
                  </div>
                </div>

                {user.role === 'admin' ? (
                  <AdminRoutePanel
                    title="Permissions"
                    description="Granular admin permissions for this account. Edit in the Admins & Permissions tab."
                  >
                    {adminPermissions === null ? (
                      <p className="text-sm text-muted">Loading permissions…</p>
                    ) : adminPermissions.length === 0 ? (
                      <div className="flex flex-wrap items-center gap-3">
                        <p className="text-sm text-muted">No granular permissions granted yet.</p>
                        <Link
                          href="/admin/users?tab=admins"
                          className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-navy hover:bg-background-light"
                        >
                          <KeyRound className="h-3.5 w-3.5" />
                          Manage permissions
                        </Link>
                      </div>
                    ) : (
                      <div className="space-y-3">
                        <div className="flex flex-wrap gap-1.5">
                          {adminPermissions.map((p) => (
                            <Badge key={p.permission} variant="info" className="text-[11px]">
                              {p.permission}
                            </Badge>
                          ))}
                        </div>
                        <Link
                          href="/admin/users?tab=admins"
                          className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-navy hover:bg-background-light"
                        >
                          <KeyRound className="h-3.5 w-3.5" />
                          Edit permissions
                        </Link>
                      </div>
                    )}
                  </AdminRoutePanel>
                ) : null}

                {user.role === 'expert' ? (
                  <AdminRoutePanel
                    title="Tutor profile"
                    description="Tutor-specific tools: Private Speaking onboarding, calibration, and scheduling."
                  >
                    <div className="flex flex-wrap gap-2">
                      <Link
                        href={`/admin/private-speaking?expertUserId=${encodeURIComponent(user.id)}`}
                        className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-navy hover:bg-background-light"
                      >
                        <Mic className="h-3.5 w-3.5" />
                        Private Speaking
                      </Link>
                      <Link
                        href={`/admin/review-ops?expertId=${encodeURIComponent(user.id)}`}
                        className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-navy hover:bg-background-light"
                      >
                        <ShieldCheck className="h-3.5 w-3.5" />
                        Review Ops
                      </Link>
                    </div>
                  </AdminRoutePanel>
                ) : null}

                <AdminRoutePanel title="Security" description="MFA, lockout, and active sessions for this account.">
                  {user.security ? (
                    <div className="grid gap-3 md:grid-cols-2">
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">MFA</p>
                        <p className="mt-1 text-sm font-medium text-navy">{user.security.mfaEnabled ? 'Authenticator enrolled' : 'Not enrolled'}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Failed sign-ins</p>
                        <p className="mt-1 text-sm font-medium text-navy">{user.security.failedSignInCount}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Lockout state</p>
                        <p className="mt-1 text-sm font-medium text-navy">
                          {user.security.lockedOut ? `Locked until ${formatDate(user.security.lockoutUntil)}` : 'Not locked'}
                        </p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Active sessions</p>
                        <p className="mt-1 text-sm font-medium text-navy">{user.security.activeSessionCount}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4 md:col-span-2">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Last seen</p>
                        <p className="mt-1 text-sm font-medium text-navy">
                          {formatDate(user.security.lastSessionAt, '-')}
                          {user.security.lastSessionIp ? ` - ${user.security.lastSessionIp}` : ''}
                        </p>
                        {user.security.lastSessionDevice ? (
                          <p className="mt-1 text-xs text-muted">{user.security.lastSessionDevice}</p>
                        ) : null}
                      </div>
                    </div>
                  ) : (
                    <p className="text-sm text-muted">No authentication account is linked to this user.</p>
                  )}
                </AdminRoutePanel>

                {user.subscription ? (
                  <AdminRoutePanel title="Subscription" description="Current billing relationship for this learner.">
                    <div className="grid gap-3 md:grid-cols-2">
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Plan</p>
                        <p className="mt-1 text-sm font-medium text-navy">{subscriptionLabel}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Status</p>
                        <p className="mt-1 text-sm font-medium text-navy">{user.subscription.status}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Started</p>
                        <p className="mt-1 text-sm font-medium text-navy">{formatDate(user.subscription.startedAt)}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-background-light p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Next renewal</p>
                        <p className="mt-1 text-sm font-medium text-navy">{formatDate(user.subscription.nextRenewalAt)}</p>
                      </div>
                    </div>
                  </AdminRoutePanel>
                ) : user.role === 'learner' ? (
                  <AdminRoutePanel title="Subscription" description="No active subscription found for this learner.">
                    <p className="text-sm text-muted">This learner has not subscribed to a paid plan yet.</p>
                  </AdminRoutePanel>
                ) : null}

                <AdminRoutePanel title="Recent activity" description="Last 20 audit events touching this account.">
                  {user.recentActivity && user.recentActivity.length > 0 ? (
                    <ol className="space-y-2">
                      {user.recentActivity.map((event) => (
                        <li key={event.id} className="rounded-xl border border-border/60 bg-background-light px-3 py-2">
                          <div className="flex flex-wrap items-baseline justify-between gap-2">
                            <p className="text-sm font-medium text-navy">{event.action}</p>
                            <p className="text-xs text-muted">{formatDate(event.occurredAt)}</p>
                          </div>
                          <p className="text-xs text-muted">By {event.actorName}</p>
                          {event.details ? <p className="mt-1 text-xs text-muted">{event.details}</p> : null}
                        </li>
                      ))}
                    </ol>
                  ) : (
                    <p className="text-sm text-muted">No audit events yet for this account.</p>
                  )}
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
          <div className="flex justify-end gap-3 border-t border-border pt-4">
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
          <div className="rounded-[20px] border border-border bg-background-light p-3 text-sm text-muted">
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
          <div className="flex justify-end gap-3 border-t border-border pt-4">
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
