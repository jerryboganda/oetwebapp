// Authored preview — DataTable (generic). Requires columns (key/header/render),
// data, and keyExtractor. Status column rendered via StatusBadge.
// Each named export = one labeled card cell.
import { DataTable, StatusBadge } from 'oet-with-dr-hesham';

interface Attempt {
  id: string;
  exam: string;
  module: string;
  date: string;
  score: string;
  status: 'completed' | 'in_progress' | 'pending_review' | 'reviewed';
}

const attempts: Attempt[] = [
  { id: 'a-12', exam: 'Reading Mock 12', module: 'Reading', date: '22 Jun 2026', score: '412 · B', status: 'completed' },
  { id: 'a-11', exam: 'Listening Mock 09', module: 'Listening', date: '18 Jun 2026', score: '398 · B', status: 'reviewed' },
  { id: 'a-10', exam: 'Writing Referral Letter 4', module: 'Writing', date: '15 Jun 2026', score: 'Awaiting', status: 'pending_review' },
  { id: 'a-09', exam: 'Speaking Role-play 7', module: 'Speaking', date: '11 Jun 2026', score: '376 · C+', status: 'completed' },
  { id: 'a-08', exam: 'Reading Mock 11', module: 'Reading', date: '08 Jun 2026', score: '—', status: 'in_progress' },
];

const columns = [
  { key: 'exam', header: 'Exam', render: (row: Attempt) => row.exam },
  { key: 'module', header: 'Module', render: (row: Attempt) => row.module },
  { key: 'date', header: 'Date', render: (row: Attempt) => row.date },
  { key: 'score', header: 'Score', render: (row: Attempt) => row.score },
  { key: 'status', header: 'Status', render: (row: Attempt) => <StatusBadge status={row.status} /> },
];

export const RecentAttempts = () => (
  <div style={{ maxWidth: 720 }}>
    <DataTable
      columns={columns}
      data={attempts}
      keyExtractor={(row: Attempt) => row.id}
      aria-label="Recent mock attempts"
    />
  </div>
);

export const Empty = () => (
  <div style={{ maxWidth: 720 }}>
    <DataTable
      columns={columns}
      data={[] as Attempt[]}
      keyExtractor={(row: Attempt) => row.id}
      emptyMessage="No mock attempts yet — start your first Reading mock to see results here."
      aria-label="Recent mock attempts (empty)"
    />
  </div>
);
