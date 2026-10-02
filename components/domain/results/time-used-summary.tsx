import { Badge } from '@/components/ui/badge';
import { cardClassName } from '@/components/ui/card';

interface TimeUsedSection {
  label: string;
  milliseconds: number | null;
}

interface TimeUsedSummaryProps {
  totalMilliseconds: number | null;
  sections: TimeUsedSection[];
  description?: string;
}

function formatTime(milliseconds: number | null) {
  if (milliseconds == null || !Number.isFinite(milliseconds) || milliseconds < 0) {
    return 'Not recorded';
  }

  const totalSeconds = Math.max(0, Math.round(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
    : `${minutes}:${String(seconds).padStart(2, '0')}`;
}

export function TimeUsedSummary({ totalMilliseconds, sections, description }: TimeUsedSummaryProps) {
  return (
    <section
      className={cardClassName({})}
      aria-labelledby="time-used-summary-title"
      data-testid="time-used-summary"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="eyebrow text-muted">Detailed analytics</p>
          <h2 id="time-used-summary-title" className="mt-1 text-base font-bold text-navy">Time used</h2>
        </div>
        <Badge variant="muted" size="md" className="tabular-nums">
          Total {formatTime(totalMilliseconds)}
        </Badge>
      </div>

      {description ? <p className="mt-3 text-sm leading-6 text-muted">{description}</p> : null}

      <div className="mt-4 overflow-hidden rounded-xl border border-border">
        <table className="w-full text-start text-sm">
          <caption className="sr-only">Time used by section and total</caption>
          <thead className="bg-background-light eyebrow text-muted">
            <tr>
              <th scope="col" className="px-4 py-3 text-start">Section</th>
              <th scope="col" className="px-4 py-3 text-end">Time used</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {sections.map((section) => (
              <tr key={section.label}>
                <th scope="row" className="px-4 py-3 text-start font-semibold text-navy">{section.label}</th>
                <td className="px-4 py-3 text-end font-bold tabular-nums text-muted">
                  {formatTime(section.milliseconds)}
                </td>
              </tr>
            ))}
            <tr className="bg-primary/5">
              <th scope="row" className="px-4 py-3 text-start font-bold text-navy">Total</th>
              <td className="px-4 py-3 text-end font-bold tabular-nums text-primary">
                {formatTime(totalMilliseconds)}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  );
}
