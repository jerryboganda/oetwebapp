import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * LiveKit (the WebRTC client and its stylesheet) is only needed by a human live-tutor room. An AI Speaking exam or
 * practice card never mounts it, so it must not ride in the AI exam's bundle or on every /speaking route
 * (owner programme 5 Oct 2026, load optimisation). This guard keeps the lazy boundary from being undone by an
 * innocent-looking static import.
 */
const read = (...segments: string[]) => readFileSync(path.join(process.cwd(), ...segments), 'utf8');

describe('LiveKit is loaded only for a live-tutor room', () => {
  it('the AI exam page loads the learner room shell on demand, never statically', () => {
    const page = read('app', '(learner)', 'speaking', 'exam', '[id]', 'page.tsx');

    expect(page).toMatch(/from 'next\/dynamic'/);
    expect(page).toMatch(/import\('@\/components\/domain\/speaking\/LearnerLiveRoomShell'\)/);
    expect(page).not.toMatch(/^import\s[^;]*LearnerLiveRoomShell[^;]*from\s/m);
  });

  it('the /speaking layout does not import the LiveKit stylesheet', () => {
    const layout = read('app', '(learner)', 'speaking', 'layout.tsx');

    expect(layout).not.toMatch(/^import\s+['"]@livekit\/components-styles['"]/m);
  });

  it('the room shell brings its own stylesheet, so the CSS loads with the shell', () => {
    const shell = read('components', 'domain', 'speaking', 'LearnerLiveRoomShell.tsx');

    expect(shell).toMatch(/^import\s+['"]@livekit\/components-styles['"]/m);
  });
});
