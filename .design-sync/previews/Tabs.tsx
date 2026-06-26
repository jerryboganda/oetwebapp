// Authored preview — Tabs. Controlled component (activeTab + onChange).
// Each named export = one labeled card cell.
import { useState } from 'react';
import { Tabs } from 'oet-prep';

export const ResultTabs = () => {
  const [active, setActive] = useState('overview');
  const tabs = [
    { id: 'overview', label: 'Overview' },
    { id: 'attempts', label: 'Attempts', count: 7 },
    { id: 'feedback', label: 'Feedback', count: 3 },
  ];

  const content: Record<string, string> = {
    overview:
      'Your latest Reading mock scored 412 — band B. Strongest in Part A skimming; review Part C inference questions.',
    attempts: 'You have completed 7 Reading mocks this cycle. Most recent: Reading Mock 12 on 22 Jun.',
    feedback: 'Your tutor left 3 written notes and one voice note on your last Writing referral letter.',
  };

  return (
    <div style={{ maxWidth: 520 }}>
      <Tabs tabs={tabs} activeTab={active} onChange={setActive} />
      <p style={{ margin: '14px 4px 0', fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
        {content[active]}
      </p>
    </div>
  );
};

export const ModuleTabs = () => {
  const [active, setActive] = useState('listening');
  const tabs = [
    { id: 'listening', label: 'Listening' },
    { id: 'reading', label: 'Reading' },
    { id: 'writing', label: 'Writing' },
    { id: 'speaking', label: 'Speaking' },
  ];
  return (
    <div style={{ maxWidth: 520 }}>
      <Tabs tabs={tabs} activeTab={active} onChange={setActive} />
    </div>
  );
};
