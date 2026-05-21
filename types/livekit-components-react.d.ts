/**
 * Ambient declaration for the optional `@livekit/components-react`
 * dependency. The package is lazy-loaded at runtime via dynamic
 * `import('@livekit/components-react')` in
 * `components/domain/speaking/Live{Tutor,Learner}LiveRoomShell.tsx`.
 *
 * Until the package is added to package.json, this declaration keeps
 * `tsc --noEmit` clean. When the real package is installed, this file
 * becomes a no-op (the real types from node_modules win).
 */
declare module '@livekit/components-react';
