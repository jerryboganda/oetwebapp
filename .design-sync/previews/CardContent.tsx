// Authored preview — CardContent (the Card body region).
// Each named export = one labeled card cell.
import { Card, CardHeader, CardTitle, CardContent } from 'oet-with-dr-hesham';

export const Paragraph = () => (
  <Card style={{ maxWidth: 400 }}>
    <CardHeader>
      <CardTitle>Writing — Referral Letter</CardTitle>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
        Mrs Patel, a 68-year-old patient, has been under your care for poorly controlled type 2
        diabetes. Write a referral letter to the endocrinology clinic summarising her history,
        current medication and reason for referral.
      </p>
    </CardContent>
  </Card>
);

export const StatList = () => (
  <Card style={{ maxWidth: 320 }}>
    <CardHeader>
      <CardTitle>Your Reading progress</CardTitle>
    </CardHeader>
    <CardContent>
      <dl style={{ margin: 0, display: 'grid', gridTemplateColumns: '1fr auto', rowGap: 10, fontSize: 14 }}>
        <dt style={{ color: '#64748b' }}>Papers completed</dt>
        <dd style={{ margin: 0, fontWeight: 700, color: '#1e293b' }}>12</dd>
        <dt style={{ color: '#64748b' }}>Average band</dt>
        <dd style={{ margin: 0, fontWeight: 700, color: '#1e293b' }}>B (350)</dd>
        <dt style={{ color: '#64748b' }}>Best section</dt>
        <dd style={{ margin: 0, fontWeight: 700, color: '#1e293b' }}>Part B</dd>
      </dl>
    </CardContent>
  </Card>
);
