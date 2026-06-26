// design-sync PREVIEW-ONLY provider.
//
// The DS components animate in from opacity:0 via motion/react (getSurfaceMotion
// / getCelebrateMotion). In a static screenshot the enter animation hasn't run,
// so the card captures blank. Setting MotionGlobalConfig.skipAnimations renders
// every motion element at its final ("animate") state immediately — motion's
// documented mechanism for tests/screenshots.
//
// IMPORTANT: the flag is set inside the render BODY, not at module load. This
// wrapper is mounted ONLY by the preview cards (cfg.provider). Designs built
// with the DS import the components directly and never mount this provider, so
// production motion in _ds_bundle.js is completely untouched.
import { MotionGlobalConfig } from 'motion/react';
import type { ReactNode } from 'react';

export function DsPreviewProvider({ children }: { children: ReactNode }) {
  MotionGlobalConfig.skipAnimations = true;
  return <>{children}</>;
}
