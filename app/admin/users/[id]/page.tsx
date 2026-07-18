'use client';

import Link from 'next/link';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import {
  Coins,
  KeyRound,
  Lock,
  LockKeyhole,
  LogOut,
  Mail,
  Mic,
  Pencil,
  Phone,
  RefreshCcw,
  Shield,
  ShieldCheck,
  User as UserIcon,
  UserLock,
} from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { AdminQuickAction } from '@/components/domain/admin-quick-action';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Input, Select, Checkbox } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { ManageAccessPanel } from '@/components/admin/user-access/manage-access-panel';
import type { GrantUserPackageInput, UserAccessSubscriptionRow } from '@/lib/api/user-access-packages';
import {
  adjustAdminUserCredits,
  deleteAdminUser,
  hardDeleteAdminUser,
  fetchAdminPermissions,
  fetchAdminSignupCatalog,
  resendAdminUserInvite,
  restoreAdminUser,
  revokeAdminUserSessions,
  setAdminUserPassword,
  triggerAdminUserPasswordReset,
  unlockAdminUser,
  updateAdminUserProfile,
  updateAdminUserStatus,
  type AdminUserProfileUpdatePayload,
} from '@/lib/api';
import {
  fetchUserAccess,
  grantUserAddon,
  grantUserPackage,
  putUserAccessScope,
  removeUserPackage,
  type UserAccess,
} from '@/lib/user-access';
import { getAdminUserDetailData } from '@/lib/admin';
import { readErrorMessage } from '@/lib/read-error-message';
import { TARGET_COUNTRY_OPTIONS } from '@/lib/auth/target-countries';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type {
  AdminPermissionGrant,
  AdminSignupCatalogResponse,
  AdminSignupProfessionCatalogItem,
  AdminUserDetail,
} from '@/lib/types/admin';

type ProfileForm = {
  displayName: string;
  firstName: string;
  lastName: string;
  mobileNumber: string;
  professionId: string;
  examTypeId: string;
  countryTarget: string;
  timezone: string;
  locale: string;
  marketingOptIn: boolean;
  agreeToTerms: boolean;
  agreeToPrivacy: boolean;
  specialties: string;
};

type LearnerTextProfileField =
  | 'displayName'
  | 'firstName'
  | 'lastName'
  | 'mobileNumber'
  | 'professionId'
  | 'examTypeId'
  | 'countryTarget'
  | 'timezone'
  | 'locale';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;
type PasswordForm = {
  password: string;
  confirmPassword: string;
};
type PasswordFormErrors = Partial<Record<keyof PasswordForm, string>>;

const PASSWORD_POLICY_HINT = 'Use at least 10 characters with uppercase, lowercase, a number, a symbol, and avoid common/leaked passwords.';

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

function formatBool(value: boolean | null | undefined) {
  if (value === null || value === undefined) return null;
  return value ? 'Yes' : 'No';
}

const allTargetCountryOptions = [...TARGET_COUNTRY_OPTIONS];

function normalizeComparable(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase();
}

function findCatalogItemByIdOrLabel<T extends { id: string; label: string }>(items: T[], value: string | null | undefined) {
  const normalized = normalizeComparable(value);
  if (!normalized) return null;
  return items.find((item) => normalizeComparable(item.id) === normalized || normalizeComparable(item.label) === normalized) ?? null;
}

function appendCurrentOption(
  options: { value: string; label: string }[],
  currentValue: string | null | undefined,
  labelPrefix = 'Current value',
) {
  const value = currentValue?.trim();
  if (!value || options.some((option) => option.value === value)) return options;
  return [...options, { value, label: `${labelPrefix}: ${value} (not in signup catalog)` }];
}

function displayCatalogValue<T extends { id: string; label: string }>(items: T[] | undefined, value: string | null | undefined) {
  if (!value) return value;
  return findCatalogItemByIdOrLabel(items ?? [], value)?.label ?? value;
}

function effectiveCountryTargets(profession: AdminSignupProfessionCatalogItem | null) {
  return profession?.countryTargets?.length ? profession.countryTargets : allTargetCountryOptions;
}

function ProfileField({
  label,
  value,
  locked = false,
}: {
  label: string;
  value: string | null | undefined;
  locked?: boolean;
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
      <p className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">
        {locked ? <LockKeyhole className="h-3 w-3" /> : null}
        {label}
      </p>
      <p className="mt-1 break-words text-sm font-medium text-admin-fg-strong">{value ?? '—'}</p>
    </div>
  );
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
  const setPasswordButtonRef = useRef<HTMLButtonElement | null>(null);
  const [adminPermissions, setAdminPermissions] = useState<AdminPermissionGrant[] | null>(null);
  const [isProfileModalOpen, setIsProfileModalOpen] = useState(false);
  const [profileForm, setProfileForm] = useState<ProfileForm | null>(null);
  const [signupCatalog, setSignupCatalog] = useState<AdminSignupCatalogResponse | null>(null);
  const [isPasswordModalOpen, setIsPasswordModalOpen] = useState(false);
  const [isPasswordSaving, setIsPasswordSaving] = useState(false);
  const [passwordForm, setPasswordForm] = useState<PasswordForm>({ password: '', confirmPassword: '' });
  const [passwordErrors, setPasswordErrors] = useState<PasswordFormErrors>({});
  const [access, setAccess] = useState<UserAccess | null>(null);
  const [originalAccess, setOriginalAccess] = useState<UserAccess | null>(null);
  const [isSavingAccess, setIsSavingAccess] = useState(false);

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

        // Learners get the interactive access & allocation editor.
        if (detail.role === 'learner') {
          try {
            const userAccess = await fetchUserAccess(detail.id);
            if (!cancelled) {
              setAccess(userAccess);
              setOriginalAccess(userAccess);
            }
          } catch (accessError) {
            console.error(accessError);
            if (!cancelled) {
              setAccess(null);
              setOriginalAccess(null);
            }
          }
        } else {
          setAccess(null);
          setOriginalAccess(null);
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

  const openPasswordModal = useCallback(() => {
    setPasswordForm({ password: '', confirmPassword: '' });
    setPasswordErrors({});
    setIsPasswordModalOpen(true);
  }, []);

  const closePasswordModal = useCallback(() => {
    setIsPasswordModalOpen(false);
    setPasswordForm({ password: '', confirmPassword: '' });
    setPasswordErrors({});
    window.setTimeout(() => {
      setPasswordButtonRef.current?.focus();
      if (document.activeElement !== setPasswordButtonRef.current) {
        requestAnimationFrame(() => setPasswordButtonRef.current?.focus());
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

  async function handleSetPassword() {
    if (!user) return;

    const nextErrors: PasswordFormErrors = {};
    if (passwordForm.password.length === 0) {
      nextErrors.password = 'New password is required.';
    } else if (passwordForm.password.length < 10) {
      nextErrors.password = 'Password must be at least 10 characters long.';
    }
    if (passwordForm.confirmPassword.length === 0) {
      nextErrors.confirmPassword = 'Please confirm the password.';
    } else if (passwordForm.password !== passwordForm.confirmPassword) {
      nextErrors.confirmPassword = 'Passwords do not match.';
    }

    if (Object.keys(nextErrors).length > 0) {
      setPasswordErrors(nextErrors);
      return;
    }

    setIsPasswordSaving(true);
    try {
      const result = await setAdminUserPassword(user.id, { password: passwordForm.password });
      const revoked = typeof result?.revoked === 'number' ? result.revoked : 0;
      setToast({
        variant: 'success',
        message: revoked > 0
          ? `Password updated for ${user.email} and ${revoked} active session(s) were revoked.`
          : `Password updated for ${user.email}.`,
      });
      closePasswordModal();
      await reloadUser();
    } catch (error) {
      console.error(error);
      const message = error instanceof Error && error.message
        ? error.message
        : 'Unable to set this password.';
      setPasswordErrors((current) => ({ ...current, password: message }));
      setToast({ variant: 'error', message });
    } finally {
      setIsPasswordSaving(false);
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

  async function handleSaveAccess() {
    if (!user || !access) return;
    setIsSavingAccess(true);
    try {
      // Persist any locally drafted packages, then remove any that were
      // dropped from the original server-fetched list.
      for (const sub of access.subscriptions as UserAccessSubscriptionRow[]) {
        if (sub.isPending) {
          const payload: GrantUserPackageInput = {
            planCode: sub.planCode,
            startsAt: sub.startsAt,
            expiresAt: sub.expiresAt,
            makePrimary: sub.isPrimary,
            grantIncludedCredits: sub.grantIncludedCredits,
            overrideProfessionMismatch: sub.overrideProfessionMismatch,
          };
          await grantUserPackage(user.id, payload);
        }
      }
      const remainingIds = new Set(access.subscriptions.filter((sub) => !sub.isPending).map((sub) => sub.id));
      for (const originalSub of originalAccess?.subscriptions ?? []) {
        if (!remainingIds.has(originalSub.id)) {
          await removeUserPackage(user.id, originalSub.id);
        }
      }

      // Add-ons have no removal endpoint — only newly drafted ones are sent.
      for (const addOn of access.addOns) {
        if (addOn.isPending) {
          await grantUserAddon(user.id, { addonCode: addOn.code, subscriptionId: addOn.subscriptionId });
        }
      }

      const saved = await putUserAccessScope(user.id, {
        modules: access.moduleOverrides,
        materialFolderIds: access.materialFolderIds,
        videoIds: access.videoIds,
        recallSetCodes: access.recallSetCodes,
        accessExpiresAt: access.accessExpiresAt,
        clearAccessExpiry: !access.accessExpiresAt,
      });

      setAccess(saved);
      setOriginalAccess(saved);
      setToast({ variant: 'success', message: `Access updated for ${user.email}.` });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: readErrorMessage(error, 'Unable to update access for this learner.') });
    } finally {
      setIsSavingAccess(false);
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

  async function handleHardDelete() {
    if (!user) return;
    if (!window.confirm('PERMANENTLY DELETE this user and EVERYTHING tied to them — attempts, results, AND invoices/payments/audit records — across the entire system? This is irreversible.')) return;
    const typed = window.prompt('This cannot be undone. Type the user\'s email to confirm permanent purge:');
    if (typed == null || typed.trim().toLowerCase() !== (user.email ?? '').trim().toLowerCase()) {
      setToast({ variant: 'error', message: 'Confirmation did not match — purge cancelled.' });
      return;
    }
    setIsMutating(true);
    try {
      const res = await hardDeleteAdminUser(user.id);
      setToast({ variant: 'success', message: `Permanently purged user (${res.purgedRows} rows across ${res.tables} tables).` });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: (error as Error).message || 'Unable to permanently purge this account.' });
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

  async function openProfileModal() {
    if (!user) return;
    setProfileForm({
      displayName: user.displayName ?? user.name ?? '',
      firstName: user.firstName ?? '',
      lastName: user.lastName ?? '',
      mobileNumber: user.mobileNumber ?? '',
      professionId: user.professionId ?? user.profession ?? '',
      examTypeId: user.examTypeId ?? '',
      countryTarget: user.countryTarget ?? '',
      timezone: user.timezone ?? '',
      locale: user.locale ?? '',
      marketingOptIn: user.marketingOptIn ?? false,
      agreeToTerms: user.agreeToTerms ?? false,
      agreeToPrivacy: user.agreeToPrivacy ?? false,
      specialties: (user.specialties ?? []).join(', '),
    });
    setIsProfileModalOpen(true);
    if (!signupCatalog) {
      try {
        const catalog = (await fetchAdminSignupCatalog()) as AdminSignupCatalogResponse;
        setSignupCatalog(catalog);
      } catch (error) {
        console.error(error);
      }
    }
  }

  function closeProfileModal() {
    setIsProfileModalOpen(false);
    setProfileForm(null);
  }

  function updateProfileField<K extends keyof ProfileForm>(key: K, value: ProfileForm[K]) {
    setProfileForm((prev) => (prev ? { ...prev, [key]: value } : prev));
  }

  async function handleSaveProfile() {
    if (!user || !profileForm) return;
    setIsMutating(true);
    try {
      const isExpert = user.role === 'expert';
      const learnerPayload: AdminUserProfileUpdatePayload = {};
      const setChangedText = (
        key: LearnerTextProfileField,
        nextValue: string,
        currentValue: string | null | undefined,
      ) => {
        const next = nextValue.trim();
        const current = (currentValue ?? '').trim();
        if (next && next !== current) {
          learnerPayload[key] = next;
        }
      };

      setChangedText('displayName', profileForm.displayName, user.displayName ?? user.name);
      setChangedText('firstName', profileForm.firstName, user.firstName);
      setChangedText('lastName', profileForm.lastName, user.lastName);
      setChangedText('mobileNumber', profileForm.mobileNumber, user.mobileNumber);
      setChangedText('timezone', profileForm.timezone, user.timezone);
      setChangedText('locale', profileForm.locale, user.locale);

      const currentProfessionId = findCatalogItemByIdOrLabel(signupCatalog?.professions ?? [], user.professionId ?? user.profession)?.id
        ?? (user.professionId ?? user.profession ?? '').trim();
      const nextProfessionId = findCatalogItemByIdOrLabel(signupCatalog?.professions ?? [], profileForm.professionId)?.id
        ?? profileForm.professionId.trim();
      if (nextProfessionId && nextProfessionId !== currentProfessionId) learnerPayload.professionId = nextProfessionId;

      const currentExamTypeId = findCatalogItemByIdOrLabel(signupCatalog?.examTypes ?? [], user.examTypeId)?.id
        ?? (user.examTypeId ?? '').trim();
      const nextExamTypeId = findCatalogItemByIdOrLabel(signupCatalog?.examTypes ?? [], profileForm.examTypeId)?.id
        ?? profileForm.examTypeId.trim();
      if (nextExamTypeId && nextExamTypeId !== currentExamTypeId) learnerPayload.examTypeId = nextExamTypeId;

      const currentCountryTarget = (user.countryTarget ?? '').trim();
      const nextCountryTarget = profileForm.countryTarget.trim();
      if (nextCountryTarget && normalizeComparable(nextCountryTarget) !== normalizeComparable(currentCountryTarget)) {
        learnerPayload.countryTarget = nextCountryTarget;
      }

      if (profileForm.marketingOptIn !== (user.marketingOptIn ?? false)) learnerPayload.marketingOptIn = profileForm.marketingOptIn;
      if (profileForm.agreeToTerms !== (user.agreeToTerms ?? false)) learnerPayload.agreeToTerms = profileForm.agreeToTerms;
      if (profileForm.agreeToPrivacy !== (user.agreeToPrivacy ?? false)) learnerPayload.agreeToPrivacy = profileForm.agreeToPrivacy;

      const payload: AdminUserProfileUpdatePayload = isExpert
        ? {
            displayName: profileForm.displayName.trim(),
            timezone: profileForm.timezone.trim() || undefined,
            specialties: profileForm.specialties
              .split(',')
              .map((s) => s.trim())
              .filter(Boolean),
          }
        : learnerPayload;

      await updateAdminUserProfile(user.id, payload);
      await reloadUser();
      closeProfileModal();
      setToast({ variant: 'success', message: 'Profile updated successfully.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to update this profile.' });
    } finally {
      setIsMutating(false);
    }
  }

  const subscriptionLabel = useMemo(() => {
    if (!user?.subscription) return null;
    const sub = user.subscription;
    return `${sub.planName} - ${sub.priceAmount} ${sub.currency}/${sub.interval}`;
  }, [user?.subscription]);

  const selectedProfession = useMemo(
    () => findCatalogItemByIdOrLabel(signupCatalog?.professions ?? [], profileForm?.professionId),
    [signupCatalog, profileForm?.professionId],
  );

  useEffect(() => {
    if (!signupCatalog || !profileForm) return;

    const canonicalProfessionId = findCatalogItemByIdOrLabel(signupCatalog.professions, profileForm.professionId)?.id ?? profileForm.professionId;
    const canonicalExamTypeId = findCatalogItemByIdOrLabel(signupCatalog.examTypes, profileForm.examTypeId)?.id ?? profileForm.examTypeId;

    if (canonicalProfessionId === profileForm.professionId && canonicalExamTypeId === profileForm.examTypeId) {
      return;
    }

    setProfileForm((prev) => prev
      ? { ...prev, professionId: canonicalProfessionId, examTypeId: canonicalExamTypeId }
      : prev);
  }, [signupCatalog, profileForm?.professionId, profileForm?.examTypeId]);

  const examTypeOptions = useMemo(() => {
    const options = (signupCatalog?.examTypes ?? [])
      .filter((examType) => examType.isActive || examType.id === profileForm?.examTypeId)
      .map((examType) => ({ value: examType.id, label: examType.label }));
    return appendCurrentOption(options, profileForm?.examTypeId, 'Current exam type');
  }, [signupCatalog, profileForm?.examTypeId]);

  const professionOptions = useMemo(() => {
    const examTypeId = profileForm?.examTypeId;
    const options = (signupCatalog?.professions ?? [])
      .filter((profession) => profession.isActive || profession.id === profileForm?.professionId)
      .filter((profession) => !examTypeId || profession.examTypeIds.length === 0 || profession.examTypeIds.includes(examTypeId) || profession.id === profileForm?.professionId)
      .map((profession) => ({ value: profession.id, label: profession.label }));
    return appendCurrentOption(options, profileForm?.professionId, 'Current value');
  }, [signupCatalog, profileForm?.examTypeId, profileForm?.professionId]);

  const countryTargetOptions = useMemo(() => {
    const targets = effectiveCountryTargets(selectedProfession);
    const options = Array.from(new Set(targets)).map((country) => ({ value: country, label: country }));
    return appendCurrentOption(options, profileForm?.countryTarget, 'Current country');
  }, [selectedProfession, profileForm?.countryTarget]);

  function updateExamType(value: string) {
    setProfileForm((prev) => {
      if (!prev) return prev;
      const currentProfession = findCatalogItemByIdOrLabel(signupCatalog?.professions ?? [], prev.professionId);
      const professionStillValid = !value
        || !currentProfession
        || currentProfession.examTypeIds.length === 0
        || currentProfession.examTypeIds.includes(value);
      return {
        ...prev,
        examTypeId: value,
        professionId: professionStillValid ? prev.professionId : '',
      };
    });
  }

  function updateProfession(value: string) {
    setProfileForm((prev) => {
      if (!prev) return prev;
      const nextProfession = findCatalogItemByIdOrLabel(signupCatalog?.professions ?? [], value);
      const nextExamTypeId = nextProfession?.examTypeIds.length
        && prev.examTypeId
        && !nextProfession.examTypeIds.includes(prev.examTypeId)
        ? nextProfession.examTypeIds[0]
        : prev.examTypeId;
      const nextTargets = effectiveCountryTargets(nextProfession);
      const nextCountryTarget = prev.countryTarget && nextTargets.some((country) => country === prev.countryTarget)
        ? prev.countryTarget
        : (nextTargets[0] ?? prev.countryTarget);
      return {
        ...prev,
        professionId: value,
        examTypeId: nextExamTypeId,
        countryTarget: nextCountryTarget,
      };
    });
  }

  const displayedProfession = displayCatalogValue(signupCatalog?.professions, user?.professionId ?? user?.profession);
  const displayedExamType = displayCatalogValue(signupCatalog?.examTypes, user?.examTypeId);

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Users', href: '/admin/users' },
    { label: user?.name ?? userId },
  ];

  return (
    <AdminSettingsLayout
      title={user?.name ?? 'User profile'}
      description={user?.id}
      breadcrumbs={breadcrumbs}
      eyebrow="User profile"
      icon={<UserIcon className="h-5 w-5" />}
      backHref="/admin/users"
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {user ? (
          <>
            <div className="flex flex-col gap-3 border-b border-admin-border pb-4 md:flex-row md:items-end md:justify-between">
              <div className="space-y-1">
                <p className="font-mono text-xs text-admin-fg-muted">{user.id}</p>
                <div className="flex flex-wrap items-center gap-2 pt-1">
                  <Badge variant={(user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default') as any}>{uiRoleLabel(user.role)}</Badge>
                  <Badge variant={(user.status === 'active' ? 'success' : user.status === 'deleted' ? 'danger' : 'default') as any}>{user.status}</Badge>
                  {user.security?.lockedOut ? <Badge variant="danger">Locked</Badge> : null}
                  {user.security?.mfaEnabled ? <Badge variant="success">MFA</Badge> : null}
                  {user.security?.emailVerifiedAt ? <Badge variant="info">Email verified</Badge> : <Badge variant="default">Email unverified</Badge>}
                </div>
              </div>
              <div className="flex flex-wrap gap-2">
                {user.role !== 'admin' && user.status !== 'deleted' ? (
                  <Button variant="primary" onClick={openProfileModal} className="gap-2">
                    <Pencil className="h-4 w-4" />
                    Edit Profile
                  </Button>
                ) : null}
                {user.availableActions.canTriggerPasswordReset ? (
                  <Button variant="outline" onClick={handlePasswordReset} loading={isMutating} className="gap-2">
                    <KeyRound className="h-4 w-4" />
                    Reset Password
                  </Button>
                ) : null}
                {user.availableActions.canTriggerPasswordReset ? (
                  <Button ref={setPasswordButtonRef} variant="outline" onClick={openPasswordModal} className="gap-2">
                    <LockKeyhole className="h-4 w-4" />
                    Set Password
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
                <AdminQuickAction
                  href={`/admin/learners/${encodeURIComponent(user.id)}/study-plan`}
                  label="Study Plan"
                  className="w-auto"
                />
                {user.availableActions.canDelete ? (
                  <Button variant="destructive" onClick={() => openLifecycleModal('delete')} loading={isMutating}>Delete</Button>
                ) : null}
                {user.availableActions.canDelete ? (
                  <Button variant="destructive" className="border-2 border-red-700" onClick={handleHardDelete} loading={isMutating} title="Irreversibly purge this user and ALL their data, including invoices/payments/audit. system_admin only.">Permanently purge</Button>
                ) : null}
                {user.availableActions.canRestore ? (
                  <Button onClick={() => openLifecycleModal('restore')} loading={isMutating}>Restore</Button>
                ) : null}
              </div>
            </div>

            <div className="grid gap-6 lg:grid-cols-[280px,1fr]">
              <SettingsSection title="Identity" description="Primary account identity and role context.">
                <div className="flex flex-col items-center gap-4 text-center">
                  <div className="flex h-20 w-20 items-center justify-center rounded-full bg-lavender text-primary">
                    <UserIcon className="h-8 w-8" />
                  </div>
                  <div className="w-full space-y-3 text-left text-sm text-admin-text-muted">
                    <div className="flex items-start gap-3">
                      <Mail className="mt-0.5 h-4 w-4 text-admin-text-muted" />
                      <span className="break-all">{user.email}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <UserLock className="mt-0.5 h-4 w-4 text-admin-text-muted" />
                      <span className="break-all">{user.authAccountId ?? 'No linked auth account'}</span>
                    </div>
                    <div className="flex items-start gap-3">
                      <Shield className="mt-0.5 h-4 w-4 text-admin-text-muted" />
                      <span>Created {formatDate(user.createdAt, 'unknown')}</span>
                    </div>
                    {user.profession ? (
                      <div className="flex items-start gap-3">
                        <ShieldCheck className="mt-0.5 h-4 w-4 text-admin-text-muted" />
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
              </SettingsSection>

              <div className="space-y-6">
                <div className="grid gap-4 md:grid-cols-3">
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{user.role === 'expert' ? 'Tasks Graded' : 'Tasks Completed'}</p>
                    <p className="mt-1 text-2xl font-semibold text-admin-fg-strong">{(user.role === 'expert' ? user.tasksGraded : user.tasksCompleted) ?? 0}</p>
                  </div>
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Credit Balance</p>
                    <p className="mt-1 text-2xl font-semibold text-admin-fg-strong">{user.creditBalance ?? 0}</p>
                  </div>
                  <div className="rounded-2xl border border-border/60 bg-surface p-4 shadow-sm">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Last Login</p>
                    <p className="mt-1 text-sm font-semibold text-admin-fg-strong">{formatDate(user.lastLogin, 'Never')}</p>
                  </div>
                </div>

                {user.role !== 'admin' ? (
                  <SettingsSection
                    title="Registration details"
                    description="Everything captured at signup. Editable via Edit Profile — email is permanent."
                  >
                    <div className="grid gap-3 md:grid-cols-2">
                      <ProfileField label="Email (locked)" value={user.email} locked />
                      <ProfileField label="Display name" value={user.displayName ?? user.name} />
                      {user.role === 'learner' ? (
                        <>
                          <ProfileField label="First name" value={user.firstName} />
                          <ProfileField label="Last name" value={user.lastName} />
                          <ProfileField label="Mobile number" value={user.mobileNumber} />
                          <ProfileField label="Profession" value={displayedProfession} />
                          <ProfileField label="Exam type" value={displayedExamType} />
                          <ProfileField label="Target country" value={user.countryTarget} />
                          <ProfileField label="Timezone" value={user.timezone} />
                          <ProfileField label="Locale" value={user.locale} />
                          <ProfileField label="Marketing opt-in" value={formatBool(user.marketingOptIn)} />
                          <ProfileField label="Agreed to terms" value={formatBool(user.agreeToTerms)} />
                          <ProfileField label="Agreed to privacy" value={formatBool(user.agreeToPrivacy)} />
                        </>
                      ) : (
                        <>
                          <ProfileField label="Timezone" value={user.timezone} />
                          <ProfileField
                            label="Specialties"
                            value={user.specialties && user.specialties.length > 0 ? user.specialties.join(', ') : null}
                          />
                        </>
                      )}
                    </div>
                  </SettingsSection>
                ) : null}

                {user.role === 'learner' && user.attribution ? (
                  <SettingsSection
                    title="Acquisition attribution"
                    description="Read-only marketing attribution captured at signup."
                  >
                    <div className="grid gap-3 md:grid-cols-2">
                      <ProfileField label="UTM source" value={user.attribution.utmSource} />
                      <ProfileField label="UTM medium" value={user.attribution.utmMedium} />
                      <ProfileField label="UTM campaign" value={user.attribution.utmCampaign} />
                      <ProfileField label="UTM term" value={user.attribution.utmTerm} />
                      <ProfileField label="UTM content" value={user.attribution.utmContent} />
                      <ProfileField label="Referrer URL" value={user.attribution.referrerUrl} />
                      <ProfileField label="Landing path" value={user.attribution.landingPath} />
                    </div>
                  </SettingsSection>
                ) : null}

                {user.role === 'admin' ? (
                  <SettingsSection
                    title="Permissions"
                    description="Granular admin permissions for this account. Edit in the Admins & Permissions tab."
                  >
                    {adminPermissions === null ? (
                      <p className="text-sm text-admin-text-muted">Loading permissions…</p>
                    ) : adminPermissions.length === 0 ? (
                      <div className="flex flex-wrap items-center gap-3">
                        <p className="text-sm text-admin-text-muted">No granular permissions granted yet.</p>
                        <Link
                          href="/admin/users?tab=admins"
                          className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
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
                          className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
                        >
                          <KeyRound className="h-3.5 w-3.5" />
                          Edit permissions
                        </Link>
                      </div>
                    )}
                  </SettingsSection>
                ) : null}

                {user.role === 'expert' ? (
                  <SettingsSection
                    title="Tutor profile"
                    description="Tutor-specific tools: Private Speaking onboarding, calibration, and scheduling."
                  >
                    <div className="flex flex-wrap gap-2">
                      <Link
                        href={`/admin/private-speaking?expertUserId=${encodeURIComponent(user.id)}`}
                        className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
                      >
                        <Mic className="h-3.5 w-3.5" />
                        Private Speaking
                      </Link>
                      <Link
                        href={`/admin/review-ops?expertId=${encodeURIComponent(user.id)}`}
                        className="inline-flex items-center gap-1.5 rounded-2xl border border-border/60 bg-surface px-3 py-1.5 text-xs font-semibold text-admin-fg-strong hover:bg-admin-bg-subtle"
                      >
                        <ShieldCheck className="h-3.5 w-3.5" />
                        Review Ops
                      </Link>
                    </div>
                  </SettingsSection>
                ) : null}

                <SettingsSection title="Security" description="MFA, lockout, and active sessions for this account.">
                  {user.security ? (
                    <div className="grid gap-3 md:grid-cols-2">
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">MFA</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{user.security.mfaEnabled ? 'Authenticator enrolled' : 'Not enrolled'}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Failed sign-ins</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{user.security.failedSignInCount}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Lockout state</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">
                          {user.security.lockedOut ? `Locked until ${formatDate(user.security.lockoutUntil)}` : 'Not locked'}
                        </p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Active sessions</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{user.security.activeSessionCount}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4 md:col-span-2">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Last seen</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">
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
                </SettingsSection>

                {user.subscription ? (
                  <SettingsSection title="Subscription" description="Current billing relationship for this learner.">
                    <div className="grid gap-3 md:grid-cols-2">
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Plan</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{subscriptionLabel}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Status</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{user.subscription.status}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Started</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{formatDate(user.subscription.startedAt)}</p>
                      </div>
                      <div className="rounded-2xl border border-border/60 bg-admin-bg-subtle p-4">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Next renewal</p>
                        <p className="mt-1 text-sm font-medium text-admin-fg-strong">{formatDate(user.subscription.nextRenewalAt)}</p>
                      </div>
                    </div>
                  </SettingsSection>
                ) : user.role === 'learner' ? (
                  <SettingsSection title="Subscription" description="No active subscription found for this learner.">
                    <p className="text-sm text-admin-text-muted">This learner has not subscribed to a paid plan yet.</p>
                  </SettingsSection>
                ) : null}

                {user.role === 'learner' ? (
                  <SettingsSection
                    title="Access & Allocation"
                    description="Packages, add-ons, module access, and content scope for this learner."
                    actions={
                      access ? (
                        <Button size="sm" onClick={handleSaveAccess} loading={isSavingAccess}>
                          Save Access
                        </Button>
                      ) : null
                    }
                  >
                    {access ? (
                      <ManageAccessPanel
                        userId={user.id}
                        value={access}
                        onChange={setAccess}
                        learnerProfessionId={user.professionId ?? user.profession ?? ''}
                        learnerProfessionLabel={displayedProfession}
                        disabled={isSavingAccess}
                      />
                    ) : (
                      <p className="text-sm text-muted">Unable to load access details for this learner.</p>
                    )}
                  </SettingsSection>
                ) : null}

                <SettingsSection title="Recent activity" description="Last 20 audit events touching this account.">
                  {user.recentActivity && user.recentActivity.length > 0 ? (
                    <ol className="space-y-2">
                      {user.recentActivity.map((event) => (
                        <li key={event.id} className="rounded-xl border border-border/60 bg-admin-bg-subtle px-3 py-2">
                          <div className="flex flex-wrap items-baseline justify-between gap-2">
                            <p className="text-sm font-medium text-admin-fg-strong">{event.action}</p>
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
                </SettingsSection>
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

      <Modal open={isPasswordModalOpen} onClose={closePasswordModal} title="Set Password">
        <div className="space-y-4 py-2">
          <div className="rounded-[20px] border border-border bg-admin-bg-subtle p-3 text-sm text-muted">
            This sets the login password directly for the linked auth account. No email is sent, and any active sessions are revoked.
          </div>
          <Input
            label="New password"
            type="password"
            autoComplete="new-password"
            value={passwordForm.password}
            hint={PASSWORD_POLICY_HINT}
            onChange={(event) => {
              const nextPassword = event.target.value;
              setPasswordForm((current) => ({ ...current, password: nextPassword }));
              if (passwordErrors.password) {
                setPasswordErrors((current) => ({ ...current, password: undefined }));
              }
            }}
            error={passwordErrors.password}
          />
          <Input
            label="Confirm password"
            type="password"
            autoComplete="new-password"
            value={passwordForm.confirmPassword}
            onChange={(event) => {
              const nextConfirmPassword = event.target.value;
              setPasswordForm((current) => ({ ...current, confirmPassword: nextConfirmPassword }));
              if (passwordErrors.confirmPassword) {
                setPasswordErrors((current) => ({ ...current, confirmPassword: undefined }));
              }
            }}
            error={passwordErrors.confirmPassword}
          />
          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={closePasswordModal}>
              Cancel
            </Button>
            <Button onClick={handleSetPassword} loading={isPasswordSaving}>
              Save Password
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
          <div className="rounded-[20px] border border-border bg-admin-bg-subtle p-3 text-sm text-muted">
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

      <Modal open={isProfileModalOpen} onClose={closeProfileModal} title="Edit Profile">
        {profileForm ? (
          <div className="space-y-4 py-2">
            <div className="flex items-start gap-2 rounded-[20px] border border-border bg-admin-bg-subtle p-3 text-sm text-muted">
              <LockKeyhole className="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <p className="font-semibold text-admin-fg-strong">Email is permanent</p>
                <p>{user?.email}</p>
                <p className="mt-0.5 text-xs">The email is the account identity and cannot be changed.</p>
              </div>
            </div>

            <Input
              label="Display name"
              value={profileForm.displayName}
              onChange={(event) => updateProfileField('displayName', event.target.value)}
            />

            {user?.role === 'expert' ? (
              <>
                <Input
                  label="Timezone"
                  value={profileForm.timezone}
                  onChange={(event) => updateProfileField('timezone', event.target.value)}
                  hint="IANA timezone, e.g. Europe/London."
                />
                <Input
                  label="Specialties"
                  value={profileForm.specialties}
                  onChange={(event) => updateProfileField('specialties', event.target.value)}
                  hint="Comma-separated list."
                />
              </>
            ) : (
              <>
                <div className="grid gap-4 md:grid-cols-2">
                  <Input
                    label="First name"
                    value={profileForm.firstName}
                    onChange={(event) => updateProfileField('firstName', event.target.value)}
                  />
                  <Input
                    label="Last name"
                    value={profileForm.lastName}
                    onChange={(event) => updateProfileField('lastName', event.target.value)}
                  />
                </div>
                <Input
                  label="Mobile number"
                  value={profileForm.mobileNumber}
                  onChange={(event) => updateProfileField('mobileNumber', event.target.value)}
                />
                <Select
                  label="Profession"
                  value={profileForm.professionId}
                  onChange={(event) => updateProfession(event.target.value)}
                  placeholder={signupCatalog ? 'Select a profession' : 'Loading…'}
                  options={professionOptions}
                />
                <Select
                  label="Exam type"
                  value={profileForm.examTypeId}
                  onChange={(event) => updateExamType(event.target.value)}
                  placeholder={signupCatalog ? 'Select an exam type' : 'Loading…'}
                  options={examTypeOptions}
                />
                <Select
                  label="Target country"
                  value={profileForm.countryTarget}
                  onChange={(event) => updateProfileField('countryTarget', event.target.value)}
                  placeholder="Select a country"
                  options={countryTargetOptions}
                />
                <div className="grid gap-4 md:grid-cols-2">
                  <Input
                    label="Timezone"
                    value={profileForm.timezone}
                    onChange={(event) => updateProfileField('timezone', event.target.value)}
                  />
                  <Input
                    label="Locale"
                    value={profileForm.locale}
                    onChange={(event) => updateProfileField('locale', event.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Checkbox
                    label="Marketing opt-in"
                    checked={profileForm.marketingOptIn}
                    onChange={(event) => updateProfileField('marketingOptIn', event.target.checked)}
                  />
                  <Checkbox
                    label="Agreed to terms"
                    checked={profileForm.agreeToTerms}
                    onChange={(event) => updateProfileField('agreeToTerms', event.target.checked)}
                  />
                  <Checkbox
                    label="Agreed to privacy policy"
                    checked={profileForm.agreeToPrivacy}
                    onChange={(event) => updateProfileField('agreeToPrivacy', event.target.checked)}
                  />
                </div>
              </>
            )}

            <div className="flex justify-end gap-3 border-t border-border pt-4">
              <Button variant="outline" onClick={closeProfileModal}>
                Cancel
              </Button>
              <Button onClick={handleSaveProfile} loading={isMutating}>
                Save Profile
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>
    </AdminSettingsLayout>
  );
}
