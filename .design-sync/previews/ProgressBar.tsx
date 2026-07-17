// Authored preview — ProgressBar. Each named export = one labeled card cell.
// Real numeric `value` is essential — defaults render an empty bar.
import { ProgressBar } from 'oet-with-dr-hesham';

export const FillLevels = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 420 }}>
    <ProgressBar value={25} />
    <ProgressBar value={60} />
    <ProgressBar value={90} />
  </div>
);

export const Labeled = () => (
  <div style={{ maxWidth: 420 }}>
    <ProgressBar value={68} label="Reading study path" showValue />
  </div>
);

export const Colors = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 420 }}>
    <ProgressBar value={82} label="Overall readiness" color="primary" showValue />
    <ProgressBar value={94} label="Listening accuracy" color="success" showValue />
    <ProgressBar value={45} label="Writing tasks completed" color="warning" showValue />
    <ProgressBar value={18} label="Mock exams remaining" color="danger" showValue />
  </div>
);

export const Sizes = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 420 }}>
    <ProgressBar value={72} label="Section progress (sm)" size="sm" showValue />
    <ProgressBar value={72} label="Section progress (md)" size="md" showValue />
  </div>
);
