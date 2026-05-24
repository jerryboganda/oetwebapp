# Axelit DNA Study — OET Admin Panel Rebuild

**Purpose**: Forensic design DNA extraction from `https://axelit-next.vercel.app` (Axelit by la-themes, ThemeForest commercial template) for the **OET admin panel rebuild**.

**Method**: Hallmark `study` skill in URL-mode + live Playwright browser crawl + Chrome DevTools MCP.

**Extraction date**: 2026-05-24
**Extractor**: Claude (Sonnet 4.6) via Claude Code, executing the `hallmark` skill installed at `.claude/skills/hallmark/`.

**Provenance** (per Hallmark §Refusal — URL-mode attestation):
- **Source**: `https://axelit-next.vercel.app/dashboard/project` (+ 9 sibling routes)
- **Attestation**: (b) — *public reference for own brand* — used to extract DNA for the OET admin rebuild
- **Confirmed source**: Customizer "Buy Now" link → `themeforest.net/user/la-themes/portfolio`
- **What is extracted**: macrostructure, archetypes, type roles, colour roles, density, motion stance
- **What is NOT extracted**: pixel copies, OET-inappropriate brand voice (playful emoji, six-accent palette), template-marketing chrome (customizer panel, Tawk.to chat)

---

## How to read this study (suggested order)

### Phase 1 — Foundation (the system)
1. **[00-ROUTE-INVENTORY.md](00-ROUTE-INVENTORY.md)** — what was crawled, what was missed
2. **[01-design.md](01-design.md)** — Hallmark portable design.md (the system, in one file)
3. **[02-ADMIN_DESIGN_SYSTEM.md](02-ADMIN_DESIGN_SYSTEM.md)** — comprehensive system overview
4. **[03-ADMIN_MACROSTRUCTURE.md](03-ADMIN_MACROSTRUCTURE.md)** — page skeleton + 4 reusable layout templates
5. **[04-COMPONENT_ARCHITECTURE.md](04-COMPONENT_ARCHITECTURE.md)** — every component inventoried (15 families)
6. **[05-UX_PATTERNS.md](05-UX_PATTERNS.md)** — interaction vocabulary
7. **[06-DESIGN_TOKENS.md](06-DESIGN_TOKENS.md)** — every CSS custom property captured verbatim
8. **[07-RESPONSIVE_SYSTEM.md](07-RESPONSIVE_SYSTEM.md)** — breakpoint behavior
9. **[08-ENTERPRISE_DASHBOARD_PATTERNS.md](08-ENTERPRISE_DASHBOARD_PATTERNS.md)** — 20 enterprise UX principles distilled
10. **[09-MOTION_AND_INTERACTION.md](09-MOTION_AND_INTERACTION.md)** — motion philosophy + recommended replacement stack
11. **[10-VISUAL_HIERARCHY_RULES.md](10-VISUAL_HIERARCHY_RULES.md)** — attention/emphasis hierarchy

### Phase 2 — Deep-dive specs (the gaps that were closed)
12. **[13-DARK-MODE-COMPLETE.md](13-DARK-MODE-COMPLETE.md)** ⭐ — Full dark mode spec — 28 token overrides, FOIT prevention, 3-state pattern, autofill fix, scrollbar theming, chart re-coloring
13. **[14-INTERACTIVE-STATES.md](14-INTERACTIVE-STATES.md)** ⭐ — All 8 states per component — focus-visible, hover-scoping for touch, ARIA contracts, transition specs
14. **[15-FORMS-COMPLETE.md](15-FORMS-COMPLETE.md)** ⭐ — Inputs, selects, comboboxes, file upload, date pickers, multi-step wizards, validation timing, autosave, recovery
15. **[16-CHARTS-COMPLETE.md](16-CHARTS-COMPLETE.md)** ⭐ — Recharts + ECharts decision matrix, sequential/diverging/categorical palettes, dark mode chart palette, sparklines, real-time
16. **[17-MODAL-DRAWER-COMPLETE.md](17-MODAL-DRAWER-COMPLETE.md)** ⭐ — Radix Dialog/AlertDialog/Drawer/Popover specs, focus trap, scroll lock, mobile sheets (Vaul), promise-based confirm()
17. **[18-AUTH-PAGES-COMPLETE.md](18-AUTH-PAGES-COMPLETE.md)** ⭐ — Passkeys (WebAuthn), magic links, OAuth/SSO, email-first flow, NIST 800-63B password rules, 2FA/TOTP, OWASP compliance
18. **[19-ERROR-EMPTY-STATES.md](19-ERROR-EMPTY-STATES.md)** ⭐ — 404/403/500/503, offline, empty-state taxonomy (first-time/filtered/loading/error), Next.js error boundaries

### Phase 3 — Synthesis (use it)
19. **[11-CONFIDENCE-GAP-ANALYSIS.md](11-CONFIDENCE-GAP-ANALYSIS.md)** — confidence scorecard (ALL ✅ HIGH after gap-closing pass)
20. **[12-REFACTORING-PLAYBOOK.md](12-REFACTORING-PLAYBOOK.md)** — concrete 10-day execution plan for the OET admin rebuild

## Raw evidence

- **[screenshots/](screenshots/)** — 10 full-page captures (desktop + mobile, light mode + dark attempt)
- **[raw-data/01-dashboard-project.json](raw-data/01-dashboard-project.json)** — verbatim JSON dump of every CSS token, every font, every layout dimension, every nav link, every button variant, every typography spec, every observed color, every observed border-radius from the live page

## Key conclusions

**What to inherit from Axelit** (the good DNA):
- Bento-grid macrostructure (KPI strip → mid-grid → table → activity rail)
- Fixed left sidebar (272px expanded / 72px collapsed) + 80px top header
- 8-role color system with paired dark variants for tinted-bg patterns
- Ambient-halo shadow (5% alpha, omnidirectional)
- Single-family typography (Montserrat or similar)
- Status-as-chip discipline (not as text)
- Stacked avatar overflow chips
- Tabs-inside-card for settings
- Sticky table headers with sortable columns
- Skeleton loading text placeholders

**What to discard** (the template-marketing voice):
- Six chromatic accents on one screen
- 28.8px radius on everything (use 12-16px for clinical voice)
- Heavy emoji in headings (`💼 🔥 ✨`)
- Three icon libraries simultaneously
- Welcome modal on every page load
- Floating runtime theme customizer
- `transition: all 0.3s ease` global
- DataTables.net (use TanStack Table)
- jQuery / Bootstrap JS (use React-native primitives)
- Tawk.to live chat in admin

**What Axelit doesn't address** (must invent for OET):
- Empty-state component pattern
- Bulk action toolbar (table header transforms on selection)
- Density toggle per admin role
- Command palette (Cmdk) for power users
- Audit log viewer specialized component

## How this study was invoked

The Hallmark skill is installed at `.claude/skills/hallmark/` (cloned from [nutlope/hallmark](https://github.com/nutlope/hallmark), MIT license). It's scoped to OET admin panel work only — see [`.claude/skills/hallmark/SKILL.md`](../../../.claude/skills/hallmark/SKILL.md) frontmatter and [`AGENTS.md` §Skill Routing](../../../AGENTS.md).

This entire study was produced by invoking:
```
Skill(skill: "hallmark", args: "study https://axelit-next.vercel.app/dashboard/project")
```

The skill enforced URL-mode protocol from [`.claude/skills/hallmark/references/study.md`](../../../.claude/skills/hallmark/references/study.md) — refusal check, remote URL safety, attestation, five-step extraction (Surface → Type → Structure → Motion → Rhythm), structured schema, diagnosis report.

## License + redistribution note

This study **describes patterns** from a commercial template; it does **not** redistribute Axelit's CSS, JavaScript, images, or other proprietary assets. Every code snippet in these files is original work derived from observed CSS values and standard Bootstrap/Tailwind/React patterns. The OET implementation built from this study will be original code using OET-licensed dependencies (Tailwind v4, Next.js 15, lucide-react, Radix UI, etc.) — no Axelit source code will be included.
