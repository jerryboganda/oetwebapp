'use client';

/** Mock-set wizard — step 2: pick the two role-play cards. */

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';
import { Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  fetchAdminSpeakingContentOptions,
  updateAdminSpeakingMockSet,
  type AdminSpeakingMockSetRow,
} from '@/lib/api';

type ContentOption = { id: string; title: string; status: string };

export function StepRolePlays() {
  const wizard = useAdminWizard<AdminSpeakingMockSetRow>();
  const row = wizard.entity;

  const [options, setOptions] = useState<ContentOption[]>([]);
  const [rolePlay1, setRolePlay1] = useState(row.rolePlay1?.contentId ?? '');
  const [rolePlay2, setRolePlay2] = useState(row.rolePlay2?.contentId ?? '');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchAdminSpeakingContentOptions()
      .then(setOptions)
      .catch(() => setError('Could not load speaking content options.'));
  }, []);

  const duplicate = Boolean(rolePlay1) && rolePlay1 === rolePlay2;
  const canAdvance = Boolean(rolePlay1) && Boolean(rolePlay2) && !duplicate;

  const submit = useCallback(async () => {
    if (!rolePlay1 || !rolePlay2) {
      setError('Pick two role-play content items.');
      throw new Error('invalid');
    }
    if (duplicate) {
      setError('Role-play 1 and 2 must be different content items.');
      throw new Error('invalid');
    }
    setError(null);
    await updateAdminSpeakingMockSet(row.mockSetId, {
      rolePlay1ContentId: rolePlay1,
      rolePlay2ContentId: rolePlay2,
    });
    await wizard.refresh();
  }, [row.mockSetId, rolePlay1, rolePlay2, duplicate, wizard]);

  useStepRegistration('role-plays', { canAdvance, submit });

  const selectOptions = [
    { value: '', label: 'Select…' },
    ...options.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
  ];

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Pick role-plays</h2>
        <p className="text-sm text-muted">Choose two distinct, published speaking role-play cards. Card 1 runs first in the exam, then Card 2.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {!row.rolePlay1?.isSpeaking && row.rolePlay1?.contentId ? (
        <InlineAlert variant="warning">
          <span className="inline-flex items-center gap-1"><AlertTriangle className="h-4 w-4" /> Current role-play 1 is not speaking content.</span>
        </InlineAlert>
      ) : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Select label="Role-play 1" value={rolePlay1} onChange={(e) => setRolePlay1(e.target.value)} options={selectOptions} required />
        <Select label="Role-play 2" value={rolePlay2} onChange={(e) => setRolePlay2(e.target.value)} options={selectOptions} required />
      </div>

      {duplicate ? <p className="text-xs text-danger">Role-play 1 and 2 must be different content items.</p> : null}
    </div>
  );
}
