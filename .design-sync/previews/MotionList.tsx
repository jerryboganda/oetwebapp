// Authored preview — MotionList. An entrance-animation wrapper for a list
// container (surface="list"); typically wraps several MotionItem children that
// stagger in. Motion is forced to final state globally, so children render
// visibly. Give it real list children.
import { MotionList, MotionItem, Card, Badge } from 'oet-prep';

const tasks = [
  { name: 'Speaking — Role-play: Reassure an anxious patient', tag: 'AI-marked', variant: 'violet' as const },
  { name: 'Writing — Discharge summary', tag: 'Tutor', variant: 'indigo' as const },
  { name: 'Reading — Part A skim & scan', tag: '15 min', variant: 'sky' as const },
  { name: 'Listening — Part C extended monologue', tag: '12 min', variant: 'info' as const },
];

export const StudyQueue = () => (
  <MotionList style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 420 }}>
    {tasks.map((task, i) => (
      <MotionItem key={task.name} delayIndex={i}>
        <Card padding="sm">
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <span style={{ fontSize: 14, fontWeight: 600, color: '#0f172a' }}>{task.name}</span>
            <Badge variant={task.variant} size="sm">{task.tag}</Badge>
          </div>
        </Card>
      </MotionItem>
    ))}
  </MotionList>
);

export const PlainTextRows = () => (
  <MotionList style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 380 }}>
    {[
      'Completed 3 of 4 Reading mock papers',
      'Predicted overall band: B',
      'Next milestone: Speaking exam in 5 days',
    ].map((line, i) => (
      <MotionItem key={line} delayIndex={i}>
        <div
          style={{
            fontSize: 14,
            color: '#334155',
            padding: '10px 14px',
            background: '#f8fafc',
            border: '1px solid #e2e8f0',
            borderRadius: 8,
          }}
        >
          {line}
        </div>
      </MotionItem>
    ))}
  </MotionList>
);
