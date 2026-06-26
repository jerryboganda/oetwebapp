// Authored preview — CircularProgress. Each named export = one labeled card cell.
// Real numeric `value` is essential — defaults render "NaN%".
import { CircularProgress } from 'oet-prep';

export const Values = () => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 32, alignItems: 'center' }}>
    <CircularProgress value={72} />
    <CircularProgress value={94} />
  </div>
);

export const WithLabels = () => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 32, alignItems: 'center' }}>
    <CircularProgress value={88} label="Overall band" sublabel="Predicted grade B" />
    <CircularProgress value={61} label="Listening" sublabel="Part B in progress" />
  </div>
);

export const SizeAndColor = () => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 32, alignItems: 'center' }}>
    <CircularProgress value={76} size={96} strokeWidth={6} label="Compact" />
    <CircularProgress value={90} size={140} strokeWidth={10} color="#10b981" label="Speaking" sublabel="AI-marked" />
  </div>
);
