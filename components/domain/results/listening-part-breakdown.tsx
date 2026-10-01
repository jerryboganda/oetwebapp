import { cardClassName } from '@/components/ui/card';
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
    <section className={cardClassName({})} aria-label="Listening part breakdown">
      <div className="mb-4">
        <p className="eyebrow text-muted">Part breakdown</p>
        <h2 className="mt-1 text-base font-bold text-navy">Listening accuracy by part</h2>
      </div>
      {/* Wide table: scrolls inside the card on a phone. */}
      <div className="overflow-x-auto">
        <table className="w-full min-w-[520px] text-start text-sm tabular-nums">
          <thead>
            <tr className="border-b border-border eyebrow text-muted">
              <th className="pb-3 pe-4 text-start">Part</th>
              <th className="pb-3 pe-4 text-end">Correct</th>
              <th className="pb-3 pe-4 text-end">Incorrect</th>
              <th className="pb-3 pe-4 text-end">Unanswered</th>
              <th className="pb-3 pe-4 text-end">Invalid review</th>
              <th className="pb-3 text-end">Accuracy</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.part} className="border-b border-border/70 last:border-0">
                <th scope="row" className="py-3 pe-4 text-start font-bold text-navy">Part {row.part}</th>
                <td className="py-3 pe-4 text-end font-semibold text-success-strong">{row.correct}/{row.total}</td>
                <td className="py-3 pe-4 text-end font-semibold text-danger-strong">{row.incorrect}</td>
                <td className="py-3 pe-4 text-end font-semibold text-warning-strong">{row.unanswered}</td>
                <td className="py-3 pe-4 text-end font-semibold text-warning-strong">{row.invalid}</td>
                <td className="py-3 text-end font-bold text-navy">{row.percentage}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
