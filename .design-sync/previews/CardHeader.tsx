// Authored preview — CardHeader (shown inside a Card, its only valid context).
// Each named export = one labeled card cell.
import { Card, CardHeader, CardTitle, CardContent, Badge } from 'oet-prep';

export const ListeningSection = () => (
  <Card style={{ maxWidth: 380 }}>
    <CardHeader>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <CardTitle>Listening — Part A</CardTitle>
        <Badge variant="info">5 min</Badge>
      </div>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6 }}>
        A consultation between a patient and a healthcare professional. Complete the notes as you
        listen to the recording.
      </p>
    </CardContent>
  </Card>
);

export const WithSubtitle = () => (
  <Card style={{ maxWidth: 380 }}>
    <CardHeader>
      <CardTitle>Mock Exam 3 — Reading</CardTitle>
      <p style={{ margin: '4px 0 0', fontSize: 13, color: '#64748b' }}>
        60 minutes · Parts A, B and C · Auto-marked
      </p>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6 }}>
        Resume where you left off. Your progress is saved after every question.
      </p>
    </CardContent>
  </Card>
);
