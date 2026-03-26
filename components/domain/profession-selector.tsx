'use client';

import { cn } from '@/lib/utils';
import { Select } from '@/components/ui';

const professions = [
  { value: 'nursing', label: 'Nursing' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'dentistry', label: 'Dentistry' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'dietetics', label: 'Dietetics' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'podiatry', label: 'Podiatry' },
  { value: 'occupational-therapy', label: 'Occupational Therapy' },
  { value: 'optometry', label: 'Optometry' },
  { value: 'radiography', label: 'Radiography' },
  { value: 'speech-pathology', label: 'Speech Pathology' },
  { value: 'veterinary-science', label: 'Veterinary Science' },
];

interface ProfessionSelectorProps {
  value?: string;
  onChange?: (val: string) => void;
  error?: string;
  className?: string;
}

export function ProfessionSelector({ value, onChange, error, className }: ProfessionSelectorProps) {
  return (
    <Select
      label="Profession"
      placeholder="Select your profession"
      options={professions}
      value={value}
      onChange={(e) => onChange?.(e.target.value)}
      error={error}
      className={className}
    />
  );
}
