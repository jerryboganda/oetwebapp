// Authored preview — RadioGroup. options shape: { value, label, description? }[].
// RadioGroup is a controlled component (value + onChange). Each named export = one cell.
import { useState } from 'react';
import { RadioGroup } from 'oet-prep';

const Column = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 460 }}>{children}</div>
);

const professions = [
  { value: 'nursing', label: 'Nursing', description: 'OET for registered and student nurses.' },
  { value: 'medicine', label: 'Medicine', description: 'OET for doctors and IMGs.' },
  { value: 'pharmacy', label: 'Pharmacy', description: 'OET for community and hospital pharmacists.' },
];

const mcqOptions = [
  { value: 'a', label: 'A — To request an urgent cardiology review' },
  { value: 'b', label: 'B — To confirm the patient has been discharged' },
  { value: 'c', label: 'C — To update the GP on a change in medication' },
  { value: 'd', label: 'D — To arrange a follow-up appointment' },
];

export const ProfessionPicker = () => {
  const [value, setValue] = useState('');
  return (
    <Column>
      <RadioGroup
        name="profession"
        label="Choose your profession"
        options={professions}
        value={value}
        onChange={setValue}
      />
    </Column>
  );
};

export const Selected = () => {
  const [value, setValue] = useState('nursing');
  return (
    <Column>
      <RadioGroup
        name="profession-selected"
        label="Choose your profession"
        options={professions}
        value={value}
        onChange={setValue}
      />
    </Column>
  );
};

export const McqAnswer = () => {
  const [value, setValue] = useState('c');
  return (
    <Column>
      <RadioGroup
        name="reading-mcq"
        label="What is the main purpose of this letter?"
        options={mcqOptions}
        value={value}
        onChange={setValue}
      />
    </Column>
  );
};

export const ErrorState = () => {
  const [value, setValue] = useState('');
  return (
    <Column>
      <RadioGroup
        name="profession-error"
        label="Choose your profession"
        options={professions}
        value={value}
        onChange={setValue}
        error="Please select a profession to continue."
      />
    </Column>
  );
};
