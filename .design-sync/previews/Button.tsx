// Authored preview — Button. Each named export = one labeled card cell.
import { Button } from 'oet-with-dr-hesham';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'center' }}>{children}</div>
);

export const Variants = () => (
  <Row>
    <Button variant="primary">Start exam</Button>
    <Button variant="secondary">Save draft</Button>
    <Button variant="outline">Preview</Button>
    <Button variant="ghost">Cancel</Button>
    <Button variant="destructive">Delete attempt</Button>
  </Row>
);

export const Sizes = () => (
  <Row>
    <Button size="sm">Small</Button>
    <Button size="md">Medium</Button>
    <Button size="lg">Large</Button>
  </Row>
);

export const States = () => (
  <Row>
    <Button loading>Submitting…</Button>
    <Button disabled>Locked</Button>
    <Button variant="secondary" loading>
      Marking
    </Button>
  </Row>
);

export const FullWidth = () => (
  <div style={{ width: 320 }}>
    <Button fullWidth size="lg">
      Submit for marking
    </Button>
  </div>
);
