// Authored preview — MotionPage. An entrance-animation wrapper for a whole
// route/page (surface="route"). Motion is forced to final state globally, so
// children render visibly. Give it real, meaningful page-level children.
import { MotionPage, Card, CardTitle, Button, Badge } from 'oet-with-dr-hesham';

export const PageEnter = () => (
  <MotionPage style={{ maxWidth: 520 }}>
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <h2 style={{ margin: 0, fontSize: 22, fontWeight: 700, color: '#0f172a' }}>
          OET Reading
        </h2>
        <Badge variant="info">4 papers available</Badge>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
        <Card hoverable style={{ cursor: 'pointer' }}>
          <CardTitle>Part A</CardTitle>
          <p style={{ margin: '6px 0 0', fontSize: 13, color: '#64748b' }}>Expeditious reading &middot; 15 min</p>
        </Card>
        <Card hoverable style={{ cursor: 'pointer' }}>
          <CardTitle>Part B &amp; C</CardTitle>
          <p style={{ margin: '6px 0 0', fontSize: 13, color: '#64748b' }}>Careful reading &middot; 45 min</p>
        </Card>
      </div>
      <div>
        <Button size="lg">Begin full exam</Button>
      </div>
    </div>
  </MotionPage>
);
