# DESIGN_TOKENS.md — Complete token reference

Every CSS custom property captured from Axelit's `:root`, in the exact value it ships with. Use this as the single source of truth when porting tokens to OET (with OET-appropriate hue substitutions).

---

## Source

Captured from `https://axelit-next.vercel.app/dashboard/project` on 2026-05-24 via Playwright `getComputedStyle(document.documentElement)` enumeration of all `--*` properties.

## Conventions

- **Bootstrap-prefixed tokens** (`--bs-*`) — inherited from Bootstrap 5.3, mostly unchanged.
- **Axelit-prefixed tokens** (no prefix or `--app-*`, `--font-*`, `--theme-*`) — the template author's overrides.
- **`--dt-*` tokens** — DataTables.net plugin tokens.
- **`--animate-*` tokens** — animate.css tokens.

Colors below are quoted **verbatim** from the live CSS. Where the value is comma-separated RGB (e.g. `140,118,240`), it's stored as channel values for use inside `rgba()` (Axelit pattern: `rgba(var(--primary), 0.3)`).

---

## 1 · Brand color roles (Axelit overrides Bootstrap defaults)

```css
:root {
  --primary:   140, 118, 240;   /* violet */
  --secondary: 100, 100, 100;   /* grey */
  --success:   20, 120, 52;     /* deep green */
  --danger:    240, 10, 200;    /* magenta — UNUSUAL */
  --warning:   215, 220, 65;    /* chartreuse — UNUSUAL */
  --info:      46, 94, 231;     /* royal blue */
  --light:     215, 208, 200;   /* warm beige */
  --dark:      40, 38, 50;      /* near-black with purple tilt */
  --white:     255, 255, 255;
  --black:     0, 0, 0;
}
```

## 2 · Dark variants of role colors (used for "light button" text)

```css
:root {
  --primary-dark:   36, 17, 135;
  --secondary-dark: 106, 90, 100;
  --success-dark:   52, 50, 46;
  --danger-dark:    102, 15, 106;
  --warning-dark:   99, 89, 29;
  --info-dark:      8, 60, 128;
  --dark-dark:      30, 29, 35;
  --dark-light:     110, 70, 90;
  --landing-dark:   32, 35, 53;
}
```

## 3 · Bootstrap-prefixed color tokens (inherited)

```css
:root, [data-bs-theme="light"] {
  --bs-blue:    #0d6efd;
  --bs-indigo:  #6610f2;
  --bs-purple:  #6f42c1;
  --bs-pink:    #d63384;
  --bs-red:     #dc3545;
  --bs-orange:  #fd7e14;
  --bs-yellow:  #ffc107;
  --bs-green:   #198754;
  --bs-teal:    #20c997;
  --bs-cyan:    #0dcaf0;
  --bs-black:   #000;
  --bs-white:   #fff;
  --bs-gray:    #6c757d;
  --bs-gray-dark: #343a40;

  --bs-gray-100: #f8f9fa;
  --bs-gray-200: #e9ecef;
  --bs-gray-300: #dee2e6;
  --bs-gray-400: #ced4da;
  --bs-gray-500: #adb5bd;
  --bs-gray-600: #6c757d;
  --bs-gray-700: #495057;
  --bs-gray-800: #343a40;
  --bs-gray-900: #212529;

  /* Axelit OVERRIDES Bootstrap defaults for these: */
  --bs-primary:   #0d6efd;        /* kept blue — but Axelit ALSO defines --primary: 140,118,240 violet */
  --bs-danger:    #f00ac8;        /* magenta — overridden from default #dc3545 */
  /* (other roles kept Bootstrap defaults) */
}
```

## 4 · Surface (background) tokens

```css
:root {
  --bs-body-bg:        #fff;
  --bs-secondary-bg:   #e9ecef;
  --bs-tertiary-bg:    #f8f9fa;
  --bs-light-bg-subtle: #fcfcfd;
  --bs-dark-bg-subtle:  #ced4da;

  --bodybg-color:       #f6f6f6;   /* outer page background */
  --body-color:         #f9f9f9;   /* secondary surface */
  --light-gray:         #f4f7f8;
  --light-gray-bg:      #f8f8f8;
}
```

## 5 · Text color tokens

```css
:root {
  --bs-body-color:     #212529;
  --bs-emphasis-color: #000;
  --bs-secondary-color: rgba(33,37,41, 0.75);
  --bs-tertiary-color:  rgba(33,37,41, 0.5);

  --font-color:           #15264b;   /* navy default */
  --font-title-color:     #1c3264;   /* navy slightly darker */
  --font-secondary-color: #22242c;
  --font-light-color:     #a0a0b0;
  --theme-body-font-color: #2b2b2b;
  --theme-body-sub-title-color: rgba(43,43,43, 0.7);
}
```

## 6 · Border + grid tokens

```css
:root {
  --bs-border-color:             #dee2e6;
  --bs-border-color-translucent: rgba(0,0,0, 0.175);
  --bs-border-width:             1px;
  --bs-border-style:             solid;

  --border_color: #e0dfd6;                          /* warm beige */
  --grid_color:   rgba(144,164,246, 0.21);          /* cool violet */
}
```

## 7 · Border-radius scale

```css
:root {
  --bs-border-radius-sm:   0.25rem;     /* 4px */
  --bs-border-radius:      1.8rem;      /* 28.8px — OVERRIDDEN from BS default 0.375rem */
  --bs-border-radius-lg:   0.5rem;      /* 8px */
  --bs-border-radius-xl:   1rem;        /* 16px */
  --bs-border-radius-xxl:  2rem;        /* 32px */
  --bs-border-radius-2xl:  var(--bs-border-radius-xxl);
  --bs-border-radius-pill: 50rem;       /* 800px effective — fully rounded */

  --app-border-radius: 1.8rem;          /* 28.8px — Axelit's signature radius */
  --bs-accordion-inner-border-radius: 0.5rem;  /* accordion uses 8px specifically */
}
```

## 8 · Box-shadow scale

```css
:root {
  --bs-box-shadow:       0 0.5rem 1rem rgba(0,0,0, 0.15);
  --bs-box-shadow-sm:    0 0.125rem 0.25rem rgba(0,0,0, 0.075);
  --bs-box-shadow-lg:    0 1rem 3rem rgba(0,0,0, 0.175);
  --bs-box-shadow-inset: inset 0 1px 2px rgba(0,0,0, 0.075);

  --box-shadow:    0px 0px 21px 3px rgba(100, 100, 100, 0.05);  /* default — AMBIENT */
  --hover-shadow:  0 0.5rem 2rem #f4f7f8;                       /* lift on hover */
  --bottom-shadow: 0 8px 6px -5px #f4f7f8;                      /* sticky header */
}
```

## 9 · Typography scale tokens

```css
:root {
  --bs-body-font-family: var(--bs-font-sans-serif);    /* system stack fallback */
  --bs-body-font-size:   1rem;
  --bs-body-font-weight: 400;
  --bs-body-line-height: 1.5;

  --bs-font-sans-serif: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", "Liberation Sans", Arial, sans-serif, "Apple Color Emoji", "Segoe UI Emoji", "Segoe UI Symbol", "Noto Color Emoji";
  --bs-font-monospace:  SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;

  /* Axelit override — Montserrat for everything */
  --font-Montserrat:  "Montserrat", system-ui;
  --font-montserrat:  "Montserrat", "Montserrat Fallback";

  /* Custom scale tokens */
  --font-size:        14px;
  --p-font-size:      14px;
  --p-line-height:    1.6;
  --btn-font-size:    15px;
  --h1-font-size:     2.5rem;     /* 40px */
  --h2-font-size:     2rem;       /* 32px */
  --h3-font-size:     1.75rem;    /* 28px */
  --h4-font-size:     1.25rem;    /* 20px */
  --h5-font-size:     1.125rem;   /* 18px */
  --h6-font-size:     1rem;       /* 16px */
}
```

## 10 · Layout tokens

```css
:root {
  --sidebar-width:  17rem;       /* 272px expanded */
  --semi-nav:       4.5rem;      /* 72px collapsed */
  /* header-height and main-padding-top observed empirically: 80px / 32px */
}
```

## 11 · Bootstrap breakpoints (universal)

```css
:root {
  --bs-breakpoint-xs:  0;
  --bs-breakpoint-sm:  576px;
  --bs-breakpoint-md:  768px;
  --bs-breakpoint-lg:  992px;     /* sidebar collapses below this */
  --bs-breakpoint-xl:  1200px;
  --bs-breakpoint-xxl: 1400px;
}
```

## 12 · Motion tokens

```css
:root {
  --animate-duration: 1s;
  --animate-delay:    1s;
  --animate-repeat:   1;
  --app-transition:   all 0.3s ease;     /* ANTI-PATTERN — see Notes in 01-design.md */
}
```

## 13 · Focus ring tokens

```css
:root {
  --bs-focus-ring-width:   0.25rem;    /* 4px */
  --bs-focus-ring-opacity: 0.25;
  --bs-focus-ring-color:   rgba(13, 110, 253, 0.25);   /* Bootstrap blue — NOT Axelit's violet */
}
```

## 14 · Form validation tokens

```css
:root {
  --bs-form-valid-color:        #198754;
  --bs-form-valid-border-color: #198754;
  --bs-form-invalid-color:        #dc3545;
  --bs-form-invalid-border-color: #dc3545;
}
```

## 15 · Link tokens

```css
:root {
  --bs-link-color:          #0d6efd;     /* Bootstrap blue, NOT Axelit violet — visible inconsistency */
  --bs-link-color-rgb:      13, 110, 253;
  --bs-link-decoration:     underline;
  --bs-link-hover-color:    #0a58ca;
  --bs-link-hover-color-rgb: 10, 88, 202;
  --link-color: var(--primary-color);     /* unused — refers to undefined --primary-color */
}
```

## 16 · Code highlight tokens

```css
:root {
  --bs-code-color:      #d63384;     /* pink */
  --bs-highlight-color: #212529;
  --bs-highlight-bg:    #fff3cd;     /* yellow */
}
```

## 17 · Bootstrap "subtle" + emphasis pairs (full set)

For each role, Bootstrap defines: `text-emphasis`, `bg-subtle`, `border-subtle`. Axelit inherits.

```css
:root, [data-bs-theme="light"] {
  --bs-primary-text-emphasis:   #052c65;
  --bs-secondary-text-emphasis: #2b2f32;
  --bs-success-text-emphasis:   #0a3622;
  --bs-info-text-emphasis:      #055160;
  --bs-warning-text-emphasis:   #664d03;
  --bs-danger-text-emphasis:    #58151c;
  --bs-light-text-emphasis:     #495057;
  --bs-dark-text-emphasis:      #495057;

  --bs-primary-bg-subtle:   #cfe2ff;
  --bs-secondary-bg-subtle: #e2e3e5;
  --bs-success-bg-subtle:   #d1e7dd;
  --bs-info-bg-subtle:      #cff4fc;
  --bs-warning-bg-subtle:   #fff3cd;
  --bs-danger-bg-subtle:    #f8d7da;
  --bs-light-bg-subtle:     #fcfcfd;
  --bs-dark-bg-subtle:      #ced4da;

  --bs-primary-border-subtle:   #9ec5fe;
  --bs-secondary-border-subtle: #c4c8cb;
  --bs-success-border-subtle:   #a3cfbb;
  --bs-info-border-subtle:      #9eeaf9;
  --bs-warning-border-subtle:   #ffe69c;
  --bs-danger-border-subtle:    #f1aeb5;
  --bs-light-border-subtle:     #e9ecef;
  --bs-dark-border-subtle:      #adb5bd;
}
```

## 18 · Gradient tokens (decorative, sparing use)

```css
:root {
  --primary-gradient:     linear-gradient(50deg, rgba(140,118,240,1) 10%, rgba(240,10,200,1) 50%, rgba(215,220,65,1) 100%);
  --secondary-gradient:   linear-gradient(50deg, rgba(140,118,240,1) 30%, rgba(20,120,52,1) 50%, rgba(140,118,240,1) 100%);
  --dark-gradient:        linear-gradient(50deg, rgba(140,118,240,1) 30%, rgba(40,38,50,1) 50%, rgba(140,118,240,1) 100%);
  --body-bg-gradient:     linear-gradient(50deg, rgba(140,118,240, 0.09) 10%, rgba(255,255,255,1) 50%, rgba(140,118,240, 0.09) 100%);
  --app-gradient:         linear-gradient(50deg, rgba(140,118,240,1) 20%, rgba(20,120,52,1) 30%, rgba(140,118,240,1) 50%, rgba(215,220,65,1) 100%);
  --primary-gradient-color: linear-gradient(45deg, rgba(140,118,240,1) 10%, rgba(140,118,240, 0.6) 50%, rgba(140,118,240, 0.3) 100%);
  --bs-gradient:          linear-gradient(180deg, rgba(255,255,255, 0.15), rgba(255,255,255, 0));
}
```

## 19 · Social color tokens (brand colours of social platforms, RGB)

```css
:root {
  --facebook:  59, 89, 152;
  --twitter:   85, 172, 238;
  --pinterest: 189, 8, 28;
  --linkedin:  0, 119, 181;
  --reddit:    255, 69, 0;
  --whatsapp:  67, 216, 84;
  --gmail:     234, 67, 53;
  --telegram:  0, 64, 93;
  --youtube:   205, 32, 31;
  --vimeo:     26, 183, 234;
  --behance:   23, 105, 255;
  --github:    0, 64, 93;
  --skype:     0, 175, 240;
  --snapchat:  255, 250, 55;
}
```

## 20 · DataTables-specific tokens (will go away when OET drops DataTables)

```css
:root {
  --dt-row-selected:            13, 110, 253;
  --dt-row-selected-text:       255, 255, 255;
  --dt-row-selected-link:       9, 10, 11;
  --dt-row-selected-stripe-alpha: 0.923;
  --dt-row-selected-column-ordering-alpha: 0.919;
  --dt-row-hover:               0, 0, 0;
  --dt-row-hover-alpha:         0.035;
  --dt-row-stripe:              0, 0, 0;
  --dt-row-stripe-alpha:        0.023;
  --dt-column-ordering:         0, 0, 0;
  --dt-column-ordering-alpha:   0.019;
  --dt-html-background:         white;
}
```

---

## OET porting checklist

When mapping these to OET admin tokens:

1. **Rename `--bs-*` to `--admin-*`** — strip the Bootstrap prefix; OET admin won't ship Bootstrap.
2. **Substitute hue values** — replace violet/magenta/chartreuse with OET's clinical blue / red / amber.
3. **Drop `--dt-*`** — TanStack Table doesn't need these.
4. **Drop social colors** — OET admin has no social-share UI.
5. **Reduce gradient set** — keep at most `--primary-gradient`, drop the rest.
6. **Pick one radius scale** — recommend dropping the 28.8px-dominant pattern; use `4 / 8 / 12 / 16` instead.
7. **Pick one font** — confirm whether OET wants Montserrat or `Geist` (per OET memory files) or sticks with Tailwind's default.
8. **Audit `--app-transition: all` and narrow** to `transform, opacity, background-color` specifically.
9. **Set `--bs-focus-ring-color` to the OET primary** at 25% alpha — fixes Axelit's mismatched focus ring.
