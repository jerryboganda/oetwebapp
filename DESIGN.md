# Design System: OET Prep Learner Platform
**Project ID:** jerryboganda/oetwebapp

This document is the source of truth for learner-facing UI. The dashboard is the canonical reference; every other page should feel like the same product, just with a different level of density or focus.

## 1. Visual Theme & Atmosphere
- Warm clinical calm, not sterile.
- Airy cream canvas, not dense dark chrome.
- Premium academic workspace, not a marketing landing page.
- Supportive and trustworthy, with gentle motion and soft cards.
- Data-rich, but never cold or cluttered.

The overall feeling is a guided study environment. Surfaces are soft, spacing is generous, and the interface should reduce anxiety rather than create it.

## 2. Color Palette & Roles
| Token | Hex | Role | Notes |
| --- | --- | --- | --- |
| Primary Violet | `#7c3aed` | Primary actions, active nav, focus states, hero accents | The signature brand accent. Use for CTAs and selected states. |
| Primary Deep Violet | `#6d28d9` | Hover and pressed states | Slightly darker for interaction depth. |
| Lavender Mist | `#ede9fe` | Soft highlights, icon tiles, chips, subtle backgrounds | Used for calm emphasis, not large surfaces. |
| Cream Canvas | `#f7f5ef` | App backdrop | The default page background in light mode. |
| Surface White | `#fffefb` | Cards, panels, hero containers | Main content surface. |
| Navy Ink | `#0f172a` | Headings, primary text, important icons | Highest-contrast text color in light mode. |
| Muted Slate | `#526072` | Supporting text, labels, metadata | Use for second-level information. |
| Border Mist | `#d8e0e8` | Card borders, dividers, field outlines | Keep borders subtle and light. |
| Success Green | `#10b981` | Streaks, completed states, positive trends | Use for reassurance and completion. |
| Warning Amber | `#d97706` | Freeze states, cautions, pending attention | Reserve for non-critical warnings. |
| Danger Red | `#ef4444` | Errors, destructive actions, critical alerts | Use sparingly and clearly. |
| Info Blue | `#2563eb` | Informational states, chart series, detail highlights | Good for neutral data emphasis. |
| Dark Canvas | `#07111d` | Dark mode page background | Deep blue-black, not pure black. |
| Dark Surface | `#0f172a` | Dark mode cards and panels | The main inverted surface color. |
| Dark Text | `#e5eef9` | Dark mode primary text | Keep contrast soft but legible. |
| Soft Dark Border | `#1f2937` | Dark mode borders and separators | Do not over-contrast the border layer. |

Use tint-based backgrounds for chips, icon badges, and status chips. Reserve saturated fills for actions and selected states.

## 3. Typography Rules
- Primary typeface: Manrope for all UI text.
- Display typeface: Fraunces only for brand lockups or rare premium emphasis.
- Headings should be semibold or bold with tight tracking and clear hierarchy.
- Eyebrows should be 11-12px, uppercase, and widely tracked.
- Body text should stay in the 14-16px range with calm line height.
- Data figures can be larger and heavier, but they should still feel editorial, not dashboard-brutal.

The dashboard uses Manrope for navigation, cards, controls, and most content. Fraunces appears sparingly in brand moments only. Do not overuse display type on utility pages.

## 4. Component Stylings
| Component | Styling | Behavior |
| --- | --- | --- |
| Buttons | Rounded-lg base shape, 44-48px touch targets, violet primary fill, navy secondary fill, light outline and ghost variants | Hover should gently lift or tint, and active should scale down slightly. |
| Cards and containers | Rounded-2xl by default, 1px border, Surface White background, shadow-sm baseline, shadow-clinical on hover | Use a slightly larger radius for hero cards and larger feature panels. |
| Inputs and forms | Soft surface fill, 1px border, rounded-lg or rounded-xl, no native chrome, clear primary focus ring | Filters should feel like part of the system, not default browser controls. |
| Navigation | Sticky glass top nav, sticky desktop sidebar, bottom nav on mobile, pill-like active state highlight | Navigation should always feel anchored and calm, never loud. |
| Data visuals | White chart canvas inside rounded surfaces, faint gridlines, small legends, one dominant accent per series | Charts should breathe and stay easy to read at a glance. |
| Empty states | Centered, explanatory, and framed inside a card or dashed surface | Never leave a blank region without context and a next step. |
| Motion | 160-320ms springs and fades, 1px hover lift, subtle route transitions, staggered entry for sections | Motion should clarify hierarchy, not decorate it. |

Preferred primitives: `AppShell`, `LearnerDashboardShell`, `LearnerWorkspaceContainer`, `TopNav`, `Sidebar`, `BottomNav`, `LearnerPageHero`, `LearnerSurfaceSectionHeader`, `LearnerSurfaceCard`, `Card`, `Button`, `ProgressBar`, `MotionSection`, and `MotionItem`.

## 5. Layout Principles
- Treat the shell as the canvas: sticky chrome, centered workspace, and a single scrollable content area.
- Keep the main workspace around 1200px wide with generous horizontal padding.
- Start pages with a hero surface or summary card, then move into card clusters and supporting rails.
- Use the dashboard composition as the baseline: hero, action cards, main grid, then a supporting side rail.
- Utility pages like Grammar and Progress should still inherit the same hero-card rhythm.
- Use white space as structure. Separate sections with breathing room rather than hard separators.
- Let cards fill their rails. Do not let content float directly on the page background.

The best pages feel like a calm sequence of surfaces, not a list of disconnected widgets.

## 6. Depth & Elevation
- The page background is a warm cream field with soft ambient color blooms and a faint grid veil.
- Cards sit above the canvas with border plus shadow, not shadow alone.
- Hover elevation should be minimal: one step up, slightly darker border, slightly stronger shadow.
- The top nav uses a glass-panel treatment with blur and translucent border.
- The sidebar and modals sit above the base chrome with stronger separation.
- Dark mode should deepen surfaces, not replace the system with pure black blocks.

Avoid heavy shadows, harsh outlines, and neon glow. The interface should feel premium because it is controlled, not because it is loud.

## 7. Do's and Don'ts
Do:
- Use the shared dashboard primitives for all learner pages.
- Keep purple as the primary accent and navy as the anchor text color.
- Use soft corners, calm spacing, and restrained motion.
- Show explicit empty states when data is missing.
- Keep the shell, chrome, and spacing consistent across dashboard, grammar, progress, and content pages.

Don't:
- Don't use default browser controls or isolated dark selects that ignore the shell language.
- Don't make a utility page feel like a separate product.
- Don't remove the ambient background or collapse everything into a flat white page.
- Don't overuse Fraunces or all-caps headings.
- Don't add loud gradients, aggressive shadows, or extra visual systems.
- Don't leave charts or panels empty without explanation.

## 8. Responsive Behavior
- Mobile: the sidebar collapses into top navigation, the bottom nav appears, and content stacks to one column.
- Mobile touch targets should stay at least 44px tall.
- Tablet: cards collapse gracefully, but hero surfaces should keep their breathing room and hierarchy.
- Desktop: the sidebar and top nav stay sticky while the workspace scrolls inside the viewport.
- Desktop grids can move to 2-column or 12-column arrangements as long as the card rhythm stays intact.
- Dedicated tool pages can split into dual panels on desktop, but they must keep the same surface, border, and typography language.

## 9. Agent Prompt Guide
- Build new screens as if they belong to the dashboard first, not as isolated pages.
- Keep the mood warm, clinical, and premium, with cream surfaces and violet accents.
- Use Manrope for UI and Fraunces only for brand moments.
- Prefer rounded 24-32px surfaces, soft shadows, and gentle motion.
- If a page feels plain, add a hero surface, a supporting section header, and at least one card rail.
- If data is missing, render a calm empty state card with a next action.

Ready-to-use prompt: Build this page in the OET Prep dashboard system. Use a warm cream canvas, a violet primary accent, navy headlines, soft rounded cards, sticky glass chrome, and calm empty states. Keep the layout airy, data-rich, and consistent with the dashboard, not like a separate product.
