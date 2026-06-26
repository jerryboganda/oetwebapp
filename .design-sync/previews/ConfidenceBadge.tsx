// Authored preview — ConfidenceBadge. Each named export = one labeled card cell.
import { ConfidenceBadge } from 'oet-prep';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>{children}</div>
);

export const Levels = () => (
  <Row>
    <ConfidenceBadge level="high" />
    <ConfidenceBadge level="medium" />
    <ConfidenceBadge level="low" />
  </Row>
);

export const OnAiFeedback = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 380 }}>
    {(
      [
        ['Grammar & sentence structure', 'high'],
        ['Vocabulary range', 'medium'],
        ['Intelligibility of pronunciation', 'low'],
      ] as const
    ).map(([criterion, level]) => (
      <div
        key={criterion}
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
        <span style={{ fontSize: 14, color: '#0f172a' }}>{criterion}</span>
        <ConfidenceBadge level={level} />
      </div>
    ))}
  </div>
);
