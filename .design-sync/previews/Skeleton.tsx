// Authored preview — Skeleton (base loading block).
// Props: variant ('text' | 'rectangle' | 'circle'), width, height, lines, className.
// Each named export = one labeled card cell.
import { Skeleton } from 'oet-prep';

export const Lines = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 360 }}>
    <Skeleton variant="text" width={140} />
    <Skeleton lines={3} />
  </div>
);

export const Variants = () => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
    <Skeleton variant="circle" width={48} height={48} />
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, flex: 1 }}>
      <Skeleton variant="text" width={180} />
      <Skeleton variant="rectangle" width={240} height={12} />
    </div>
  </div>
);

export const Widths = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 360 }}>
    <Skeleton variant="text" width="100%" />
    <Skeleton variant="text" width="75%" />
    <Skeleton variant="text" width="50%" />
  </div>
);
