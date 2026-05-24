# VISUAL_HIERARCHY_RULES.md — How Axelit organizes attention

The deliberate (and accidental) rules that make Axelit's hierarchy work — or fail.

---

## 1 · The three-band hierarchy (universal)

Every Axelit dashboard splits the screen into three vertical attention bands:

```
┌────────────────────────────────────────────────────────┐
│  BAND A · "the glance" — KPI numbers, status badges    │  ← biggest type, biggest contrast
│                                                          │
├────────────────────────────────────────────────────────┤
│  BAND B · "the explore" — charts, tables, mid-density  │  ← H5 titles, body text
│                                                          │
├────────────────────────────────────────────────────────┤
│  BAND C · "the act" — activity feeds, lists, tasks     │  ← H6 titles, micro text
└────────────────────────────────────────────────────────┘
```

**Reading flow**: Top-left → right → drop down → right → drop down (F-pattern).

## 2 · Heading-to-body ratio per card

| Band | Heading | Body | Ratio |
| ---- | ------- | ---- | ----- |
| KPI tile | H5 label (18px) | H2 number (32-45px) | **Inverted** — number is bigger than label |
| Chart card | H5 title (18px) | SVG chart | ~1:18 by visual weight |
| Table card | H5 title (18px) | 13-14px cell text | 1:1.3 |
| Task tile | H6 task name (16px) | 12-13px metadata | 1:1.3 |

**Rule extracted**: KPI cards invert the normal heading-to-content ratio — the number IS the content; the label is the modifier. Everywhere else, heading > body in size.

## 3 · Color emphasis ladder

```
brightest → dimmest

  PURE WHITE (#fff) cards on #f6f6f6 body  ← cards POP via contrast, not borders/shadows
        ↓
  FONT-TITLE (#1c3264) navy headings        ← darkest text reserves itself for titles
        ↓
  FONT-COLOR (#15264b) body text            ← slightly lighter navy
        ↓
  FONT-SECONDARY (#22242c) less important   ← darker variant for muted info
        ↓
  FONT-LIGHT (#a0a0b0) very muted           ← captions, helper text, footer
        ↓
  Tinted bg (color@30%) light buttons       ← lowest emphasis interactive
```

**Rule extracted**: There are **5 levels of text emphasis**. Use exactly these — don't invent intermediate greys mid-component.

## 4 · Status emphasis hierarchy

```
LOUDEST → QUIETEST

  Solid badge   bg-{role}, white text          ← critical states (danger, success)
        ↓
  Light badge   bg-{role}@30%, dark text       ← important but not urgent
        ↓
  Outline badge bg-transparent, colored border ← informational
        ↓
  Text-only     no chip, plain text            ← lowest-emphasis (e.g. "High" priority)
```

**Rule extracted**: Match badge intensity to action urgency:
- Failures, errors, blocked → **solid**
- Pending, scheduled, waiting → **light**
- Active, ongoing, current → **outline**
- Numeric or text-only → **no chip**

## 5 · Whitespace allocation

| Surface | Internal padding | External gap | Notes |
| ------- | ---------------- | ------------ | ----- |
| Sidebar item | ~14px vertical, ~16px horizontal | 4px between siblings | Tight — packs 80+ items |
| Card body | ~20-24px | 16-24px between cards | Comfortable |
| Card header | ~16-20px vertical | (none — flush) | |
| KPI tile content | ~16px | (none — flush) | |
| Table cell | ~12-16px vertical, ~12-20px horizontal | (collapsed — table borders) | |
| Header bar | ~16px vertical, ~24px horizontal | | |
| Modal | ~24-32px | (none) | More generous than cards |

**Rule extracted**: Padding scales with the surface's role:
- Chrome (sidebar, header, table) → tight (12-16px)
- Content (card body, modal) → generous (20-32px)
- Display (KPI number) → minimal (16px around the number)

## 6 · Border discipline

**Axelit barely uses borders.** Hierarchy comes from:
1. Background contrast (#fff card on #f6f6f6 body)
2. Ambient shadow (5% halo)
3. Radius (rounded vs sharp signals different roles)

Borders are reserved for:
- Outline buttons (`btn-outline-*`)
- Form inputs (`--bs-border-color: #dee2e6`)
- Table cells (`--bs-border-color`)
- Dividers (`<hr>`, `border-top`)

**Rule extracted**: Surfaces don't need borders if they have contrasting backgrounds. Reserve borders for interactive elements (inputs) and structural dividers (hr).

## 7 · Shadow as hierarchy

Three shadow elevations:
1. **Ambient** (`--box-shadow`) — default state, "elevation 1"
2. **Hover** (`--hover-shadow`) — "elevation 2"
3. **Bottom** (`--bottom-shadow`) — sticky elements casting downward

**Rule extracted**: Axelit uses only 3 shadow levels. That's the right number — more than 5 creates noise.

## 8 · Type scale gap analysis

| Step | Token | Value | Px | Ratio from previous |
| ---- | ----- | ----- | -- | ------------------- |
| 1 | `--font-size` body | `14px` | 14 | — |
| 2 | `--btn-font-size` | `15px` | 15 | 1.07× |
| 3 | `--h6-font-size` | `1rem` | 16 | 1.07× |
| 4 | `--h5-font-size` | `1.125rem` | 18 | 1.13× |
| 5 | `--h4-font-size` | `1.25rem` | 20 | 1.11× |
| 6 | `--h3-font-size` | `1.75rem` | 28 | 1.4× |
| 7 | `--h2-font-size` | `2rem` | 32 | 1.14× |
| 8 | `--h1-font-size` | `2.5rem` | 40 | 1.25× |

**Diagnosis**: The scale isn't musical (varies between 1.07× and 1.4×). The gaps from H6→H5 and H4→H3 are particularly inconsistent. **A clean modular scale (e.g. 1.2× = "minor third", or 1.25× = "major third") would create more harmonious rhythm.**

**OET recommendation** — Major-third scale anchored at 14px body:
```css
--text-body:   14px;     /* 14 */
--text-sm:     12.5px;   /* 14 / 1.125 ≈ 12.5 */
--text-lg:     17.5px;   /* 14 * 1.25 */
--text-xl:     21.9px;   /* 17.5 * 1.25 */
--text-2xl:    27.3px;   /* 21.9 * 1.25 */
--text-3xl:    34.2px;   /* 27.3 * 1.25 */
--text-4xl:    42.7px;   /* 34.2 * 1.25 */
```

## 9 · Weight discipline

| Weight | Use observed |
| ------ | ------------ |
| 400 (regular) | Body text, dropdown items |
| 500 (medium) | Paragraph emphasis, badges |
| 600 (semibold) | Buttons, sub-headings, table cell text-emphasis |
| 700 (bold) | H2 KPI numbers, primary headings |

Four weights total — Axelit doesn't use thin (300) or extra-bold (800). **This is good discipline.** Most templates use 6+ weights, dilutes hierarchy.

## 10 · Capital-letter / case rules

| Element | Case |
| ------- | ---- |
| Sidebar group separators (DASHBOARD, COMPONENT) | UPPERCASE |
| Badges (In Progress, Completed) | Title Case |
| Buttons (Save, Cancel) | Title Case |
| KPI labels (Total Hours, Product Sold) | Title Case |
| Headings (Project Status) | Title Case |
| Body text | Sentence case |
| Numeric (2,450, $6.56k) | tabular numerals (`font-feature-settings: 'tnum'` implied) |

**Rule extracted**: UPPERCASE reserved for sidebar group dividers. Title Case for interactive labels and headings. Sentence case for prose.

## 11 · Icon-to-text balance

| Pattern | Used for |
| ------- | -------- |
| Icon ONLY (no label) | Header utility buttons (search, fullscreen, notifications), Inline row actions |
| Icon + label | Sidebar nav items, buttons (CTAs), tag chips |
| Label ONLY | Sidebar group separators, badges, table cell text |
| Icon + number badge | Header notifications (🔔 + 5), Sidebar nav items (Dashboard + 4) |

**Rule extracted**: Icon-only is acceptable for high-frequency utility actions where users build muscle memory. Always pair icon+label for primary CTAs and navigation.

## 12 · Density-emphasis trade-off

```
DENSE                                              SPARSE
←─────────────────────────────────────────────────→

  Table rows ←─ KPI tiles ←─ Cards ←─ Modal ←─ Hero
  (12px cell)  (16px pad)   (20px)   (32px)   (none)
```

**Rule extracted**: Density inversely tracks importance:
- More important = more space
- Less important = denser packing

## 13 · Page-level hierarchy

Each page has a hierarchy from top to bottom (3-5 zones):

1. **Top zone (page identity)** — title, breadcrumb, page CTA
2. **Above-fold action zone** — KPIs or hero or filter bar
3. **Main content zone** — the actual work surface (table, form, board)
4. **Supplementary zone** — sidebar widgets, activity feeds (if applicable)
5. **Footer zone** — copyright + version + help

In Axelit's `/dashboard/project`, the title is *missing* (there's a sr-only H1 only). KPI strip is the de-facto top. This is acceptable for a "home" dashboard but feels naked on deep pages.

**OET recommendation**: Always show a visible page title + breadcrumb in the top zone, even when it duplicates the active sidebar label. Don't rely on sr-only.

## 14 · Hierarchical use of color tinting

```
PRIMARY (loudest)
  bg-primary {solid 100%}             ← page-level CTA
  btn-primary {solid 100%}            ← hero action
  badge bg-primary {solid 100%}       ← critical status

SECONDARY (medium)
  btn-light-primary {30% tint}        ← context action
  badge bg-light-primary {30% tint}   ← active status

TERTIARY (quietest)
  text-primary {plain text}           ← link
  border-primary {1px border}         ← outline button
```

**Rule extracted**: Same hue, three intensities (solid → tint → text/border). This is the entire emphasis ladder for ONE color.

## 15 · Final principle: predictable inconsistency

Axelit has *too many components* — 23 UI Kit pages + 9 Advanced UI pages = 32 component families. Quantity hurts consistency. Even Axelit can't keep them all aligned (e.g. button focus ring is Bootstrap blue, not Axelit violet).

**OET rule extracted**: Define a **minimum viable component set** (~15 components) for the admin rebuild:
1. Layout: Shell, Sidebar, Header, Footer, PageHeader
2. Surface: Card, Panel, Modal, Drawer, Tabs
3. Data: Table, KPITile, ChartContainer, EmptyState, Skeleton
4. Input: Button, IconButton, Input, Select, Combobox, Checkbox, Switch
5. Display: Badge, Avatar, AvatarGroup, ProgressBar, Tooltip
6. Feedback: Toast, Alert, Spinner, ErrorState

Anything beyond ~25 components in an admin is template bloat.
