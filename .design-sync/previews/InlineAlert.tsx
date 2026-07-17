// Authored preview — InlineAlert. Each named export = one labeled card cell.
import { InlineAlert, Button } from 'oet-with-dr-hesham';

export const Variants = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 460 }}>
    <InlineAlert variant="info">Your speaking session is being marked by AI.</InlineAlert>
    <InlineAlert variant="success">Submission received — results within 24 hours.</InlineAlert>
    <InlineAlert variant="warning">You have 5 minutes remaining in this section.</InlineAlert>
    <InlineAlert variant="error">Audio upload failed. Check your connection and retry.</InlineAlert>
  </div>
);

export const WithTitle = () => (
  <div style={{ maxWidth: 460 }}>
    <InlineAlert variant="warning" title="Microphone not detected">
      We couldn&rsquo;t access your microphone. Grant permission in your browser settings before
      starting the speaking test.
    </InlineAlert>
  </div>
);

export const Dismissible = () => (
  <div style={{ maxWidth: 460 }}>
    <InlineAlert
      variant="info"
      title="New mock exams available"
      dismissible
      action={
        <Button size="sm" variant="outline">
          View mocks
        </Button>
      }
    >
      Three new Reading mock papers were added to your plan this week.
    </InlineAlert>
  </div>
);
