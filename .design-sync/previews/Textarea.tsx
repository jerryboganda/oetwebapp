// Authored preview — Textarea (multi-line; e.g. an OET writing response box).
// Each named export = one labeled card cell.
import { Textarea } from 'oet-with-dr-hesham';

const Column = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 560 }}>{children}</div>
);

export const Default = () => (
  <Column>
    <Textarea
      label="Your written response"
      placeholder="Write your referral letter here (180–200 words)…"
      rows={6}
    />
  </Column>
);

export const WithHint = () => (
  <Column>
    <Textarea
      label="Notes for your tutor"
      placeholder="Anything you'd like feedback on before your speaking session…"
      hint="Optional — your tutor reviews this before marking."
      rows={4}
    />
  </Column>
);

export const Filled = () => (
  <Column>
    <Textarea
      label="Your written response"
      rows={6}
      defaultValue={
        'Dear Dr Lee,\n\nI am writing to refer Mr James Carter, a 68-year-old patient who was admitted yesterday with chest pain and shortness of breath. His vital signs have stabilised, however he requires further cardiac assessment.'
      }
    />
  </Column>
);

export const ErrorState = () => (
  <Column>
    <Textarea
      label="Your written response"
      rows={4}
      defaultValue="Dear Doctor, please see this patient."
      error="Your response is too short — aim for at least 180 words."
    />
  </Column>
);

export const Disabled = () => (
  <Column>
    <Textarea
      label="Submitted response (locked)"
      rows={4}
      defaultValue="This letter has been submitted for marking and can no longer be edited."
      disabled
    />
  </Column>
);
