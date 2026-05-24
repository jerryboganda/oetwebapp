# COMPONENT_ARCHITECTURE.md — Forensic component inventory

**Source**: Axelit pages crawled (full list in [`00-ROUTE-INVENTORY.md`](00-ROUTE-INVENTORY.md))
**Method**: Live DOM probe via Playwright `browser_evaluate` — distinct visual variants by `bg|color|radius|fontWeight|padding` signature.

For every component family below: **what it looks like, how it's classed in Bootstrap, what props/variants exist, and what the OET equivalent should be**.

---

## 1 · Buttons (40 distinct variants captured)

### 1.1 · Solid (8 variants)
```css
.btn .btn-primary     /* bg: rgb(140,118,240) violet,   color: white */
.btn .btn-secondary   /* bg: rgb(100,100,100) grey,     color: white */
.btn .btn-success     /* bg: rgb(20,120,52) green,      color: white */
.btn .btn-warning     /* bg: rgb(215,220,65) chartreuse, color: white */
.btn .btn-danger      /* bg: rgb(240,10,200) magenta,   color: white */
.btn .btn-info        /* bg: rgb(46,94,231) royal blue, color: white */
.btn .btn-light       /* bg: rgb(215,208,200) beige,    color: white */
.btn .btn-dark        /* bg: rgb(40,38,50) near-black,  color: white */
```
**All share**: `border-radius: 5px`, `font-size: 15px`, `font-weight: 600`, `padding: 7px 25px`, 1px solid border same color as bg.

### 1.2 · Outline (8 variants)
```css
.btn .btn-outline-primary  /* bg: transparent, color: violet, border: 1px violet */
.btn .btn-outline-secondary
.btn .btn-outline-success
.btn .btn-outline-warning
.btn .btn-outline-danger
.btn .btn-outline-info
.btn .btn-outline-light
.btn .btn-outline-dark
```

### 1.3 · Light / soft (8 variants) — the "tinted background" pattern
```css
.btn .btn-light-primary    /* bg: rgba(140,118,240, 0.3),  color: rgb(36,17,135) ← primary-dark */
.btn .btn-light-secondary  /* bg: rgba(100,100,100, 0.3),  color: rgb(106,90,100) */
.btn .btn-light-success    /* bg: rgba(20,120,52, 0.3),    color: rgb(52,50,46) */
/* ... etc — same pattern for warning/danger/info/light/dark */
```
**Pattern**: background is the role colour at **0.3 alpha**, text is the role's `-dark` variant. No visible border.

### 1.4 · Link
```css
.btn .btn-link  /* bg: transparent, color: rgb(140,118,240) violet */
```

### 1.5 · Sizes
- `.btn-lg`: `font-size: 18px`, `padding: 6px 30px`
- `.btn` (default): `font-size: 15px`, `padding: 7px 25px`
- `.btn-sm`: `font-size: 14px`, `padding: 4px 20px`

### 1.6 · Icon-only ("icon-btn b-r-18")
```css
.icon-btn  /* square, ~28-36px, border-radius: 18px */
```

### 1.7 · Button groups
The first/middle/last children get asymmetric border-radius:
- First: `5px 0 0 5px`
- Middle: `0`
- Last: `0 5px 5px 0`

### Sections on the `/ui-kit/buttons` page (the full taxonomy)
`Basic · Outline · Light · Group · Sizes · Icon · Radius · Social · Disable · Active · Loading · Block · Sizes (again) · Radius (again) · Nesting · Checkbox Radio · Vertical`

**OET application** — collapse Axelit's 40-variant matrix to a **6-variant Button component**:
```tsx
type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'destructive' | 'link';
type ButtonSize = 'sm' | 'md' | 'lg';
type ButtonState = 'default' | 'hover' | 'focus-visible' | 'active' | 'disabled' | 'loading';
```
- Drop the 8-tinted-light pattern (use `secondary` or `ghost` instead).
- Drop the colour-per-status buttons (use a status `Badge` component for state communication; buttons should drive action, not state).

## 2 · Cards (37 instances on `/dashboard/ecommerce` alone)

### Observed card variants (from class signature):
| Class | Observed dimensions | Use |
| ----- | ------------------- | --- |
| `.orders-provided-card .card` | 247 × 115 | Narrow KPI tile |
| `.bg-primary-300 .product-sold-card .card` | 247 × 207 | Tinted KPI with chart sparkline |
| `.bg-danger-300 .product-sold-card .card` | 247 × 210 | Tinted KPI (danger tone) |
| `.product-store-card .card` | 247 × 115 | Narrow stat tile (negative value) |
| `.card` (plain) | 654 × 339 | Wide chart container |
| `.order-detail-card .card` | 383 × 331 | Narrow detail card |
| `.card` (plain wide) | 519 × 267 | Product-row card |

### Card anatomy:
```html
<div class="card">                       <!-- radius: 28.8px, shadow: ambient halo -->
  <div class="card-header">              <!-- optional, padded -->
    <h5>Title</h5>                       <!-- 18px, 600/700 weight -->
    <button>…</button>                   <!-- optional menu/action -->
  </div>
  <div class="card-body">                <!-- main content, padding ~16-24px -->
    <!-- content -->
  </div>
  <div class="card-footer">              <!-- optional, light tint -->
    <!-- pagination, links -->
  </div>
</div>
```

### Card surface variants (color-tinted):
- `.bg-primary-300` — soft violet tint (background)
- `.bg-danger-300` — soft magenta tint
- `.bg-success-300` — soft green tint
- (pattern: `.bg-{role}-300` for the 8 role colors)

**OET application**: Single `Card` component, `surface` prop:
```tsx
type CardSurface = 'default' | 'tinted-primary' | 'tinted-success' | 'tinted-warning' | 'tinted-danger';
```
Use tinted variants ONLY for status panels (alerts, callouts). Default to plain white for data display.

## 3 · KPI Tiles (universal stat-card pattern)

```
┌──────────────────────┐
│ ┌─┐                  │  ← optional icon top-left (24-32px, in tinted square)
│ └─┘                  │
│                      │
│  2,450               │  ← H4 or H2, 700 weight, large
│  Product Sold        │  ← H6 or label, muted text
│                      │
│  ▲ +12.5%  vs last   │  ← optional trend indicator + comparison
└──────────────────────┘
```

**Variants observed**:
- Pure number (no chart)
- Number + sparkline (Apex chart inside card-body)
- Tinted background (`.bg-primary-300`) + number + label
- Number + trend arrow

## 4 · Status Badges (chips)

```html
<span class="badge bg-primary">In Progress</span>
<span class="badge bg-success">Completed</span>
<span class="badge bg-warning">Pending</span>
<span class="badge bg-secondary">Not Started</span>
<span class="badge bg-info">Scheduled</span>
```

Or the light variant (used in Project Status table):
```html
<span class="badge bg-light-success">Completed</span>
```

**Observed**:
- "In Progress" — info/warning tone
- "Completed" — success tone
- "Not Started" — secondary (grey)
- "Scheduled" — info tone

**OET application**: Single `Badge` component, 6-status enum, two intensities (solid + tinted).

## 5 · Tables (DataTables.net legacy)

### Observed structure (Project Status table on `/dashboard/project`):
```html
<table class="datatable">
  <thead>
    <tr>
      <th>Project</th>     <!-- sortable, click toggles dt-column-ordering -->
      <th>Status</th>
      <th>TeamLead</th>
      <th>Priority</th>
      <th>Remarks</th>
    </tr>
  </thead>
  <tbody>
    <tr>                   <!-- hover: bg rgba(0,0,0, 0.035) -->
      <td><h6>Web Redesign</h6></td>           <!-- H6 — text emphasis -->
      <td><span class="badge">In Progress</span></td>  <!-- chip -->
      <td><a><img class="avatar" /></a></td>    <!-- avatar link -->
      <td>High</td>                             <!-- plain text -->
      <td><i class="ti ti-check"></i> Design phase…</td>  <!-- icon + text -->
    </tr>
    <!-- … -->
  </tbody>
</table>

<div class="datatable-footer">
  <p>Showing 7 to 20 of 20 entries</p>
  <ul class="pagination">
    <li><a>Previous</a></li>
    <li><a>1</a></li>
    <li><a>2</a></li>
    <li><a>3</a></li>
    <li><a>Next</a></li>
  </ul>
</div>
```

**Observed row signatures**:
- Selected row alpha: `--dt-row-selected-stripe-alpha: 0.923`
- Hover alpha: `--dt-row-hover-alpha: 0.035` (very subtle)
- Selected text color: white on `rgb(13, 110, 253)` (Bootstrap blue — NOT Axelit's primary; appears DataTables wasn't fully reskinned)

**OET application**: Replace DataTables with **TanStack Table** (already common in React stacks). Replicate:
- Sortable headers (click to sort, chevron indicator)
- Row hover (3.5% alpha overlay)
- Row selected (full primary at ~90% alpha)
- Cell content can be ANY component (text, badge, avatar, icon+text)
- Pagination: `Previous / N / Next` + "Showing X to Y of Z" caption

## 6 · Sidebar Nav Item

```html
<li class="sidebar-item">                          <!-- normal item -->
  <a href="/dashboard/project" class="sidebar-link">
    <img src="..." class="sidebar-icon" />        <!-- 20-24px icon -->
    <span>Project</span>
  </a>
</li>

<li class="sidebar-item has-children">             <!-- collapsible parent -->
  <a href="#dashboard" class="sidebar-link" aria-expanded="true">
    <img class="sidebar-icon" />
    <span>Dashboard</span>
    <span class="badge bg-primary">4</span>        <!-- optional count badge -->
    <i class="chevron"></i>                        <!-- rotates on expand -->
  </a>
  <ul class="submenu">
    <li><a href="/dashboard/project">Project</a></li>
    <li><a href="/dashboard/ecommerce">Ecommerce</a></li>
  </ul>
</li>
```

**Group separator**:
```html
<li class="sidebar-group-label">DASHBOARD</li>
```

**States**:
- Default: plain text, dark color
- Hover: light tint background (`btn-light-primary` reused)
- Active: violet text + light-violet bg (matches active route)
- Expanded: parent shows children indented +16px

## 7 · Avatar (stacked, single)

### Single
```html
<img src="..." alt="avatar" class="avatar" />
```
- Circular (`border-radius: 50%`)
- Sizes: 24, 32, 40, 64, 96 (approx — multiples of 8)

### Stacked overlapping (avatar-group)
```html
<ul class="avatar-group">
  <li><img class="avatar" /></li>
  <li><img class="avatar" /></li>  <!-- overlaps prev by ~10px -->
  <li><img class="avatar" /></li>
  <li class="avatar-overflow">10+</li>  <!-- pill text "10+", grey bg -->
</ul>
```

## 8 · Progress bar (with overlapping % badge)

```html
<div class="progress-wrapper">
  <div class="progress">
    <div class="progress-bar" style="width: 60%;"></div>
  </div>
  <span class="progress-badge">+ 60%</span>    <!-- absolute, overlaps the bar -->
</div>
```

Observed percentages on `/dashboard/project` Today Tasks: 60%, 80%, 68%, 35%.

## 9 · Header bar widgets (right side, from left to right)

1. **Weather** — `26°C` with superscript `°C`, link
2. **Language picker** — flag image button (`EN` with US/UK/FR/RU/IT options in dropdown)
3. **Search** — icon button (likely opens overlay)
4. **Fullscreen toggle** — icon button
5. **Notifications** — bell icon + count badge (`5`)
6. **Theme toggle** — icon (moon/sun?) — exists but did NOT toggle `data-bs-theme`
7. **Settings** — gear icon (opens customizer? sidebar settings?)
8. **Avatar** — circular, opens user menu dropdown

**OET application**: Drop weather + language picker (irrelevant to OET admin). Keep search, fullscreen, notifications (with count), theme toggle, settings, avatar.

## 10 · Modal / Dialog (Welcome example)

```html
<dialog class="modal show" role="dialog">
  <div class="modal-content">
    <button class="btn-close"></button>            <!-- ✕ top-right -->
    <h2>Welcome! <img class="gif" alt="wave" /></h2>
    <img class="hero-illustration" />
    <button class="btn btn-primary">Get Started</button>
  </div>
</dialog>
```

Observed: opens on every page load (anti-pattern). Sweet Alert and Sweet Alert variants for confirmations elsewhere.

## 11 · Dropdown menu

```html
<div class="dropdown">
  <button class="dropdown-toggle">EN</button>
  <ul class="dropdown-menu show">
    <li><button class="dropdown-item p-2 selected">US English (US)</button></li>
    <li><button class="dropdown-item p-2">FR Français</button></li>
    <!-- … -->
  </ul>
</div>
```

**Observed item styling**: `border-radius: 10px`, `padding: 8px`, `font-size: 16px`, `font-weight: 400` (lighter than buttons).

## 12 · Tag chips (project tile)

```html
<div class="tag-row">
  <span class="tag">📱 Mobile app</span>
  <span class="tag">Marketing</span>
</div>
```
- Pill-shaped (`border-radius: 50px`)
- Light grey or tinted background
- Small (12-13px text)
- Often with emoji prefix in Axelit (deliberate informal voice)

## 13 · Pagination

```html
<ul class="pagination">
  <li><a class="page-link">Previous</a></li>
  <li><a class="page-link active">1</a></li>     <!-- active: primary bg -->
  <li><a class="page-link">2</a></li>
  <li><a class="page-link">3</a></li>
  <li><a class="page-link">Next</a></li>
</ul>
```

Caption (left of pagination): `Showing 7 to 20 of 20 entries`.

## 14 · Customizer panel (off-canvas)

Right-side drawer with sections:
- **Sidebar option** — Vertical / Horizontal / Dark layouts
- **Layout option** — LTR / RTL / Box
- **Color Hint** — 6 colour swatches
- **Text size** — small / medium / large
- Footer: Reset · Buy Now (themeforest) · Support · Document

**OET application**: Remove. Admin users shouldn't be theming their own panels in production.

## 15 · Components NOT captured (gap)

These exist in Axelit's sidebar but were not visited in this study pass:
- **Forms** (`/ui-kit/forms-elements`) — input/select/textarea/checkbox/radio/switch/datepicker
- **Tabs** (`/ui-kit/tabs`) — pill tabs, underline tabs, vertical tabs
- **Accordions** (`/ui-kit/accordions`)
- **Sweet Alert** dialogs
- **Slider** (`/advance-ui/slider`)
- **Calendar** (`/apps/calendar`) — full calendar widget
- **Editor** (`/ui-kit/editor`) — rich text editor
- **File Manager** (`/apps/file-manager`)
- **Chat** (`/apps/chat`)

For full OET fidelity, run a follow-up Hallmark study pass on these routes.
