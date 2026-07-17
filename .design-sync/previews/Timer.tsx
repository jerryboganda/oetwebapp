// Authored preview — Timer. Each named export = one labeled card cell.
// Provide real numeric time values via `initialSeconds`. Rendered paused
// (running={false}) so the preview shows a stable, deterministic value.
import { Timer } from 'oet-with-dr-hesham';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'center' }}>{children}</div>
);

export const ExamCountdown = () => (
  <Row>
    {/* Reading section: 45 minutes remaining */}
    <Timer mode="countdown" initialSeconds={2700} running={false} />
    {/* Speaking section: 20 minutes remaining */}
    <Timer mode="countdown" initialSeconds={1200} running={false} size="lg" />
  </Row>
);

export const WarningAndElapsed = () => (
  <Row>
    {/* Under 5 minutes — warning styling */}
    <Timer mode="countdown" initialSeconds={180} running={false} />
    {/* Time elapsed during a practice attempt */}
    <Timer mode="elapsed" initialSeconds={0} running={false} size="sm" />
  </Row>
);
