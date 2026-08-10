# Four-platform app-download icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all app-download controls with one shared four-platform badge system matching the supplied Windows, Mac, Google Play, and App Store reference.

**Architecture:** `components/marketing/store-badges.tsx` owns the four inline SVG glyphs, shared black badge shell, and a four-platform grid. `components/marketing/app-download-promo.tsx` and `app/get-app/page.tsx` consume those primitives for banner, modal, card, and public download surfaces. Native Tauri/Capacitor launcher assets remain out of scope because they are not download controls.

**Tech Stack:** Next.js App Router, React 19, TypeScript, Tailwind CSS v4, lucide only for unrelated controls, Vitest/Testing Library, Playwright smoke assertions.

## Global Constraints

- Use the Windows four-pane mark, Apple mark for Mac, colored Google Play triangle, and App Store mark respectively.
- App-download surfaces must not use generic `Monitor`, `Laptop`, `Smartphone`, or plain `Apple` icons.
- Use one shared black button shell with a tall fixed height, rounded corners, white text, centered icon/text group, and consistent icon scale.
- Always expose all four buttons responsively; use a two-column grid where space allows and one column on narrow screens.
- Preserve `/get-app` direct platform destinations and existing grouped promo destinations when splitting grouped buttons.
- Do not modify native launcher icons, splash assets, `.env*`, credentials, or unrelated dirty files.

---

### Task 1: Build the shared platform glyph and badge primitives

**Files:**
- Modify: `components/marketing/store-badges.tsx`
- Test: `components/marketing/app-download-promo.test.tsx`

**Interfaces:**
- Produces `PlatformKey = 'windows' | 'mac' | 'android' | 'ios'`.
- Produces `PlatformGlyph({ platform, className? })` for reuse by `/get-app` card headers.
- Produces `PlatformDownloadBadge({ platform, href, compact?, className? })` with platform-specific accessible labels.
- Produces `AppDownloadGrid({ links, compact?, className? })` rendering exactly one badge for each `PlatformKey` in Windows, Mac, Google Play, App Store order.

- [ ] **Step 1: Extend the focused test expectations**

Update the banner test to require four links with labels for Windows, Mac, Google Play, and App Store, each carrying the shared compact width class. Add a card render assertion that the shared grid also exposes exactly four platform links.

- [ ] **Step 2: Run the focused test to verify the new contract fails**

Run:

```powershell
pnpm exec vitest run components/marketing/app-download-promo.test.tsx --reporter=dot
```

Expected: FAIL because the current banner renders one grouped desktop badge and no Mac-specific badge.

- [ ] **Step 3: Replace generic/grouped badge implementations with shared primitives**

Define a `PlatformKey` union, platform accessible labels, and four inline SVG glyphs. Use a shared shell such as:

```tsx
const shell = cn(
  'inline-flex items-center justify-center rounded-2xl border border-black/40 bg-black text-white shadow-sm transition-colors',
  compact ? 'h-16 gap-3 px-5' : 'h-20 gap-4 px-7',
  'hover:bg-black/85 hover:border-black/60',
  className,
);
```

`PlatformDownloadBadge` must render the platform glyph plus only the reference label (`Windows`, `Mac`, `Google Play`, or `App Store`) while retaining descriptive `aria-label` values. `AppDownloadGrid` must accept four hrefs and render all four badges with equal responsive widths.

- [ ] **Step 4: Run the focused test to verify the primitive contract passes**

Run the same Vitest command and expect PASS for the updated banner/card assertions.

- [ ] **Step 5: Review only the shared component diff**

Run:

```powershell
git diff --check -- components/marketing/store-badges.tsx components/marketing/app-download-promo.test.tsx
```

Expected: no whitespace errors and no changes outside the two listed files at this checkpoint.

### Task 2: Apply the shared four-button system to every app-download surface

**Files:**
- Modify: `components/marketing/app-download-promo.tsx`
- Modify: `app/get-app/page.tsx`
- Modify: `components/auth/__tests__/auth-screen-shell.test.tsx`
- Modify: `app/get-app/page.test.tsx`
- Modify: `tests/e2e/shared/mobile-smoke.spec.ts`

**Interfaces:**
- Consumes `PlatformDownloadBadge`, `PlatformGlyph`, and `AppDownloadGrid` from Task 1.
- Uses existing `GET_APP_PATH`, `WINDOWS_DOWNLOAD_URL`, `MAC_DOWNLOAD_URL`, `ANDROID_INSTALL_URL`, and `IOS_DOWNLOAD_URL` values without changing their definitions.

- [ ] **Step 1: Update the promo variants to render four platform badges**

Use `AppDownloadGrid` in the banner with the existing banner hrefs. Replace the modal's two generic `Monitor`/`Smartphone` tiles with a four-item grid of `PlatformDownloadBadge` instances, preserving its existing `/get-app` destination for grouped promo actions. Replace the card's two generic links with the same four-item grid and existing `/get-app` destination. Remove app-download-specific generic icon imports and JSX.

- [ ] **Step 2: Update `/get-app` card headers without introducing duplicate generic icons**

Replace the current `Laptop`, `Apple`, and `Smartphone` header icons with `PlatformGlyph` for the corresponding platform. Keep the four card descriptions and direct platform badge hrefs. Use `PlatformDownloadBadge` for each card's action so every visible app-download icon is from the shared four-platform set.

- [ ] **Step 3: Update focused and smoke test labels**

Change auth-shell, `/get-app`, and mobile-smoke assertions from grouped names (`Windows & Mac`, `Google Play & App Store`) to the four individual accessible labels. Assert all four links exist in each shared surface and retain the existing `/get-app`, Android-install, and iOS resolver destinations.

- [ ] **Step 4: Run all affected focused tests**

Run:

```powershell
pnpm exec vitest run components/marketing/app-download-promo.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx app/get-app/page.test.tsx --reporter=dot
```

Expected: PASS with four-platform assertions in all affected component/page tests.

### Task 3: Audit, validate, and deliver

**Files:**
- Modify: `.github/agent-state.local.md`
- Review: `components/marketing/store-badges.tsx`
- Review: `components/marketing/app-download-promo.tsx`
- Review: `app/get-app/page.tsx`

**Interfaces:**
- Consumes the completed four-platform surfaces and tests from Tasks 1-2.
- Produces a clean focused validation record and a pushed `main` commit.

- [ ] **Step 1: Audit app-download source for replaced generic icons and grouped labels**

Run:

```powershell
rg -n -i 'MonitorDown|<Monitor|<Laptop|<Smartphone|<Apple|Windows & Mac|Google Play & App Store|DesktopAppBadge|GooglePlayBadge|AppStoreBadge' components/marketing app/get-app tests/e2e/shared/mobile-smoke.spec.ts
```

Expected: no app-download surface uses the removed generic/grouped icon components; only shared platform primitives and unrelated non-download icons may remain outside the audited surfaces.

- [ ] **Step 2: Run the required lightweight validation**

Run:

```powershell
pnpm exec vitest run components/marketing/app-download-promo.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx app/get-app/page.test.tsx --reporter=dot
git diff --check
```

Expected: focused tests pass and `git diff --check` is clean. Full builds and suites are intentionally not run under the repository's lightweight-check directive.

- [ ] **Step 3: Update the handoff state with exact evidence**

Record the changed files, four-platform coverage, focused test command/result, `git diff --check` result, and the next delivery step in `.github/agent-state.local.md` without removing existing history.

- [ ] **Step 4: Stage only requested implementation and state paths**

Run:

```powershell
git add -- components/marketing/store-badges.tsx components/marketing/app-download-promo.tsx components/marketing/app-download-promo.test.tsx components/auth/__tests__/auth-screen-shell.test.tsx app/get-app/page.tsx app/get-app/page.test.tsx tests/e2e/shared/mobile-smoke.spec.ts .github/agent-state.local.md
```

Expected: `.codex/config.toml` and `.superpowers/` remain untracked and unstaged.

- [ ] **Step 5: Commit and push `main`**

Run:

```powershell
git commit -m "feat: unify four-platform app download icons"
git push origin main
```

Expected: the implementation commit and prior approved spec commit are present on `origin/main`; report the commit and focused validation results.
