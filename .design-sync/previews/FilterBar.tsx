// Authored preview — FilterBar. Requires groups, selected (Record<string,string[]>),
// onChange, onClear?. The desktop toolbar shows at >=768px; dropdowns are radix
// popovers (closed by default), so the toolbar with filter buttons is the goal.
// Each named export = one labeled card cell.
import { useState } from 'react';
import { FilterBar } from 'oet-with-dr-hesham';

const groups = [
  {
    id: 'module',
    label: 'Module',
    options: [
      { id: 'listening', label: 'Listening', count: 9 },
      { id: 'reading', label: 'Reading', count: 12 },
      { id: 'writing', label: 'Writing', count: 4 },
      { id: 'speaking', label: 'Speaking', count: 7 },
    ],
  },
  {
    id: 'status',
    label: 'Status',
    options: [
      { id: 'completed', label: 'Completed', count: 18 },
      { id: 'in_progress', label: 'In progress', count: 2 },
      { id: 'pending_review', label: 'Pending review', count: 1 },
    ],
  },
];

export const WithSelections = () => {
  const [selected, setSelected] = useState<Record<string, string[]>>({
    module: ['reading', 'listening'],
    status: ['completed'],
  });

  const handleChange = (groupId: string, optionId: string) => {
    setSelected((prev) => {
      const current = prev[groupId] ?? [];
      const next = current.includes(optionId)
        ? current.filter((id) => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: next };
    });
  };

  return (
    <div style={{ minWidth: 480 }}>
      <FilterBar
        groups={groups}
        selected={selected}
        onChange={handleChange}
        onClear={() => setSelected({})}
      />
    </div>
  );
};
