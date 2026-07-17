// Authored preview — Checkbox (single boolean; label required).
// Each named export = one labeled card cell.
import { Checkbox } from 'oet-with-dr-hesham';

const Column = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 460 }}>{children}</div>
);

export const Default = () => (
  <Column>
    <Checkbox label="Email me when my speaking session has been marked" />
  </Column>
);

export const Checked = () => (
  <Column>
    <Checkbox
      label="I confirm the work submitted is my own and was completed under exam conditions."
      defaultChecked
    />
  </Column>
);

export const ErrorState = () => (
  <Column>
    <Checkbox
      label="I accept the OET terms of use and privacy policy."
      error="Required to continue"
    />
  </Column>
);

export const Disabled = () => (
  <Column>
    <Checkbox label="Unlimited mock exams (included in your current plan)" defaultChecked disabled />
  </Column>
);
