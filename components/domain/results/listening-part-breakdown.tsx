import type { ListeningReviewItemDto } from '@/lib/listening-api';

type ListeningBreakdownItem = Pick<ListeningReviewItemDto, 'partCode' | 'isCorrect' | 'isInvalid' | 'learnerAnswer'>;

function topLevelPart(partCode: string | null | undefined): 'A' | 'B' | 'C' | null {
  const code = (partCode ?? '').trim().toUpperCase();
  if (code.startsWith('A')) return 'A';
  if (code.startsWith('C')) return 'C';
  if (code.startsWith('B')) return 'B';
  return null;
}

function isUnanswered(answer: string | null | undefined): boolean {
  return answer == null || answer.trim().length === 0;
}

/** Candidate-facing Listening Part A/B/C score breakdown. */
export function ListeningPartBreakdown({ items }: { items: ListeningBreakdownItem[] }) {
  const rows = (['A', 'B', 'C'] as const).map((part) => {
    const partItems = items.filter((item) => topLevelPart(item.partCode) === part);
    const invalid = partItems.filter((item) => item.isInvalid === true).length;
    const unanswered = partItems.filter((item) => isUnanswered(item.learnerAnswer)).length;
    const correct = partItems.filter((item) => item.isInvalid !== true && item.isCorrect && !isUnanswered(item.learnerAnswer)).length;
    const scoredTotal = Math.max(0, partItems.length - invalid);
    const incorrect = Math.max(0, scoredTotal - correct - unanswered);
    const percentage = scoredTotal > 0 ? Math.round((correct / scoredTotal) * 100) : 0;
    return { part, total: partItems.length, correct, incorrect, unanswered, invalid, percentage };
  });

  return (
    <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm" aria-label="Listening part breakdown">
      <div className="mb-4">
        <p className="text-xs font-black uppercase tracking-widest text-muted">Part breakdown</p>
        <h2 className="mt-1 text-base font-black text-navy">Listening accuracy by part</h2>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[520px] text-left text-sm">
          <thead>
            <tr className="border-b border-border text-xs font-black uppercase tracking-widest text-muted">
              <th className="pb-3 pr-4">Part</th>
              <th className="pb-3 pr-4 text-right">Correct</th>
              <th className="pb-3 pr-4 text-right">Incorrect</th>
              <th className="pb-3 pr-4 text-right">Unanswered</th>
              <th className="pb-3 pr-4 text-right">Invalid review</th>
              <th className="pb-3 text-right">Accuracy</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.part} className="border-b border-border/70 last:border-0">
                <th scope="row" className="py-3 pr-4 font-bold text-navy">Part {row.part}</th>
                <td className="py-3 pr-4 text-right font-semibold text-success">{row.correct}/{row.total}</td>
                <td className="py-3 pr-4 text-right font-semibold text-danger">{row.incorrect}</td>
                <td className="py-3 pr-4 text-right font-semibold text-warning">{row.unanswered}</td>
                <td className="py-3 pr-4 text-right font-semibold text-warning">{row.invalid}</td>
                <td className="py-3 text-right font-bold tabular-nums text-navy">{row.percentage}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
