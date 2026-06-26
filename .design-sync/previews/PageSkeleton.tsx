// Authored preview — PageSkeleton (full-page loading scaffold:
// header block with avatar/title/actions + a 3-up grid of CardSkeletons).
// Each named export = one labeled card cell.
import { PageSkeleton } from 'oet-prep';

export const FullPage = () => (
  <div style={{ maxWidth: 960 }}>
    <PageSkeleton />
  </div>
);
