'use client';

import { useCallback } from 'react';

import { Input, Textarea } from '@/components/ui/form-controls';
import type { WritingRecipientDto } from '@/lib/writing/types';

interface RecipientEditorProps {
  value: WritingRecipientDto;
  onChange: (next: WritingRecipientDto) => void;
}

/**
 * Editor for the letter recipient block (who the candidate is writing to).
 * Spec §3 — recipient name/role/organisation/address. All four fields are
 * strings in the contract (`WritingRecipientDto`); organisation/address may be
 * left blank.
 */
export function RecipientEditor({ value, onChange }: RecipientEditorProps) {
  const patch = useCallback(
    (p: Partial<WritingRecipientDto>) => onChange({ ...value, ...p }),
    [value, onChange],
  );

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Input
          label="Recipient name"
          value={value.name}
          onChange={(e) => patch({ name: e.target.value })}
          placeholder="e.g. Dr Sarah Lin"
        />
        <Input
          label="Recipient role"
          value={value.role}
          onChange={(e) => patch({ role: e.target.value })}
          placeholder="e.g. Consultant Cardiologist"
        />
      </div>
      <Input
        label="Organisation"
        hint="Optional — hospital, clinic, or practice."
        value={value.organisation}
        onChange={(e) => patch({ organisation: e.target.value })}
        placeholder="e.g. St Mary's Hospital"
      />
      <Textarea
        label="Address"
        hint="Optional — appears in the letter header."
        value={value.address}
        onChange={(e) => patch({ address: e.target.value })}
        rows={2}
        placeholder="e.g. 14 Park Road, Newtown NSW 2042"
      />
    </div>
  );
}
