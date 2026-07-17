// Authored preview — Badge. Each named export = one labeled card cell.
import { Badge } from 'oet-with-dr-hesham';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>{children}</div>
);

export const Variants = () => (
  <Row>
    <Badge variant="default">Active plan</Badge>
    <Badge variant="success">Passed</Badge>
    <Badge variant="warning">Expiring soon</Badge>
    <Badge variant="danger">Below grade B</Badge>
    <Badge variant="info">AI-marked</Badge>
    <Badge variant="muted">Not started</Badge>
    <Badge variant="outline">Optional</Badge>
  </Row>
);

export const ColourTokens = () => (
  <Row>
    <Badge variant="violet">Speaking</Badge>
    <Badge variant="indigo">Writing</Badge>
    <Badge variant="sky">Listening</Badge>
    <Badge variant="emerald">Pharmacology</Badge>
    <Badge variant="rose">Conditions</Badge>
    <Badge variant="slate">Documentation</Badge>
  </Row>
);

export const Sizes = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    <Row>
      <Badge variant="info" size="sm">
        Reading Part B
      </Badge>
      <Badge variant="success" size="sm">
        Grade A
      </Badge>
      <Badge variant="warning" size="sm">
        Pending Review
      </Badge>
    </Row>
    <Row>
      <Badge variant="info" size="md">
        Reading Part B
      </Badge>
      <Badge variant="success" size="md">
        Grade A
      </Badge>
      <Badge variant="warning" size="md">
        Pending Review
      </Badge>
    </Row>
  </div>
);

export const InContext = () => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 8, maxWidth: 360 }}>
    <span style={{ fontSize: 15, fontWeight: 600, color: '#0f172a' }}>Listening — Part C</span>
    <Badge variant="violet">New</Badge>
    <Badge variant="muted" size="sm">
      35 min
    </Badge>
  </div>
);
