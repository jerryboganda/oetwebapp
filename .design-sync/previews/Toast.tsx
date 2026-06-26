// Authored preview — Toast. Toast is position:fixed (viewport bottom-right), so
// each cell wraps it in a sized, transformed Stage that becomes the containing
// block for the fixed element — the toast then renders INSIDE the card instead
// of escaping to the page viewport. duration={0} disables the auto-dismiss timer.
import { Toast } from 'oet-prep';
import type { ReactNode } from 'react';

const Stage = ({ children }: { children: ReactNode }) => (
  <div style={{ position: 'relative', transform: 'translateZ(0)', height: 150, width: '100%', overflow: 'hidden' }}>
    {children}
  </div>
);

export const Success = () => (
  <Stage>
    <Toast variant="success" message="Submission received — results within 24 hours." duration={0} onClose={() => {}} />
  </Stage>
);

export const Error = () => (
  <Stage>
    <Toast variant="error" message="Audio upload failed. Check your connection and retry." duration={0} onClose={() => {}} />
  </Stage>
);

export const Info = () => (
  <Stage>
    <Toast variant="info" message="Your speaking session is now being marked by AI." duration={0} />
  </Stage>
);
