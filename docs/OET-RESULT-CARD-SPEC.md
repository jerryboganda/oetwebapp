# OET Statement of Results — Pixel-Faithful Design Contract

> **Status**: authoritative visual spec. This document describes the exact
> layout, typography, colours, and data bindings of the OET "Statement of
> Results" as issued by Cambridge Boxhill Language Assessment Trust (CBLA).
> The learner-facing result card in this app MUST match this specification
> exactly — no creative reinterpretation.
>
> Source of truth: reference screenshots in
> `Project Real Content/Create Similar Table Formats for Results to show to
> Candidates/`. Cross-checked against the public OET results portal
> (`occupationalenglishtest.org`).

---

## 1. Composition (top to bottom)

The Statement of Results has six stacked blocks, in order:

1. **Test Details table** — two-column key/value table, grey header strip.
2. **TEST RESULTS summary strip** — blue bar with the four scaled scores.
3. **Band chart** — the signature visual: a 500-unit vertical scale on the
   left, a coloured band-letter column (A / B / C+ / C / D / E), and four
   subtest columns (Listening, Reading, Speaking, Writing) each showing a
   horizontal bar at the learner's score.
4. **Certification stamp + signature** — circular "CBLA · OET · Cambridge ·
   Boxhill · Language · Assessment" stamp (right side) with Sujata Stead's
   signature and "CEO, CBLA" caption beneath it.
5. **Verification footer** — "Recognising organisations are required to
   validate this Statement of Results through our verification portal.
   https://www.occupationalenglishtest.org/organisations/results-verification/"
6. **Ownership footer** — "OET is owned by Cambridge Boxhill Language
   Assessment Trust (CBLA), a venture between Cambridge Assessment English
   and Box Hill Institute."

---

## 2. Test Details table

### Rows (in order)

| Field | Source in our data model |
|---|---|
| Candidate Name | `ApplicationUserAccount.DisplayName` |
| Candidate Number | `LearnerUser.CandidateNumber` (generate if absent: `OET-{userId[0..6]}-{attemptId[0..6]}`) |
| Date of Birth | `LearnerRegistrationProfile.DateOfBirth` |
| Gender | `LearnerRegistrationProfile.Gender` |
| Venue Name | For mock attempts: `"OET Prep — Practice Mock"`. For real: per-attempt metadata. |
| Venue Number | Mock: `"PREP-{attemptId[0..4]}"`. Real: supplied. |
| Venue Country | Learner profile country. |
| Test Date | `Attempt.CompletedAt` (DD MMM YYYY) |
| Test Delivery Mode | `"OET on computer (practice)"` for mock |
| Profession | `LearnerUser.ProfessionId` → display name |

### Styling

- Table width: 100% of result container.
- First column: 40% width, grey background `#E5E5E5`, left-aligned,
  padding `8px 12px`, `font-family` Arial, `font-size` 13px, `color` `#333`.
- Second column: 60% width, white background, left-aligned, same padding,
  same font, `font-weight: 500`.
- Row separator: 1px solid `#D0D0D0`.
- Section header row (`TEST DETAILS:`): grey bar `#9A9A9A`, white text,
  uppercase, bold, 14px, full width.

---

## 3. TEST RESULTS summary strip

### Structure

One row, two-line content:
- Line 1 (headers): "Listening:", "Reading:", "Speaking:", "Writing:"
- Line 2 (values): the four numeric scores (0–500)

### Styling

- Full-width horizontal band, background `#1487BF` (OET blue).
- Height: ~80px total (40px header line + 40px score line).
- Header text: Arial, 15px, **bold**, colour white, left-padded 12px per cell.
- Score text: Arial, 20px, **bold**, colour white, centred in each cell,
  white horizontal separator lines between cells (1px, `rgba(255,255,255,0.4)`).
- Four equal-width cells (25% each).
- Section header row above (`TEST RESULTS`): lighter blue `#1D96D2`, white
  text, uppercase, 14px, left-padded 12px, height 30px.

---

## 4. Band chart (the signature visual)

This is the mission-critical piece. Layout:

```
┌──────────────────────────────────────────────────────────────┐
│                    (chart title row)                         │
│                                                              │
│             Listening  Reading  Speaking  Writing  ← labels  │
│  ┌──┐     ┌────────┬────────┬────────┬────────┐              │
│  │A │500  │░░░░░░░░│░░░░░░░░│░░░░░░░░│░░░░░░░░│              │
│  │──│450  │ - - - -│─[430]──│- - - - │- - - - │  ← dashed    │
│  │B │400  │░░░░░░░░│░░░░░░░░│─[420]──│░░░░░░░░│    band      │
│  │──│     │░░░░░░░░│░░░░░░░░│░░░░░░░░│─[370]──│    thresholds│
│  │C+│350  │- - - - │- - - - │- - - - │- - - - │              │
│  │  │300  │                                   │              │
│  │C │     │                                   │              │
│  │──│250  │                                   │              │
│  │  │200  │ - - - - - - - - - - - - - - - -  │              │
│  │D │150  │                                   │              │
│  │  │100  │ - - - - - - - - - - - - - - - -  │              │
│  │──│50   │                                   │              │
│  │E │0    │                                   │              │
│  └──┘     └───────────────────────────────────┘              │
└──────────────────────────────────────────────────────────────┘
```

### 4.1 Y-axis scale

- Left gutter: 44px wide.
- Numeric labels: `0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500`.
  Arial, 11px, colour `#555`, right-aligned within gutter.
- Tick marks: 4px horizontal lines at each label, colour `#999`.
- **Data range**: 0 to 500 (`scaledMax` from `lib/scoring.ts`).

### 4.2 Band column (the letter strip)

A 28px-wide vertical column immediately right of the Y-axis gutter, divided
into six coloured segments corresponding to OET grade bands. From top to bottom:

| Band | Range | Colour | Letter |
|---|---|---|---|
| A | 450–500 | `#2B6F9F` (deep teal-blue) | `A` |
| B | 350–450 | `#5B9AC4` (mid blue) | `B` |
| C+ | 300–350 | `#9DC0DB` (light blue) | `C+` |
| C | 200–300 | `#BDD6E8` (paler blue) | `C` |
| D | 100–200 | `#D6E3ED` (very pale blue-grey) | `D` |
| E | 0–100 | `#EBF0F4` (near-white blue-grey) | `E` |

Letter labels: Arial, 13px, bold, colour white (on A/B) or `#333` (on C+ / C /
D / E for contrast), centred within each segment.

### 4.3 Subtest columns

Four equal-width vertical columns to the right of the band column, each
labelled (top) with one of `Listening`, `Reading`, `Speaking`, `Writing`.

- Column background: `#E8E8E8` (light grey).
- 1px white gap between columns.
- Labels: Arial, 13px, colour `#555`, centred above column, padding 8px below.

### 4.4 Dashed threshold lines

Horizontal dashed lines span all four subtest columns at y-values
`100, 200, 300, 350, 450`. (Not 500, not 0.) These visually separate the
grade bands across the whole chart.

- Stroke: `#1487BF` (OET blue).
- Dash pattern: `8px dash, 6px gap`.
- Stroke width: 1.5px.

### 4.5 Score bars

For each subtest, a **single solid horizontal bar** at the y-coordinate of
the candidate's scaled score, spanning the full width of that subtest's column.

- Bar height: 18px.
- Bar fill: `#1487BF` (OET blue).
- Bar centre-line is at the score (so a score of 430 has the bar centred on
  430, extending 9px above and 9px below).
- Score label: Arial, 13px, **bold**, colour white, centred inside the bar.
- If a score falls within 9px of the top or bottom edge, the label is drawn
  outside the bar instead to stay readable (edge-case handled in code).

### 4.6 Overall chart dimensions

- Chart region height: 420px on desktop, 340px on mobile.
- Chart region width: 100% of container, min 480px desktop / 320px mobile.
- Padding: 16px top, 24px right, 24px bottom, 16px left.

---

## 5. Certification stamp + signature

Positioned flush-right beneath the band chart on desktop. Hidden on mobile
(the stamp is a print-only artefact; mobile shows a simpler verification
link instead).

- Circular stamp SVG: outer ring `#B0B0B0`, inner disc `#FFFFFF`, centre text
  `OET` in bold Arial grey, encircled text reads (clockwise from top):
  `CAMBRIDGE · BOXHILL · LANGUAGE · ASSESSMENT`.
- Signature: PNG asset `public/oet/signature-sujata-stead.png` (250×80, under the stamp).
- Caption: "Sujata Stead" (14px bold) newline "CEO, CBLA" (12px regular),
  left-aligned beneath signature.

---

## 6. Verification & ownership footer

Two paragraphs, Arial 11px, colour `#666`, centred:

> Recognising organisations are required to validate this Statement of
> Results through our verification portal.
> `https://www.occupationalenglishtest.org/organisations/results-verification/`

> OET is owned by Cambridge Boxhill Language Assessment Trust (CBLA), a
> venture between Cambridge Assessment English and Box Hill Institute.

On OUR learner portal, we add a third line in small italic:

> *This is a practice result generated by the OET Prep platform. It is not
> an official OET Statement of Results.*

This disclaimer is **legally required**. Without it, we'd be producing a
lookalike of an official regulated assessment document. The band chart
itself is informational/educational fair use; the stamp and signature
reproduce a trust mark, so the disclaimer is the line that keeps us on the
right side of the CBLA ToS.

---

## 7. Responsive behaviour

- Breakpoint: 768px.
- Mobile (<768px): chart uses 340px fixed height, band column narrows to
  22px, threshold labels reduced to 10px, stamp/signature hidden, test-details
  table collapses into a two-column card with labels stacked above values.
- Desktop (≥768px): full layout per §1–6.
- Print: A4 portrait, 100% zoom. Stamp and signature fully visible. Page
  break before verification footer forbidden.

---

## 8. Colour system (use `lib/tailwind` tokens / CSS vars)

Register these in `app/globals.css` as custom properties so print and web
stay identical:

```css
:root {
  --oet-blue: #1487BF;
  --oet-blue-hover: #1D96D2;
  --oet-band-a: #2B6F9F;
  --oet-band-b: #5B9AC4;
  --oet-band-c-plus: #9DC0DB;
  --oet-band-c: #BDD6E8;
  --oet-band-d: #D6E3ED;
  --oet-band-e: #EBF0F4;
  --oet-chart-bg: #E8E8E8;
  --oet-table-header: #9A9A9A;
  --oet-table-row-alt: #F5F5F5;
  --oet-text-primary: #333333;
  --oet-text-secondary: #555555;
  --oet-text-muted: #666666;
  --oet-tick: #999999;
}
```

---

## 9. Data contract (what the component receives)

```ts
export interface OetStatementOfResults {
  candidate: {
    name: string;
    candidateNumber: string;       // OET-XXXXXX-XXXXXX
    dateOfBirth?: string;          // DD MMM YYYY
    gender?: 'Male' | 'Female' | 'Non-binary' | 'Prefer not to say';
  };
  venue: {
    name: string;
    number: string;
    country: string;
  };
  test: {
    date: string;                  // DD MMM YYYY
    deliveryMode: string;          // "OET on computer (practice)"
    profession: string;            // "Medicine" | "Nursing" | ...
  };
  scores: {
    listening: number;             // 0–500, step 10
    reading: number;
    speaking: number;
    writing: number;
  };
  isPractice: boolean;             // true for our app — always, for now
  issuedAt: string;                // ISO timestamp
}
```

All four scores are validated against `lib/scoring.ts` before rendering —
a score that's not a multiple of 10 or out of 0–500 is a bug, not a display
problem. Component throws in dev, clamps in prod.

---

## 10. Acceptance criteria

The component is "done" when:

1. Placed side-by-side with any of the reference screenshots in
   `Project Real Content/Create Similar Table Formats…/`, a non-technical
   observer cannot distinguish the two at 100% zoom except for the watermark
   disclaimer in §6.
2. Band chart uses SVG (not canvas) so it is print-sharp at any zoom and
   accessible to screen readers via labelled `<title>` / `<desc>`.
3. Rendering pure from props, no data fetching inside — feed it fixtures.
4. Storybook-style fixtures cover: pass UK, pass USA (300 threshold),
   borderline, fail, perfect 500.
5. Print CSS (`@media print`) drops the outer app shell, keeps only the SoR
   card, sets `@page { size: A4 portrait; margin: 12mm; }`.
6. Accessibility: all data represented in a hidden `<table>` alongside the
   SVG; `aria-labelledby` / `aria-describedby` set appropriately; all
   interactive elements keyboard-navigable.
7. Unit tests: score-to-Y-position math, band assignment, edge-cases (score
   at exact boundaries 100/200/300/350/450), label-outside-bar fallback.
