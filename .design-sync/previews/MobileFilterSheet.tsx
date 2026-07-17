// Authored preview — MobileFilterSheet. OVERLAY component (single-card mode,
// already configured, 440x720 viewport). NOTE: this component manages its own
// `open` state internally (starts closed), so it statically renders only the
// filter TRIGGER toolbar (the "Filters" button + selection badge + Clear). The
// Drawer sheet with the option groups only appears after the trigger is clicked.
// We seed realistic OET filter groups + a `selected` map so the trigger shows
// its active-state count badge and the Clear control.
import { MobileFilterSheet } from 'oet-with-dr-hesham';

const groups = [
  {
    id: 'module',
    label: 'Module',
    options: [
      { id: 'reading', label: 'Reading', count: 12 },
      { id: 'listening', label: 'Listening', count: 9 },
      { id: 'speaking', label: 'Speaking', count: 6 },
      { id: 'writing', label: 'Writing', count: 8 },
    ],
  },
  {
    id: 'status',
    label: 'Status',
    options: [
      { id: 'not-started', label: 'Not started', count: 14 },
      { id: 'in-progress', label: 'In progress', count: 5 },
      { id: 'completed', label: 'Completed', count: 21 },
    ],
  },
];

export const WithSelection = () => (
  <MobileFilterSheet
    groups={groups}
    selected={{ module: ['reading', 'listening'], status: ['in-progress'] }}
    onChange={() => {}}
    onClear={() => {}}
  />
);

export const Empty = () => (
  <MobileFilterSheet groups={groups} selected={{}} onChange={() => {}} onClear={() => {}} />
);
