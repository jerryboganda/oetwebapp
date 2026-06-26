// Authored preview — Input (text input with label / placeholder / hint / error).
// Each named export = one labeled card cell.
import { Input } from 'oet-prep';

const Column = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 400 }}>{children}</div>
);

export const Default = () => (
  <Column>
    <Input label="Email" type="email" placeholder="you@hospital.nhs.uk" />
    <Input label="Full name" placeholder="e.g. Dr Amara Okafor" />
  </Column>
);

export const WithHint = () => (
  <Column>
    <Input
      label="Candidate number"
      placeholder="OET-000000"
      hint="Find this on your booking confirmation email."
    />
  </Column>
);

export const Filled = () => (
  <Column>
    <Input label="First language" defaultValue="Tagalog" />
    <Input label="Target overall grade" defaultValue="B" />
  </Column>
);

export const ErrorState = () => (
  <Column>
    <Input
      label="Email"
      type="email"
      defaultValue="amara.okafor@"
      error="Enter a valid email address."
    />
  </Column>
);

export const Disabled = () => (
  <Column>
    <Input label="Profession" defaultValue="Registered Nurse" disabled />
  </Column>
);
