// Authored preview — CardFooter (renders a top border + horizontal actions row).
// Each named export = one labeled card cell.
import { Card, CardHeader, CardTitle, CardContent, CardFooter, Button } from 'oet-prep';

export const ExamActions = () => (
  <Card style={{ maxWidth: 380 }}>
    <CardHeader>
      <CardTitle>Speaking — Role-play 2</CardTitle>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#475569' }}>
        You are a nurse reassuring a patient anxious about an upcoming MRI scan. Recordings are
        marked by AI within 24 hours.
      </p>
    </CardContent>
    <CardFooter>
      <Button size="sm">Start recording</Button>
      <Button size="sm" variant="ghost">
        Review later
      </Button>
    </CardFooter>
  </Card>
);

export const SubmitOrCancel = () => (
  <Card style={{ maxWidth: 380 }}>
    <CardHeader>
      <CardTitle>Writing — Submit for marking</CardTitle>
    </CardHeader>
    <CardContent>
      <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#475569' }}>
        Your referral letter is 198 words. Once submitted you cannot edit your response.
      </p>
    </CardContent>
    <CardFooter>
      <Button size="sm" variant="primary">
        Submit letter
      </Button>
      <Button size="sm" variant="ghost">
        Keep editing
      </Button>
    </CardFooter>
  </Card>
);
