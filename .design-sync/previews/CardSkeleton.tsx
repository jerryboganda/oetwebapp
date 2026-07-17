// Authored preview — CardSkeleton (card-shaped loading placeholder).
// Each named export = one labeled card cell.
import { CardSkeleton } from 'oet-with-dr-hesham';

export const Single = () => (
  <div style={{ maxWidth: 360 }}>
    <CardSkeleton />
  </div>
);

export const Grid = () => (
  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 16, maxWidth: 720 }}>
    <CardSkeleton />
    <CardSkeleton />
  </div>
);
