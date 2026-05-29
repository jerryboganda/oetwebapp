'use client';

interface HeatmapDay {
  date: string;
  minutesPracticed: number;
}

interface ActivityHeatmapProps {
  days: HeatmapDay[];
}

function getColor(minutes: number): string {
  if (minutes === 0) return 'bg-border';
  if (minutes < 15) return 'bg-violet-200 dark:bg-violet-800';
  if (minutes < 30) return 'bg-violet-400 dark:bg-violet-600';
  return 'bg-violet-600 dark:bg-violet-400';
}

const DAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

export function ActivityHeatmap({ days }: ActivityHeatmapProps) {
  // Build a map for quick lookup
  const minutesByDate: Record<string, number> = {};
  for (const d of days) {
    minutesByDate[d.date.slice(0, 10)] = d.minutesPracticed;
  }

  // Use the last 90 days
  const today = new Date();
  const startDate = new Date(today);
  startDate.setDate(today.getDate() - 89);

  // Pad so grid starts on a Sunday
  const startDow = startDate.getDay();
  const paddedDays: Array<{ date: string | null; minutes: number }> = [];
  for (let p = 0; p < startDow; p++) {
    paddedDays.push({ date: null, minutes: 0 });
  }
  for (let i = 0; i < 90; i++) {
    const d = new Date(startDate);
    d.setDate(startDate.getDate() + i);
    const iso = d.toISOString().slice(0, 10);
    paddedDays.push({ date: iso, minutes: minutesByDate[iso] ?? 0 });
  }

  // Build weeks (columns of 7)
  const weeks: Array<typeof paddedDays> = [];
  for (let i = 0; i < paddedDays.length; i += 7) {
    weeks.push(paddedDays.slice(i, i + 7));
  }

  return (
    <div className="overflow-x-auto">
      {/* Day-of-week row */}
      <div className="flex gap-1 mb-1 pl-0">
        {DAY_LABELS.map((label) => (
          <div key={label} className="w-4 text-center text-xs text-muted" style={{ minWidth: '1rem' }}>
            {label[0]}
          </div>
        ))}
      </div>
      {/* Grid: rows = day-of-week, columns = weeks */}
      <div className="flex gap-1">
        {weeks.map((week, wi) => (
          <div key={wi} className="flex flex-col gap-1">
            {week.map((cell, di) =>
              cell.date ? (
                <div
                  key={cell.date}
                  className={`w-4 h-4 rounded-sm ${getColor(cell.minutes)}`}
                  title={`${cell.date}: ${cell.minutes} min`}
                  aria-label={`${cell.date}: ${cell.minutes} minutes practiced`}
                />
              ) : (
                <div key={`pad-${wi}-${di}`} className="w-4 h-4" />
              ),
            )}
          </div>
        ))}
      </div>
      {/* Legend */}
      <div className="flex items-center gap-2 mt-3 text-xs text-muted">
        <span>Less</span>
        <div className="w-3 h-3 rounded-sm bg-border" />
        <div className="w-3 h-3 rounded-sm bg-violet-200 dark:bg-violet-800" />
        <div className="w-3 h-3 rounded-sm bg-violet-400 dark:bg-violet-600" />
        <div className="w-3 h-3 rounded-sm bg-violet-600 dark:bg-violet-400" />
        <span>More</span>
      </div>
    </div>
  );
}
