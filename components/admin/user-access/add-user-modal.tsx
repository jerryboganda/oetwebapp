'use client';

import { useState } from 'react';
import { Button } from '@/components/admin/ui/button';
import { Input, RadioGroup, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { InlineAlert } from '@/components/ui/alert';
import { useProfessions } from '@/lib/hooks/use-professions';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  createAdminUser,
  createEmptyUserAccess,
  grantUserAddon,
  grantUserPackage,
  putUserAccessScope,
  type CreateAdminUserResult,
  type UserAccess,
} from '@/lib/user-access';
import { ManageAccessPanel } from './manage-access-panel';

type LoginSetupMode = 'invite' | 'password';

interface AddUserModalProps {
  open: boolean;
  onClose: () => void;
  /** Fired after the account (and its access allocation) has been fully created. */
  onCreated: (result: CreateAdminUserResult) => void;
}

const initialAccess = () => createEmptyUserAccess();

/**
 * "Add User" modal — creates a learner account (password or email invite),
 * then allocates packages/add-ons/module scope in one flow:
 *   createAdminUser -> grantUserPackage* -> grantUserAddon* -> putUserAccessScope
 */
export function AddUserModal({ open, onClose, onCreated }: AddUserModalProps) {
  const { options: professionOptions } = useProfessions();
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [mobileNumber, setMobileNumber] = useState('');
  const [professionId, setProfessionId] = useState('');
  const [loginSetup, setLoginSetup] = useState<LoginSetupMode>('invite');
  const [password, setPassword] = useState('');
  const [access, setAccess] = useState<UserAccess>(initialAccess());
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  function resetForm() {
    setName('');
    setEmail('');
    setMobileNumber('');
    setProfessionId('');
    setLoginSetup('invite');
    setPassword('');
    setAccess(initialAccess());
    setError(null);
  }

  function handleClose() {
    if (isSubmitting) return;
    resetForm();
    onClose();
  }

  async function handleSubmit() {
    setError(null);
    const trimmedName = name.trim();
    const trimmedEmail = email.trim();
    const emailLooksValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail);

    if (!trimmedName || !trimmedEmail) {
      setError('Name and email are required.');
      return;
    }
    if (!emailLooksValid) {
      setError('Please enter a valid email address.');
      return;
    }
    if (!professionId) {
      setError('Please select a profession.');
      return;
    }
    if (loginSetup === 'password' && password.length < 10) {
      setError('Password must be at least 10 characters long.');
      return;
    }

    setIsSubmitting(true);
    try {
      const created = await createAdminUser({
        name: trimmedName,
        email: trimmedEmail,
        role: 'learner',
        professionId,
        mobileNumber: mobileNumber.trim() || undefined,
        password: loginSetup === 'password' ? password : undefined,
        sendInvite: loginSetup === 'invite',
      });

      for (const sub of access.subscriptions) {
        await grantUserPackage(created.id, {
          planCode: sub.planCode,
          expiresAt: sub.expiresAt,
          makePrimary: sub.isPrimary,
          grantIncludedCredits: sub.grantIncludedCredits,
        });
      }

      for (const addOn of access.addOns) {
        await grantUserAddon(created.id, {
          addonCode: addOn.code,
          subscriptionId: addOn.subscriptionId,
        });
      }

      await putUserAccessScope(created.id, {
        modules: access.moduleOverrides,
        materialFolderIds: access.materialFolderIds,
        recallSetCodes: access.recallSetCodes,
        accessExpiresAt: access.accessExpiresAt,
        clearAccessExpiry: !access.accessExpiresAt,
      });

      onCreated(created);
      resetForm();
      onClose();
    } catch (submitError) {
      console.error(submitError);
      setError(readErrorMessage(submitError, 'Unable to create this user.'));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={handleClose} title="Add User" size="lg">
      <div className="space-y-5 py-2">
        {error ? <InlineAlert variant="error" dismissible>{error}</InlineAlert> : null}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Full Name" value={name} onChange={(event) => setName(event.target.value)} disabled={isSubmitting} />
          <Input
            label="Email Address"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            disabled={isSubmitting}
          />
          <Input
            label="Phone Number"
            value={mobileNumber}
            onChange={(event) => setMobileNumber(event.target.value)}
            disabled={isSubmitting}
          />
          <Select
            label="Profession"
            value={professionId}
            onChange={(event) => setProfessionId(event.target.value)}
            options={[{ value: '', label: 'Select a profession...' }, ...professionOptions]}
            disabled={isSubmitting}
          />
        </div>

        <RadioGroup
          name="login-setup"
          label="Login setup"
          value={loginSetup}
          onChange={(value) => setLoginSetup(value as LoginSetupMode)}
          options={[
            {
              value: 'invite',
              label: 'Send invite email',
              description: 'The learner sets their own password from the invite link.',
            },
            {
              value: 'password',
              label: 'Set a password now',
              description: 'You choose the initial password for this account.',
            },
          ]}
        />

        {loginSetup === 'password' ? (
          <Input
            label="Initial Password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            hint="At least 10 characters, mixing case, a number, and a symbol."
            disabled={isSubmitting}
          />
        ) : null}

        <div className="border-t border-border pt-4">
          <h3 className="mb-3 text-sm font-semibold text-navy">Access &amp; allocation</h3>
          <ManageAccessPanel value={access} onChange={setAccess} disabled={isSubmitting} />
        </div>

        <div className="flex justify-end gap-3 border-t border-border pt-4">
          <Button variant="outline" onClick={handleClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} loading={isSubmitting}>
            Create User
          </Button>
        </div>
      </div>
    </Modal>
  );
}
