// Authored preview — Card (with its CardHeader / CardTitle / CardContent /
// CardFooter compound parts shown in context). Each named export = one cell.
import { Card, CardHeader, CardTitle, CardContent, CardFooter, Button, Badge } from 'oet-prep';

export const ExamSection = () => (
  <Card style={{ maxWidth: 380 }}>
    <CardHeader>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <CardTitle>Reading — Part B</CardTitle>
        <Badge variant="info">10 min</Badge>
      </div>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6 }}>
        Six short workplace texts — memos, emails and policy notices. Choose the option that
        best matches the writer&rsquo;s intent.
      </p>
    </CardContent>
    <CardFooter>
      <Button size="sm">Begin section</Button>
      <Button size="sm" variant="ghost">
        Review later
      </Button>
    </CardFooter>
  </Card>
);

export const Paddings = () => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12 }}>
    {(['sm', 'md', 'lg'] as const).map((p) => (
      <Card key={p} padding={p} style={{ width: 150 }}>
        <CardTitle>padding="{p}"</CardTitle>
        <p style={{ margin: '6px 0 0', fontSize: 13, color: '#64748b' }}>Compact clinical surface.</p>
      </Card>
    ))}
  </div>
);

export const Hoverable = () => (
  <Card hoverable style={{ maxWidth: 320, cursor: 'pointer' }}>
    <CardTitle>Listening practice</CardTitle>
    <p style={{ margin: '6px 0 0', fontSize: 14, color: '#64748b' }}>
      Hover to lift — used for clickable navigation tiles across the dashboard.
    </p>
  </Card>
);
