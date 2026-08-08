# OET Prep Quick Fixes v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the PDF's video controls, download-badge consistency, and temporary direct iOS download path.

**Architecture:** Keep the existing first-party video container and watermark layering as the security boundary. Add scoped presentation state and fullscreen compatibility in the player, extend the existing trusted release resolver for IPA assets, and centralize consistent badge sizing through shared classes at the existing download surfaces.

**Tech Stack:** Next.js 16 App Router, React 19, TypeScript strict mode, Tailwind CSS v4, lucide-react, Vitest, React Testing Library.

## Global Constraints

- The fullscreen element is the first-party video container so the forensic watermark remains visible.
- Raw HLS/MP4 URLs, playback tokens, secrets, and credentials must not be exposed or changed.
- iOS prefers `NEXT_PUBLIC_IOS_APP_STORE_URL`; otherwise it uses the trusted `/api/download/ios` resolver.
- The resolver accepts only published GitHub assets under `jerryboganda/oetwebapp/releases/download/`.
- Local validation is targeted; no full test/build marathon is required.

---

### Task 1: Add responsive video fullscreen and stretch controls

**Files:**
- Modify: `components/videos/video-player.tsx`
- Modify: `components/videos/secure-embed-player.tsx`
- Modify: `app/globals.css`
- Create: `components/videos/video-player.test.tsx`

**Interfaces:**
- `SecureEmbedPlayer` consumes a `fit: 'contain' | 'cover'` presentation prop and applies it to the provider iframe.
- `VideoPlayer` owns `isStretched`, exposes accessible `Stretch video to fill player` / `Fit video to player` controls, and keeps its existing `VideoPlayerHandle` unchanged.

- [x] **Step 1: Add fullscreen compatibility helpers and presentation state**

  Add typed helpers that read standard/WebKit/Mozilla fullscreen elements, request fullscreen on the container with vendor fallbacks, and exit fullscreen with vendor fallbacks. Track `isStretched` alongside `isFullscreen`, and subscribe/unsubscribe to standard and vendor fullscreen-change events.

- [x] **Step 2: Apply mobile viewport fullscreen sizing**

  Add the scoped player class and a fullscreen selector in the component class/CSS path so the fullscreen container uses `100vw` and `100dvh`, while its media child and watermark still use the full inset area.

- [x] **Step 3: Wire fit mode into both media paths**

  Pass `fit={isStretched ? 'cover' : 'contain'}` to `SecureEmbedPlayer`; apply the matching `object-contain`/`object-cover` class to the legacy `<video>`. Add `allowFullScreen` and `fullscreen` to the secure iframe presentation policy.

- [x] **Step 4: Add bottom-right controls**

  Render the stretch toggle as an accessible, pressed button in the secure overlay and the legacy bottom-right control cluster. Keep fullscreen controls adjacent and preserve the watermark z-order.

- [x] **Step 5: Test the interaction contract**

  Mock playback/session dependencies and fullscreen methods. Assert the secure player renders the stretch control, toggles its label/pressed state, requests/exits the container fullscreen, and that the legacy path receives the corresponding fit class.

- [x] **Step 6: Run the focused player test**

  Run:

  ```powershell
  pnpm exec vitest run components/videos/video-player.test.tsx --reporter=dot
  ```

  Expected: the new player tests pass.

### Task 2: Make all download badges visually consistent

**Files:**
- Modify: `components/marketing/app-download-promo.tsx`
- Modify: `app/get-app/page.tsx`
- Create: `app/get-app/page.test.tsx`
- Modify: `components/marketing/store-badges.tsx`
- Modify: `components/marketing/app-download-promo.test.tsx`
- Modify: `components/auth/__tests__/auth-screen-shell.test.tsx`

**Interfaces:**
- Existing badge component props remain backward compatible.
- `AppStoreBadge` accepts a direct-download presentation flag/copy without changing its link semantics.

- [x] **Step 1: Add shared badge geometry classes**

  Use the existing Google Play shell as the reference and give every `/get-app` badge the same height, full/max width, centered content, and gap. Apply one compact shared width treatment to the banner badges.

- [x] **Step 2: Update iOS badge copy for direct mode**

  Add a prop that changes the App Store eyebrow/label and accessible name when the destination is a temporary direct IPA resolver, while retaining the App Store wording when a store URL is configured.

- [x] **Step 3: Extend focused download tests**

  Assert the banner still exposes all actionable platform links, the iOS link is `/api/download/ios` when no App Store env is present, and all public `/get-app` platform cards render one download badge each.

- [x] **Step 4: Run focused download tests**

  Run:

  ```powershell
  pnpm exec vitest run components/marketing/app-download-promo.test.tsx --reporter=dot
  ```

  Expected: the download-promo tests pass.

### Task 3: Resolve temporary direct iOS release downloads safely

**Files:**
- Modify: `app/api/download/[platform]/route.ts`
- Modify: `lib/app-downloads.ts`
- Modify: `app/api/releases/native/route.test.ts`
- Create: `app/api/download/[platform]/route.test.ts`

**Interfaces:**
- `GET /api/download/ios` redirects to the newest trusted published `.ipa` asset when present.
- `IOS_DOWNLOAD_URL` remains a string and follows App Store URL → direct resolver precedence.

- [x] **Step 1: Add an iOS asset matcher**

  Extend the route's platform union and matcher map with `ios`, accepting only `.ipa` assets. Keep existing desktop/Android behavior and the trusted GitHub URL boundary unchanged.

- [x] **Step 2: Change the iOS client fallback**

  Add `IOS_DIRECT_DOWNLOAD_URL = '/api/download/ios'` and set `IOS_DOWNLOAD_URL = IOS_STORE_URL || IOS_DIRECT_DOWNLOAD_URL`. Preserve the App Store environment override.

- [x] **Step 3: Add resolver regression tests**

  Cover newest trusted IPA redirect, untrusted IPA rejection/fallback, and no-IPA fallback without weakening existing Android tests.

- [x] **Step 4: Run the route/download tests**

  Run:

  ```powershell
  pnpm exec vitest run app/api/releases/native/route.test.ts app/api/download/[platform]/route.test.ts --reporter=dot
  ```

  Expected: all focused route tests pass.

### Task 4: Review, validate, commit, and push

**Files:**
- Review only the explicit implementation, test, continuity, and design/plan paths above.
- Preserve unrelated `.codex/config.toml` and `.superpowers/` files.

- [x] **Step 1: Run the combined focused validation**

  Run:

  ```powershell
  pnpm exec vitest run components/videos/video-player.test.tsx components/marketing/app-download-promo.test.tsx app/get-app/page.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx app/api/releases/native/route.test.ts app/api/download/[platform]/route.test.ts --reporter=dot
  git diff --check
  pnpm exec tsc --noEmit
  ```

- [x] **Step 2: Inspect the scoped diff**

  Confirm the PDF requirements map to the changed controls, no secrets or `.env*` files are staged, and the release route still rejects untrusted URLs.

- [x] **Step 3: Update continuity state**

  Record changed files, focused validation output, the current public no-IPA boundary, and the next production/owner verification step in `.github/agent-state.local.md`.

- [ ] **Step 4: Stage explicit paths and commit**

  ```powershell
  git add app/globals.css app/api/download/[platform]/route.ts app/api/download/[platform]/route.test.ts app/api/releases/native/route.test.ts app/get-app/page.tsx app/get-app/page.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx components/marketing/app-download-promo.tsx components/marketing/app-download-promo.test.tsx components/marketing/store-badges.tsx components/videos/secure-embed-player.tsx components/videos/video-player.tsx components/videos/video-player.test.tsx lib/app-downloads.ts docs/superpowers/specs/2026-08-08-oet-prep-quick-fixes-design.md docs/superpowers/plans/2026-08-08-oet-prep-quick-fixes.md .github/agent-state.local.md
  git commit -m "fix: complete OET prep quick fixes"
  ```

- [ ] **Step 5: Push main and verify parity**

  ```powershell
  git push origin main
  git rev-parse HEAD
  git ls-remote origin refs/heads/main
  git diff --quiet
  ```

  Expected: the pushed SHA matches `origin/main` and the worktree is clean except for the pre-existing unrelated untracked files, which remain unstaged.
