// Authored preview — MotionItem. An entrance-animation wrapper for a single
// list row / element (surface="item"). Motion is forced to final state globally,
// so children render visibly. Give it real, meaningful children.
import { MotionItem, Card, CardTitle, Badge } from 'oet-prep';

export const SingleRow = () => (
  <MotionItem>
    <Card style={{ maxWidth: 360 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <CardTitle>Listening — Part A</CardTitle>
        <Badge variant="success">Completed</Badge>
      </div>
      <p style={{ margin: '6px 0 0', fontSize: 14, color: '#64748b' }}>
        Two consultations with note-completion gaps. Scored 11/12.
      </p>
    </Card>
  </MotionItem>
);

export const StaggeredByIndex = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 360 }}>
    {[
      { label: 'Reading — Part B', delayIndex: 0 },
      { label: 'Reading — Part C', delayIndex: 1 },
      { label: 'Writing — Referral letter', delayIndex: 2 },
    ].map((row) => (
      <MotionItem key={row.label} delayIndex={row.delayIndex}>
        <Card padding="sm">
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <span style={{ fontSize: 14, fontWeight: 600, color: '#0f172a' }}>{row.label}</span>
            <Badge variant="info" size="sm">10 min</Badge>
          </div>
        </Card>
      </MotionItem>
    ))}
  </div>
);
