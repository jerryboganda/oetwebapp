// Authored preview — CardTitle (the Card heading; renders an <h3>).
// Each named export = one labeled card cell.
import { Card, CardHeader, CardTitle, CardContent } from 'oet-with-dr-hesham';

export const SectionHeading = () => (
  <Card style={{ maxWidth: 360 }}>
    <CardHeader>
      <CardTitle>Listening — Part A</CardTitle>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#475569' }}>
        Note-completion tasks from two recorded consultations.
      </p>
    </CardContent>
  </Card>
);

export const ModuleTitles = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
    {(['Reading', 'Listening', 'Speaking', 'Writing'] as const).map((m) => (
      <Card key={m} style={{ width: 280 }}>
        <CardTitle>{m} — Practice</CardTitle>
      </Card>
    ))}
  </div>
);
