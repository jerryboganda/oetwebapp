// Authored preview — ScoreRangeBadge. Each named export = one labeled card cell.
import { ScoreRangeBadge } from 'oet-with-dr-hesham';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>{children}</div>
);

export const GradeBands = () => (
  <Row>
    <ScoreRangeBadge low={450} high={500} label="Grade A" />
    <ScoreRangeBadge low={350} high={440} label="Grade B" />
    <ScoreRangeBadge low={300} high={340} label="Grade C+" />
    <ScoreRangeBadge low={200} high={290} label="Grade C" />
  </Row>
);

export const WithoutLabel = () => (
  <Row>
    <ScoreRangeBadge low={350} high={440} />
    <ScoreRangeBadge low={450} high={500} />
  </Row>
);

export const InResultRow = () => (
  <div
    style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 12,
      maxWidth: 360,
      padding: '10px 14px',
      border: '1px solid #e2e8f0',
      borderRadius: 10,
    }}
  >
    <span style={{ fontSize: 14, fontWeight: 600, color: '#0f172a' }}>Listening — Mock 3</span>
    <ScoreRangeBadge low={350} high={440} label="Grade B" />
  </div>
);
