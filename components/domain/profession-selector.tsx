'use client';

import { Select } from '@/components/ui';
import { useProfessions } from '@/lib/hooks/use-professions';

interface ProfessionSelectorProps {
  value?: string;
  onChange?: (val: string) => void;
  error?: string;
  className?: string;
}

export function ProfessionSelector({ value, onChange, error, className }: ProfessionSelectorProps) {
  const { options } = useProfessions();
  return (
    <Select
      label="Profession"
      placeholder="Select your profession"
      options={options}
      value={value}
      onChange={(e) => onChange?.(e.target.value)}
      error={error}
      className={className}
    />
  );
}
