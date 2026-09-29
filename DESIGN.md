# Design System: OET Prep Platform
**Project ID:** jerryboganda/oetwebapp

This is the single design-system spec for every surface in the web app: learner, expert/tutor, sponsor and admin. They share one palette, one typeface, one motion vocabulary and one set of tokens. Only **density** differs; admin and expert are denser than learner. The learner dashboard is the canonical reference page.

## 0. Where things live (read this first)
| Concern | Location |
| --- | --- |
| Design tokens (colour, type, radius, elevation, motion, z-index) | `app/globals.css`, in the `@theme` / `@theme static` blocks |
| Dark-mode token values | `app/globals.css`, in `:root.dark` (class-based, via next-themes) |
| Admin alias layer (`--admin-*`, the same palette at admin density) | `app/admin/_design/admin-tokens.css` |
| Motion for motion/react (durations, easings, springs, surface presets) | `lib/motion.ts` (mirrors the CSS `--duration-*` / `--ease-*`) |
| Motion components | `components/ui/motion-primitives.tsx` (`MotionPage/Section/List/Item/Presence/Collapse/FadeSwitch`) |
| Shared primitives | `components/ui/*`: Button, Card, Badge, Tabs, Modal/Drawer, InlineAlert/Toast, form-controls, Skeleton, EmptyState/ErrorState, DataTable, StatCard, Stepper, Pagination, FilterBar, BulkActionBar |
| Dense admin primitives (Radix-based) | `components/admin/ui/*`, with admin page layouts in `components/admin/layout/*`. Admin routes only; ESLint-enforced (`eslint.config.mjs`), which also bans aliasing a kit primitive (e.g. `Button as LegacyButton`) |
| Global toast | `components/ui/toaster.tsx` (`toast()`, mounted once in `app/providers.tsx`). Inline page messages: `InlineAlert`/`Toast` in `components/ui/alert.tsx` |
| Learner page compositions | `components/domain/learner-surface.tsx`: LearnerPageHero, LearnerSurfaceSectionHeader, LearnerSurfaceCard |
| App shell | `components/layout/*`: AppShell, role shells, TopNav, ProfileMenu, Sidebar, BottomNav, GlobalSearch |
| Chart colours | `lib/domain/chart-palette.ts` |
| Accessibility preferences (large text, high contrast, reduce motion) | `contexts/accessibility-context.tsx`, with the CSS at the bottom of `app/globals.css` |

**Which kit:** admin pages use the dense `components/admin/ui` primitives, and `components/ui` for what the admin kit lacks: Modal/Drawer, DataTable with mobile cards, FilterBar, Pagination, InlineAlert, PageSkeleton, Tabs. Everything outside admin routes uses `components/ui`.

**Adding a new screen:**
1. Wrap it in the role shell.
2. Open it with `LearnerPageHero` (learner) or admin `PageHeader` (admin).
3. Build the body from `Card` / `LearnerSurfaceCard`.
4. Give every data view a loading, an empty and an error state.
5. Use only the token classes below; no hex values and no `text-[Npx]`.

## 1. Visual theme and atmosphere
- Warm clinical calm, not sterile.
- An airy cream canvas, not dense dark chrome.
- A premium academic workspace, not a marketing landing page.
- Supportive and trustworthy, with gentle motion and soft cards.
- Data-rich, but never cold or cluttered.

## 2. Colour
Use semantic classes. Never use raw hex values, and avoid raw `slate-*`/`gray-*` for text or surfaces.

| Class | Light | Dark | Role |
| --- | --- | --- | --- |
| `primary` | `#7c3aed` | `#a78bfa` | Primary actions, active nav, focus, accents |
| `primary-dark` | `#6d28d9` | `#8b5cf6` | Hover/pressed |
| `primary-50…950` | static violet ramp | same | Tints, chart fills, fixed fills (does not flip) |
| `lavender` | `#ede9fe` | `#1e1b4b` | Soft highlights, icon tiles, chips |
| `background-light` | `#f7f5ef` | `#07111d` | Page canvas |
| `surface` | `#fffefb` | `#0f172a` | Cards, panels, menus |
| `navy` | `#0f172a` | `#e5eef9` | Headings and primary text |
| `muted` | `#526072` | `#94a3b8` | Secondary text, metadata |
| `border` / `border-hover` | `#d8e0e8` / `#b9c6d1` | `#1f2937` / `#334155` | Borders, dividers |
| `success` / `warning` / `danger` / `info` | `#10b981` / `#d97706` / `#ef4444` / `#2563eb` | | Status only. Info blue is never the brand accent. |
| `gold`, `oet-navy`, `oet-teal` | | | OET corporate accents (billing, certificates) |

- **Admin** uses the same violet; there is no separate brand colour. Its `--admin-*` variables are aliases whose light and dark values match the table above.
- **White text on a primary fill** needs `dark:bg-violet-700` in dark mode for AA contrast. Button already does this.

## 3. Typography
- **Typefaces:** Manrope (`--font-sans`) for all UI, admin included. Fraunces (`font-display`) only for rare brand moments.
- **Scale:**
  - `text-3xs` (10px): dense badges, chart ticks
  - `text-2xs` (11px): eyebrows, captions, metadata
  - `text-xs` (12px) through `text-4xl`: Tailwind defaults

  The micro sizes are rem-based, so the "large text" accessibility setting scales them.
- **Eyebrows:** `text-2xs font-bold uppercase tracking-[0.16em] text-muted`.
- **Body:** 14–16px with a calm line height. Headings are semibold or bold with tight tracking.
- **Numbers:** use `tabular-nums` in tables and metrics, and right-align numeric columns.

## 4. Shape, elevation, spacing
- **Radius roles:**
  - `rounded-control` (10px): inputs, buttons, chips
  - `rounded-card` (16px): cards, panels. Existing cards use `rounded-2xl`, which is equivalent.
  - `rounded-surface` (24px): heroes, modals, sheets
- **Elevation:**
  - `shadow-xs` (hairline)
  - `shadow-sm` (resting card)
  - `shadow-clinical` (hover or raised)
  - `shadow-md` (popover/menu)
  - `shadow-lg` (dialog)

  Cards always pair a border with a shadow, never a shadow alone.
- **Spacing:** use Tailwind's 4px scale. Card padding comes from `Card padding="sm|md|lg"`, which is mobile-dense and desktop-comfortable. Avoid arbitrary `p-[13px]`.
- **Z-index:** overlay 40 < modal 50 < drawer 60 < popover 70 < toast 80.

## 5. Motion
- **Durations:**
  - `--duration-instant` 120ms
  - `fast` 160ms
  - `normal` 220ms
  - `slow` 280ms
  - `hero` 360ms
- **Easing:** `ease-standard` (decisive ease-out), `ease-enter`, `ease-exit`. motion/react uses the same values through `motionTokens` in `lib/motion.ts`.
- **Surface presets:** use `getSurfaceMotion('route'|'section'|'list'|'item'|'overlay'|'state')` or the Motion* components. Don't hand-write springs.
- **Animate only `transform` and `opacity`**, never layout properties.
- **Hover:** a lift of at most 1px, gated with the `hoverable:` variant so touch devices don't stick. Press feedback is a scale of 0.98 (`.pressable`).
- **Active indicators** (tabs, sidebar, bottom nav) use a shared-layout `layoutId`. Scope each instance's id with `useId`.
- **Reduced motion is mandatory.**
  - Three layers already handle it: the OS media query, the in-app `a11y-reduce-motion` class, and `MotionConfig` in the accessibility context.
  - Under reduced motion, keep every **state** visible (active fills, selected rings). Drop only the movement.
- **Never animate** exam timers, audio/recording controls or anything on the critical path of a live attempt.
- **Route changes:** enter-only `.page-enter` (an opacity fade) on `#main-content`. It is skipped on first paint, in the learner shell (learner pages animate themselves), with `distractionFree`, and on exam/live routes (`isExamOrLiveRoute`). Never put an exit animation (`AnimatePresence`) around routes: it keeps the old `<main>` mounted against the new route and renders the page twice.

## 6. Components
| Component | Styling | Behaviour |
| --- | --- | --- |
| Button | `components/ui/button`. Variants `primary` / `secondary` / `ghost` / `destructive` / `outline`; sizes 44–48px tall | Micro hover/tap, `loading` spinner, `asChild` for links, native haptics |
| Card | Border, `bg-surface`, `shadow-sm`; `hoverable` gives a clinical hover | Mobile-dense padding |
| Inputs | `form-controls`: soft surface, 1px border, primary focus ring | Label, hint and error wired with aria |
| Tabs | Segmented pill with a moving active pill | Arrow/Home/End keys |
| Modal / Drawer | Body portal, focus trap, refcounted scroll lock, focus restore | Escape and backdrop close |
| Overlays | admin Dialog/AlertDialog use `--z-modal`; Select/DropdownMenu use `--z-popover` | One `--z-*` scale in `app/globals.css` |
| Navigation | Sticky glass top nav, desktop sidebar, mobile bottom nav | `aria-current="page"`; bottom nav hides while the keyboard is open |
| Empty / Error | `EmptyState` / `ErrorState` | Always explain the situation and offer the next action or a retry |
| Data visuals | Charts on `bg-surface`, faint gridlines, one accent per series | Colours from `chart-palette` |

## 7. Layout and responsive behaviour
- Keep the workspace about 1200px wide.
- Pages flow as: hero → action cards → main grid → supporting rail.
- **Mobile (<lg):**
  - The sidebar becomes the top-nav drawer and the bottom nav appears.
  - Content is a single column.
  - Touch targets are ≥44px.
  - Tables scroll inside `overflow-x-auto` or become cards.
  - No horizontal page scroll at 360px.
- **Desktop:** sticky chrome with a scrolling workspace. Tool pages may split into dual panels.
- **Safe areas:** use `--safe-area-inset-*`, `.overlay-safe-area`, `.keyboard-safe-bottom`. Never hardcode device insets.
- **Admin and expert:** dense tables, sticky headers, keyboard-reachable row actions, and no airy consumer spacing.

## 8. Do's and don'ts
**Do:**
- Use the shared primitives and tokens.
- Keep violet as the one accent and navy as the text anchor.
- Show explicit empty, loading and error states.
- Keep the shell and spacing consistent across all roles.

**Don't:**
- Add hex colours, `text-[Npx]`, new shadow recipes or a second brand colour.
- Use default browser controls that ignore the system.
- Add loud gradients, glassmorphism on content cards, neon glows or decorative motion.
- Remove the ambient background blooms from learner pages.
- Change exam-player layouts or timing for visual reasons.

## 9. Agent prompt guide
Build this page in the OET Prep system. Use:
- a warm cream canvas, violet primary accent, navy headlines, soft bordered cards and sticky glass chrome
- tokens from `app/globals.css` and primitives from `components/ui`
- `LearnerPageHero` on learner pages, or admin `PageHeader` and admin primitives at admin density
- loading, empty and error states on every data view
- motion from `lib/motion.ts` presets, respecting reduced motion
