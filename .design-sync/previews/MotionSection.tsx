// Authored preview — MotionSection. An entrance-animation wrapper for a page
// section (surface="section"). Motion is forced to final state globally, so
// children render visibly. Give it real, meaningful children.
import { MotionSection, Card, CardHeader, CardTitle, CardContent, Button, Badge } from 'oet-with-dr-hesham';

export const DashboardSection = () => (
  <MotionSection style={{ maxWidth: 460 }}>
    <Card>
      <CardHeader>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
          <CardTitle>This week&rsquo;s focus</CardTitle>
          <Badge variant="violet">Speaking</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
          You scored below grade B on patient-reassurance role-plays. We&rsquo;ve queued three
          targeted scenarios with AI feedback on fluency and clinical empathy.
        </p>
        <div style={{ marginTop: 14 }}>
          <Button size="sm">Start practice</Button>
        </div>
      </CardContent>
    </Card>
  </MotionSection>
);

export const DelayedReveal = () => (
  <MotionSection delay={0.1} style={{ maxWidth: 460 }}>
    <Card padding="lg">
      <CardTitle>Mock exam results</CardTitle>
      <p style={{ margin: '8px 0 0', fontSize: 14, color: '#64748b' }}>
        Reading 89% &middot; Listening 92% &middot; Writing pending tutor review.
      </p>
    </Card>
  </MotionSection>
);
