// Authored preview — StatCard. Props: label, value, icon?, trend?, hint?,
// sparklineData?, tone?. Each named export = one labeled card cell.
import { StatCard } from 'oet-with-dr-hesham';
import { TrendingUp, CheckCircle2, Clock, Flame } from 'lucide-react';

export const DashboardStats = () => (
  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(180px, 1fr))', gap: 12, maxWidth: 560 }}>
    <StatCard
      label="Average score"
      value="412"
      icon={<TrendingUp />}
      trend={{ value: '+14', label: 'vs last cycle', direction: 'up' }}
      hint="Band B — 18 points to your target of 430"
      sparklineData={[372, 384, 391, 398, 405, 412]}
    />
    <StatCard
      label="Mocks completed"
      value="7"
      tone="success"
      icon={<CheckCircle2 />}
      trend={{ value: '+3', label: 'this week', direction: 'up' }}
    />
    <StatCard
      label="Awaiting review"
      value="2"
      tone="warning"
      icon={<Clock />}
      trend={{ value: 'No change', direction: 'neutral' }}
      hint="1 Writing referral letter, 1 Speaking role-play"
    />
    <StatCard
      label="Study streak"
      value="12 days"
      tone="info"
      icon={<Flame />}
      trend={{ value: '+5 days', direction: 'up' }}
    />
  </div>
);

export const SingleStat = () => (
  <div style={{ width: 220 }}>
    <StatCard
      label="Listening accuracy"
      value="86%"
      icon={<TrendingUp />}
      trend={{ value: '+4%', label: 'Part B', direction: 'up' }}
      sparklineData={[74, 78, 80, 82, 84, 86]}
    />
  </div>
);
