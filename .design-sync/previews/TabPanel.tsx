// Authored preview — TabPanel, shown inside a controlled Tabs. TabPanel renders
// its children only when `id === activeTab`. Each named export = one cell.
import { useState } from 'react';
import { Tabs, TabPanel, Card, CardTitle, Badge } from 'oet-with-dr-hesham';

export const WithTabs = () => {
  const [active, setActive] = useState('overview');
  const tabs = [
    { id: 'overview', label: 'Overview' },
    { id: 'attempts', label: 'Attempts', count: 7 },
    { id: 'feedback', label: 'Feedback', count: 3 },
  ];

  return (
    <div style={{ maxWidth: 520 }}>
      <Tabs tabs={tabs} activeTab={active} onChange={setActive} />

      <div style={{ marginTop: 14 }}>
        <TabPanel id="overview" activeTab={active}>
          <Card padding="md">
            <CardTitle>Reading — overall progress</CardTitle>
            <p style={{ margin: '8px 0 0', fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
              Average score 412 (band B). You are 18 points from your target of 430.
            </p>
          </Card>
        </TabPanel>

        <TabPanel id="attempts" activeTab={active}>
          <Card padding="md">
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
              <CardTitle>Recent attempts</CardTitle>
              <Badge variant="info">7 total</Badge>
            </div>
            <p style={{ margin: '8px 0 0', fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
              Reading Mock 12 — 22 Jun — 412. Reading Mock 11 — 15 Jun — 398.
            </p>
          </Card>
        </TabPanel>

        <TabPanel id="feedback" activeTab={active}>
          <Card padding="md">
            <CardTitle>Tutor feedback</CardTitle>
            <p style={{ margin: '8px 0 0', fontSize: 14, lineHeight: 1.6, color: '#334155' }}>
              &ldquo;Strong skimming in Part A. Slow down on Part C — read the question stem before
              the matching options.&rdquo;
            </p>
          </Card>
        </TabPanel>
      </div>
    </div>
  );
};
