# OET UI — how to build with this design system

This is the **oet-with-dr-hesham** component library: a clinical, professional design system for an OET
(Occupational English Test) exam-prep app used by nurses, doctors and pharmacists. The accent
colour is a calm violet/indigo (`primary`); surfaces are white-ish with soft borders and the
`shadow-clinical` elevation. Compose screens from the real components below — don't re-implement them.

## Setup

- Link **`styles.css`** once. It `@import`s the full token + component stylesheet
  (`_ds_bundle.css`), so every utility class and `--color-*` token below resolves. There is
  **no theme/context provider to wrap** — the styling ships in the stylesheet; just render the
  components. (Components are client React and animate in via `motion/react`; nothing extra to set up.)
- Default appearance is light mode.

## Styling idiom — Tailwind utilities with semantic tokens

Style your own layout glue (wrappers, grids, spacing) with this system's **semantic Tailwind
classes**, not raw hex or generic greys. Use these real token families (all defined in `styles.css`):

| Purpose | Classes |
|---|---|
| Brand accent | `bg-primary` `text-primary` `border-primary` (violet/indigo); dark text `bg-navy` `text-navy` |
| Surfaces | `bg-surface` (cards) · `bg-background-light` (subtle insets) · `bg-lavender` (soft accent) |
| Text | `text-navy` (headings/body) · `text-muted` (secondary) · `text-white` (on accent) |
| Borders | `border border-border` · hover `border-border-hover` |
| Danger | `bg-danger` `text-danger` (destructive) |
| Radius | `rounded-lg` `rounded-xl` `rounded-2xl` `rounded-3xl` `rounded-full` |
| Elevation | `shadow-sm` `shadow-clinical` `shadow-xl` |

Semantic status colours (success/warning/info) come from the standard Tailwind palette as used by
`Badge`/`InlineAlert` — prefer those components over hand-rolling status chips. Tokens are also
available as CSS vars (`var(--color-primary)`, `--color-navy`, `--color-surface`, `--color-border`,
`--color-muted`, `--color-danger`) if you need them in custom CSS.

## Where the truth lives

- **`styles.css`** (and its `@import`ed `_ds_bundle.css`) — the authoritative token + class list.
- **`components/<group>/<Name>/<Name>.prompt.md`** — per-component usage + examples.
- **`components/<group>/<Name>/<Name>.d.ts`** — the exact props contract.

## Idiomatic example

```tsx
const { Card, CardHeader, CardTitle, CardContent, CardFooter, Button, Badge } = window.OetUI;

<div style={{ display: 'grid', gap: 16 }} className="bg-background-light">
  <Card>
    <CardHeader>
      <div className="flex items-center justify-between">
        <CardTitle>Reading — Part B</CardTitle>
        <Badge variant="info">10 min</Badge>
      </div>
    </CardHeader>
    <CardContent>
      <p className="text-muted">Six short workplace texts. Choose the writer's intent.</p>
    </CardContent>
    <CardFooter>
      <Button>Begin section</Button>
      <Button variant="ghost">Review later</Button>
    </CardFooter>
  </Card>
</div>
```

Use library components for controls (buttons, cards, badges, inputs, alerts, tabs, …) and the
semantic classes above only for your own surrounding layout. Match the calm, clinical, generously
-spaced feel of the examples in each component's `.prompt.md`.
