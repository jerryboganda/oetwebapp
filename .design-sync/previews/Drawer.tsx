// Authored preview — Drawer. Drawer is position:fixed (full-height edge panel +
// backdrop), so each cell wraps it in a sized, transformed Stage that becomes the
// containing block for the fixed elements — the drawer renders INSIDE the card
// instead of escaping to the page viewport. Pass open + onClose so it mounts.
import { Drawer, Button, Badge, InlineAlert } from 'oet-with-dr-hesham';
import type { ReactNode } from 'react';

const Stage = ({ children }: { children: ReactNode }) => (
  <div style={{ position: 'relative', transform: 'translateZ(0)', height: 520, width: '100%', overflow: 'hidden' }}>
    {children}
  </div>
);

export const AttemptDetails = () => (
  <Stage>
    <Drawer open={true} onClose={() => {}} title="Attempt details">
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <span style={{ fontSize: 14, fontWeight: 600 }}>Reading — Mock 3</span>
          <Badge variant="success">Completed</Badge>
        </div>
        <dl style={{ margin: 0, display: 'grid', gridTemplateColumns: '1fr auto', rowGap: 10, fontSize: 14 }}>
          <dt style={{ color: '#64748b' }}>Submitted</dt>
          <dd style={{ margin: 0, fontWeight: 600 }}>24 Jun 2026</dd>
          <dt style={{ color: '#64748b' }}>Score</dt>
          <dd style={{ margin: 0, fontWeight: 600 }}>38 / 42</dd>
          <dt style={{ color: '#64748b' }}>Band</dt>
          <dd style={{ margin: 0, fontWeight: 600 }}>B</dd>
        </dl>
        <InlineAlert variant="info">Detailed per-question feedback is available in the review view.</InlineAlert>
      </div>
      <div style={{ display: 'flex', gap: 10, marginTop: 24 }}>
        <Button fullWidth onClick={() => {}}>
          Review answers
        </Button>
        <Button variant="ghost" onClick={() => {}}>
          Close
        </Button>
      </div>
    </Drawer>
  </Stage>
);

export const LeftSide = () => (
  <Stage>
    <Drawer open={true} onClose={() => {}} title="Filter cohort" side="left">
      <p style={{ margin: '0 0 16px', fontSize: 14, lineHeight: 1.7, color: '#64748b' }}>
        Narrow the learner list by profession and exam progress before exporting the marking queue.
      </p>
      <div style={{ display: 'flex', gap: 10 }}>
        <Button onClick={() => {}}>Apply filters</Button>
        <Button variant="outline" onClick={() => {}}>
          Reset
        </Button>
      </div>
    </Drawer>
  </Stage>
);
