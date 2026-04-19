import type { ReactNode } from 'react';

/**
 * Visually-hidden tabular fallback for charts. Screen readers read the table;
 * sighted users see the chart. Same source of truth, zero duplication.
 */
export function ChartTabularFallback({
  caption,
  headers,
  rows,
}: {
  caption: string;
  headers: string[];
  rows: (string | number | null)[][];
}): ReactNode {
  return (
    <table
      className="sr-only absolute"
      aria-label={caption}
      // Keep the table in the a11y tree; CSS `.sr-only` removes visual paint.
    >
      <caption>{caption}</caption>
      <thead>
        <tr>
          {headers.map((h) => (
            <th key={h} scope="col">
              {h}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, i) => (
          <tr key={i}>
            {row.map((cell, j) => (
              <td key={j}>{cell === null || cell === undefined ? '—' : cell}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
