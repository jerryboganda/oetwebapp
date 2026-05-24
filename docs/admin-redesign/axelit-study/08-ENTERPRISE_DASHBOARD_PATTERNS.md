# ENTERPRISE_DASHBOARD_PATTERNS.md — Enterprise UX principles distilled

What Axelit gets right (and wrong) by the standards of enterprise admin dashboard design. Use this when deciding which patterns to inherit, which to discard, and which to invent.

---

## 1 · The "Operations Center" anchor pattern ✓

**What Axelit does**: The Project Dashboard opens with a KPI strip — 4 numbers above the fold, no scrolling required. This is the **glance layer** that lets a returning admin see "is anything on fire?" in 2 seconds.

**Pattern**: KPI strip → trend visualization → status table → activity feed. Maps to the **glance → drill → act** progression.

**OET application**: Keep this pattern for the OET admin landing (`app/admin/page.tsx`). The current OET implementation does this well (`PulseTile` grid + `Panel` cards) — verify alignment.

## 2 · Dense-but-breathable card lattice ✓

**What Axelit does**: Cards have generous internal padding (~20px) and ambient halo shadows. They don't crowd each other (16-24px gaps) but they DON'T have luxurious whitespace either.

**Compare**: Bloomberg Terminal = dense; Notion = airy; Axelit = medium. Right calibration for admin staff who scan many pages per day.

**OET application**: Match Axelit's density. Don't go denser (admin isn't Bloomberg); don't go airier (admin isn't a marketing site).

## 3 · Status colour-coding consistency ✓

**What Axelit does**: Green = success/done. Red/magenta = danger/blocked. Blue/violet = info/active/primary. Yellow/chartreuse = warning/pending.

**Mostly consistent across pages.** The Project Status table uses the same chip colors as the KPI strip and the badge variants.

**OET application**: Define a 6-state palette (`success | warning | danger | info | neutral | primary`) and apply rigorously. Map OET's domain states onto these:
- "Published" → success
- "Draft" → neutral / info
- "Pending review" → warning
- "Failed" → danger
- "Active experiment" → primary
- "Archived" → neutral muted

## 4 · Single-family typography ✓

**What Axelit does**: Montserrat everywhere — display, body, labels, buttons. No editorial display face + body grotesque + mono label pairing.

**Trade-off**: Visually simpler (one font ships, faster page load) at the cost of typographic richness. Acceptable for admin where information density beats aesthetic distinction.

**OET application**: Consider keeping Montserrat OR using Geist (OET's existing font). Don't introduce a display serif for admin — wrong genre.

## 5 · One radius for surfaces ✓ (with caveat)

**What Axelit does**: 28.8px on every card.

**Caveat**: This is *too pillowy* for serious enterprise admin. It works for consumer-friendly dashboards (Linear, Stripe) but feels less serious than competitors (DataDog, Grafana, Looker).

**OET recommendation**: Use a smaller radius (12–16px) for OET's clinical/educational tone. Pillow corners signal "consumer", sharp corners signal "professional".

## 6 · The 8-role color system ✓

**What Axelit does**: 8 named role colors (primary/secondary/success/warning/danger/info/light/dark) each with a paired `-dark` variant for tinted-bg patterns. Total: 16 chromatic tokens + surfaces + text.

**Why this works**: 16 is enough to express every UI state. Adding a 9th role tempts inconsistency.

**OET application**: Adopt the 8-role system. Critically: pick OET-appropriate HUES, not Axelit's playful ones.

## 7 · Anti-pattern: Three icon libraries ✗

**What Axelit does**: Ships Tabler Icons (webfont) + Phosphor Icons (all 6 weights) + lucide-react. Total: ~3MB of icons loaded.

**Why it's bad**: Visual inconsistency (each library has its own line weight / corner radius); performance hit; nothing demands three.

**OET fix**: Standardize on **lucide-react** (already in OET). Delete Tabler + Phosphor.

## 8 · Anti-pattern: Six chromatic accents ✗

**What Axelit does**: Violet + magenta + chartreuse + blue + green + beige all appear in the same view. Two soft tints of each = 12 visible hues per dashboard.

**Why it's bad**: Hue overload destroys hierarchy. The eye can't tell what's important.

**OET fix**: Anchor on ONE primary (OET navy/clinical-blue) + ONE accent (e.g. warm coral or amber for callouts) + status colors (success/warning/danger/info) used ONLY for status. Maximum 6 visible hues per view.

## 9 · Anti-pattern: Welcome modal on every load ✗

**What Axelit does**: Welcome dialog opens on every page refresh.

**Why it's bad**: Annoying. Trains users to dismiss-without-reading.

**OET fix**: Show onboarding modal ONCE keyed to user state. Never on a page refresh. Provide a "Help" menu item that re-opens it on demand.

## 10 · Anti-pattern: `transition: all 0.3s ease` ✗

**What Axelit does**: `--app-transition: all 0.3s ease` declared globally and applied broadly.

**Why it's bad**: Animating `all` includes layout properties (width, height, padding, margin) which trigger reflows. `ease` is the browser's worst default easing (perfectly symmetric, looks robotic).

**OET fix**: Narrow to `transform, opacity, background-color` with named easings:
```css
--transition-fast:   transform 150ms cubic-bezier(0.25, 0.46, 0.45, 0.94);
--transition-base:   transform 200ms cubic-bezier(0.25, 0.46, 0.45, 0.94),
                     opacity 150ms cubic-bezier(0.25, 0.46, 0.45, 0.94);
--transition-slow:   opacity 300ms cubic-bezier(0.25, 0.46, 0.45, 0.94);
```

## 11 · Anti-pattern: DataTables.net jQuery legacy ✗

**What Axelit does**: Tables backed by jQuery DataTables (visible from `--dt-*` tokens).

**Why it's bad**: jQuery ships ~85KB; DataTables ~120KB; styles often clash with Bootstrap 5; React reconciliation conflicts with jQuery DOM mutations.

**OET fix**: Use **TanStack Table** (headless, React-native, ~30KB). Pair with `@tanstack/react-virtual` for large datasets.

## 12 · Pattern: Tabbed settings ✓

**What Axelit does**: Settings page uses tabs to chunk a long form into Profile / Notifications / Privacy / Security / Billing sections.

**Why it works**: Each tab is a focused task. Users don't scroll a 2000px form.

**OET application**: Mirror this for OET's `/admin/settings` (the Runtime Settings page). Tabs by category: General / Brevo / Stripe / Sentry / Backups / Auth / Push.

## 13 · Pattern: Inline row actions ✓

**What Axelit does**: Rightmost cell of every table row contains 2-3 small icon buttons (Edit ✎, Delete 🗑, View 👁).

**Trade-off**: Saves a click vs. row-click → menu, but consumes column space. Better when actions are <= 3.

**OET application**: For ≤ 3 actions, inline icon buttons. For 4+, use a kebab `⋯` menu.

## 14 · Pattern: Skeleton loading states ✓

**What Axelit does**: Heavy widgets (FilePond, Charts) show "Loading FilePond..." / "Loading Chart..." text placeholders in their slots.

**Why it works**: Layout doesn't shift when the widget mounts (no CLS), and users see the slot location immediately.

**OET application**: Use shadcn `<Skeleton>` or framer-motion entry animations for any widget that takes > 200ms to render. Reserve slot dimensions upfront.

## 15 · Pattern: Avatar overflow chips ✓

**What Axelit does**: `[👤 👤 👤 +10]` — three avatars overlapping, then a grey "+10" pill for the rest.

**Why it works**: Conveys count + identity without listing 13 names.

**OET application**: Use for any list of people/items > 3 (e.g. team members per content paper, reviewers on a marketplace item).

## 16 · Pattern: Two-tier breadcrumb / page title ✓ (inferred)

**What Axelit does**: (inferred from settings page) Page has a top-level title (e.g. "Settings") + a sub-context (e.g. "Profile"). Tabs are the second tier.

**OET application**: For OET's deep admin tree (`/admin/content/reading/[paperId]/questions`), use breadcrumb in header + tabs for sibling pages.

## 17 · Pattern: Floating utility chrome (customizer) ✗ (for OET)

**What Axelit does**: Fixed bottom-right circular button opens a global settings drawer.

**Why it's wrong for OET**: Production admin doesn't need a runtime theme customizer. The button is template-marketing chrome.

**OET fix**: Remove. If a global floating button is needed, use it for *Help* / *Feedback* / *Support* — not theming.

## 18 · Density philosophy ramps up vs down by role

A future OET pattern Axelit doesn't implement: **different density per admin role**.

- **Content authors** (writing/reading editors): generous density — they're focused on one item at a time, long-form text.
- **Operations admins** (review ops, billing, escalations): medium density — they scan many items per session.
- **System admins** (settings, audit logs): high density — they're power users dealing with raw data.

Implement via a `density` design token (`compact | comfortable | spacious`) that scales padding + line-height per role.

## 19 · Empty-state philosophy ✗ (missing in Axelit)

Axelit's captured pages all have demo data — never an empty state. **Real admin spends time in empty states** (no flags created, no users awaiting review, no failed jobs).

**OET fix**: Define a standard empty-state component:
```
┌────────────────────────────┐
│         🌱                 │  ← contextual illustration / icon
│   No items yet             │  ← H5 muted
│   Description of what      │  ← p-small muted
│   would appear here.       │
│   [Create one →]           │  ← primary CTA
└────────────────────────────┘
```

## 20 · Bulk action philosophy

Axelit's orders-list table shows checkboxes per row but no captured "bulk actions" toolbar transition.

**OET fix**: Standard pattern:
- Idle: table shows row hover, no bulk bar
- 1+ rows selected: header transforms to bulk-action toolbar (`3 selected · Archive · Export · Delete`)
- Selection persists across pagination (with a "select all matching X" link)
- Esc clears selection
