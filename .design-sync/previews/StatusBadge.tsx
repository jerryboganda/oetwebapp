// Authored preview — StatusBadge. Each named export = one labeled card cell.
import { StatusBadge } from 'oet-prep';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>{children}</div>
);

export const AttemptLifecycle = () => (
  <Row>
    <StatusBadge status="not_started" />
    <StatusBadge status="in_progress" />
    <StatusBadge status="completed" />
    <StatusBadge status="failed" />
  </Row>
);

export const MarkingPipeline = () => (
  <Row>
    <StatusBadge status="queued" />
    <StatusBadge status="processing" />
    <StatusBadge status="pending_review" />
    <StatusBadge status="reviewed" />
  </Row>
);

export const InExamList = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 360 }}>
    {(
      [
        ['Reading — Part A', 'completed'],
        ['Listening — Part B', 'in_progress'],
        ['Writing — Referral letter', 'pending_review'],
        ['Speaking — Role-play', 'not_started'],
      ] as const
    ).map(([title, status]) => (
      <div
        key={title}
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 12,
          padding: '8px 12px',
          border: '1px solid #e2e8f0',
          borderRadius: 10,
        }}
      >
        <span style={{ fontSize: 14, color: '#0f172a' }}>{title}</span>
        <StatusBadge status={status} />
      </div>
    ))}
  </div>
);
