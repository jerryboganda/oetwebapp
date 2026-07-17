// Authored preview — Select (dropdown). options shape: { value, label, disabled? }[].
// Each named export = one labeled card cell.
import { Select } from 'oet-with-dr-hesham';

const Column = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 400 }}>{children}</div>
);

const languages = [
  { value: 'tagalog', label: 'Tagalog' },
  { value: 'hindi', label: 'Hindi' },
  { value: 'arabic', label: 'Arabic' },
  { value: 'spanish', label: 'Spanish' },
  { value: 'yoruba', label: 'Yoruba' },
  { value: 'other', label: 'Other' },
];

const grades = [
  { value: 'A', label: 'Grade A (≥ 450)' },
  { value: 'B', label: 'Grade B (350–440)' },
  { value: 'C+', label: 'Grade C+ (300–340)' },
  { value: 'C', label: 'Grade C (200–290)' },
];

const professions = [
  { value: 'nursing', label: 'Nursing' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'pharmacy', label: 'Pharmacy' },
  { value: 'physiotherapy', label: 'Physiotherapy' },
  { value: 'dentistry', label: 'Dentistry (coming soon)', disabled: true },
];

export const WithPlaceholder = () => (
  <Column>
    <Select label="First language" placeholder="Select your first language…" options={languages} />
  </Column>
);

export const Selected = () => (
  <Column>
    <Select label="Target overall grade" options={grades} defaultValue="B" />
  </Column>
);

export const WithDisabledOption = () => (
  <Column>
    <Select
      label="Profession"
      placeholder="Choose your profession…"
      options={professions}
      hint="You can change this later in your account settings."
    />
  </Column>
);

export const ErrorState = () => (
  <Column>
    <Select
      label="First language"
      placeholder="Select your first language…"
      options={languages}
      error="Please select your first language to continue."
    />
  </Column>
);

export const Disabled = () => (
  <Column>
    <Select label="Profession" options={professions} defaultValue="nursing" disabled />
  </Column>
);
