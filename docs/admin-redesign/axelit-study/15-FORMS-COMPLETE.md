# 15 — FORMS: Complete spec + May 2026 industry guidelines

**Gap closed**: Forms (input/select/checkbox/radio/switch/datepicker/file upload/textarea)
**Method**: CSS rule extraction from Axelit's Bootstrap 5 stylesheet + 2026 industry pattern synthesis.
**Confidence**: **HIGH** ✅

---

## 1 · What Axelit ships (extracted from CSS)

Axelit uses Bootstrap 5 form controls verbatim. All form-related CSS keys off these classes:

| Class | Purpose |
| ----- | ------- |
| `.form-control` | text inputs, textareas, date/file inputs |
| `.form-select` | native select with custom arrow |
| `.form-check-input` | checkbox / radio |
| `.form-switch .form-check-input` | iOS-style toggle switch |
| `.form-range` | native range slider with custom thumb |
| `.form-label` | label above input |
| `.form-text` | helper / hint text |
| `.invalid-feedback`, `.is-invalid` | error state |
| `.valid-feedback`, `.is-valid` | success state |
| `.input-group` | input + addons (prefix/suffix buttons or text) |
| `.floating-label` | label-floats-on-focus pattern |

### Axelit-specific token overrides

```css
:root {
  --bs-border-radius: 1.8rem;                /* 28.8px on inputs too — pillow corners */
  --bs-form-control-bg: white;
  --bs-form-control-color: var(--bs-body-color);
  --bs-form-select-bg-img: url("...chevron-down-svg...");
}

[data-bs-theme="dark"] {
  --bs-form-control-bg: #333644;
  --bs-form-control-color: white;
  --bs-form-select-bg-img: url("...chevron-down-svg-light...");
}
```

### Extracted state CSS

```css
/* DEFAULT */
.form-control {
  display: block;
  width: 100%;
  padding: 0.375rem 0.75rem;                  /* 6px 12px */
  font-size: 1rem;
  font-weight: 400;
  line-height: 1.5;
  color: var(--bs-body-color);
  background-color: var(--bs-body-bg);
  border: 1px solid var(--bs-border-color);
  border-radius: var(--bs-border-radius);     /* INHERITS 28.8px — too pillowy for inputs */
  transition: border-color 150ms ease-in-out, box-shadow 150ms ease-in-out;
}

/* FOCUS — same 4px Bootstrap-blue ring as buttons */
.form-control:focus {
  color: var(--bs-body-color);
  background-color: var(--bs-body-bg);
  border-color: rgb(134, 183, 254);
  outline: 0;
  box-shadow: rgba(13, 110, 253, 0.25) 0 0 0 0.25rem;
}

/* DISABLED */
.form-control:disabled {
  background-color: var(--bs-secondary-bg);   /* #e9ecef grey */
  opacity: 1;
}

/* PLACEHOLDER */
.form-control::placeholder {
  color: var(--bs-secondary-color);            /* rgba(33,37,41, 0.75) */
  opacity: 1;
}

/* INVALID */
.form-control.is-invalid {
  border-color: var(--bs-form-invalid-border-color);  /* #dc3545 */
  padding-right: calc(1.5em + 0.75rem);
  background-image: url("data:image/svg+xml;...alert-icon");
  background-repeat: no-repeat;
  background-position: right calc(0.375em + 0.1875rem) center;
  background-size: calc(0.75em + 0.375rem) calc(0.75em + 0.375rem);
}
.form-control.is-invalid:focus {
  box-shadow: rgba(220, 53, 69, 0.25) 0 0 0 0.25rem;  /* red ring */
}

/* VALID */
.form-control.is-valid {
  border-color: var(--bs-form-valid-border-color);    /* #198754 */
  /* same icon-injection pattern, with check icon */
}
```

### Form-check (checkbox/radio)

```css
.form-check-input {
  width: 1em;
  height: 1em;
  margin-top: 0.25em;
  vertical-align: top;
  background-color: var(--bs-body-bg);
  background-repeat: no-repeat;
  background-position: center;
  background-size: contain;
  border: 1px solid var(--bs-border-color);
  appearance: none;
}

.form-check-input[type="checkbox"] { border-radius: 0.25em; }
.form-check-input[type="radio"]    { border-radius: 50%; }

.form-check-input:checked {
  background-color: var(--bs-form-check-bg);
  border-color: var(--bs-form-check-bg);
  background-image: url("data:image/svg+xml;...check-svg");
}

.form-check-input:focus { box-shadow: rgba(13, 110, 253, 0.25) 0 0 0 0.25rem; }
.form-check-input:active { filter: brightness(90%); }
.form-check-input:disabled { pointer-events: none; opacity: 0.5; }
```

### Form-switch (iOS toggle)

```css
.form-switch .form-check-input {
  width: 2em;
  margin-left: -2.5em;
  background-image: url("...circle-svg");        /* thumb */
  background-position: left center;
  border-radius: 2em;
  transition: background-position 150ms ease-in-out;
}

.form-switch .form-check-input:checked {
  background-position: right center;
  background-image: url("...circle-svg-light");
}
```

### Input-group (prefix/suffix)

```html
<div class="input-group">
  <span class="input-group-text">@</span>
  <input class="form-control" placeholder="username" />
  <button class="btn btn-outline-secondary">Verify</button>
</div>
```

Children share border-radius edges (first child gets left-radius, last child gets right-radius).

### Floating label

```html
<div class="form-floating">
  <input class="form-control" id="email" placeholder="email@example.com" />
  <label for="email">Email address</label>
</div>
```

Label floats to a smaller size above the input when focused or filled.

---

## 2 · MAY 2026 INDUSTRY STANDARD — Form Design Guidelines

### 2.1 · Label placement — TOP, always

Three positions are common:
- **Top** (label above input) — ✅ accepted as best practice 2020-2026
- **Left** (label inline) — only for compact admin forms with consistent column widths
- **Floating** — looks slick but degrades for screen readers and on autofill

**OET rule**: top labels always. Use floating-label ONLY for auth pages (single-field flows where charm matters).

### 2.2 · Single-column layout

Two columns split user attention. Even on wide desktops, **single-column forms convert better** (Baymard Institute, NN/g). Exceptions:
- Address blocks (city/state/zip can wrap horizontally)
- Date ranges (start/end side-by-side)
- Card details (number / expiry / CVC inline)

Otherwise: one input per row.

### 2.3 · Spacing scale

Within a form:
```css
--form-gap-tight:   0.5rem;   /* between input and helper text */
--form-gap-base:    1rem;     /* between input + label */
--form-gap-section: 2rem;     /* between form sections */
--form-gap-actions: 2.5rem;   /* before submit button row */
```

### 2.4 · Input size targets

| Variant | Height | Padding | Use |
| ------- | ------ | ------- | --- |
| Small (`sm`) | 32px | 4px 8px | dense inline forms (filters) |
| Default | 40px | 8px 12px | most forms |
| Large (`lg`) | 48px | 12px 16px | auth, marketing, mobile |

Touch target ≥ 44px on mobile (use `lg` for mobile-first forms).

### 2.5 · Field grouping — fieldset + legend

```html
<fieldset>
  <legend class="form-section-title">Account Settings</legend>
  <div class="form-field">…</div>
  <div class="form-field">…</div>
</fieldset>
```

ARIA-correct, semantically meaningful, screen-reader-friendly.

### 2.6 · Required field marking

Three accepted patterns:
1. **Asterisk (★)** in red after label — long established but redundant if MOST fields are required
2. **"Optional" tag** on optional fields — preferred when most are required (less visual noise)
3. **Required by default; mark optional**

OET should pick ONE convention per form and apply consistently. Don't mix.

### 2.7 · Validation timing — the four modes

```ts
type ValidationMode = 'onSubmit' | 'onBlur' | 'onChange' | 'onTouched';
```

- **onSubmit** (default for ALL forms) — validates only after submit attempt
- **onBlur** — validates when user leaves the field (after first touch)
- **onChange** — real-time, every keystroke (use sparingly — annoying)
- **onTouched** — first validation onBlur, then re-validates onChange

**OET rule**: `onTouched` for most forms. Email/URL formats benefit from real-time helper text ("must contain @").

### 2.8 · Error message anatomy

```tsx
<div className="form-field">
  <label htmlFor="email" className="form-label">
    Email <span className="text-danger" aria-hidden="true">*</span>
  </label>
  <input
    id="email"
    type="email"
    className={cn('form-control', error && 'is-invalid')}
    aria-invalid={!!error}
    aria-describedby={error ? 'email-error' : hint ? 'email-hint' : undefined}
    required
  />
  {hint && !error && (
    <p id="email-hint" className="form-text">{hint}</p>
  )}
  {error && (
    <p id="email-error" className="invalid-feedback flex items-center gap-1.5">
      <AlertCircle className="h-4 w-4" aria-hidden="true" />
      <span>{error}</span>
    </p>
  )}
</div>
```

**Critical**:
- Error appears IMMEDIATELY below input (not in tooltip)
- Error has ICON + TEXT (not color-only, WCAG 1.4.1)
- `aria-invalid="true"` on input
- `aria-describedby` points at error message id
- Helper text becomes error text (or both shown — error takes precedence)

### 2.9 · Error message copy — the 4 rules

1. **Be specific**: "Email must include @" not "Invalid input"
2. **Be helpful**: tell user how to fix, not just what's wrong
3. **Be brief**: ≤ 60 chars; if longer, link to docs
4. **Be human**: "Looks like that email isn't right — try again?" not "ERR_VALIDATION_001"

### 2.10 · Async validation

For server-validated fields (email already taken, username availability):
- Debounce 500ms
- Show pending state (spinner in field)
- Use `aria-busy="true"` while validating
- Show result inline (success or error)

### 2.11 · Select / Combobox — when to use which

| Variant | Use case |
| ------- | -------- |
| Native `<select>` | ≤ 10 options, no search needed, mobile-first (native picker is good UX) |
| Custom Combobox | > 10 options, search needed, custom rendering (avatars, descriptions) |
| Radio group | 2-5 mutually exclusive options where visibility matters |
| Multi-select chips | Tag/keyword selection (with autocomplete) |

**OET stack recommendation**: use **shadcn-ui Combobox** (built on `cmdk`) or **Radix Select**. Drop native select for anything > 5 options.

### 2.12 · File upload patterns

Three patterns:
1. **Drop zone** (drag + drop OR click) — best for single files / images
2. **Multi-file list** (queue with progress per file) — bulk imports
3. **Inline avatar uploader** (click to replace) — profile photos

OET stack: **react-dropzone** or **uploadthing**. Drop Axelit's FilePond (jQuery legacy).

```tsx
<Dropzone
  accept={{ 'application/pdf': ['.pdf'] }}
  maxSize={10 * 1024 * 1024}  // 10MB
  multiple={false}
  onDrop={handleDrop}
/>
```

State requirements:
- Default: dashed border + icon + "Drop files here, or click to browse" + "PDF, up to 10MB"
- Drag-over: solid border, primary tint background
- Uploading: progress bar per file
- Error: red border + error message ("File too large", "Wrong format")
- Success: file list with remove icon per file

### 2.13 · Date / time pickers

| Pattern | When |
| ------- | ---- |
| Native `<input type="date">` | Single date, low complexity, mobile-friendly |
| Custom popover calendar (Radix) | Range, complex constraints, custom rendering |
| Inline calendar | Booking flows, multi-date selection |
| Time picker (numeric scroll) | Mobile-first time entry |
| Combined datetime | Schedule, deadline |

**OET stack**: **react-day-picker** (Tailwind-friendly) or **Radix UI** primitives.

### 2.14 · Multi-step forms (wizards)

Long forms (> 8 fields) benefit from steps:
1. Stepper component at top (progress visualization)
2. One section per step (3-7 fields each)
3. Back / Next at bottom
4. Save-as-draft option (autosave to localStorage + server)
5. Confirm step at the end with editable summary

**State management**: `react-hook-form` + Zod schema per step + Zustand for cross-step state.

### 2.15 · Inline editing vs modal editing

Three patterns:
- **Inline cell edit** (table cells) — click to edit, save on blur
- **Inline form edit** (settings) — click "Edit" → form replaces text, Save/Cancel buttons
- **Modal edit** — open dialog with the form, Save closes

**OET rule**: inline for simple text/number, modal for forms ≥ 3 fields or destructive impact.

### 2.16 · Autosave indicators

```tsx
<div className="flex items-center gap-2 text-xs text-muted">
  {autosaving ? (
    <><Loader className="h-3 w-3 animate-spin" /> Saving...</>
  ) : (
    <><Check className="h-3 w-3 text-success" /> Saved {formatRelative(lastSaved)}</>
  )}
</div>
```

Debounce 1500ms after last input. Show "Saving..." briefly, then "Saved" with timestamp.

### 2.17 · Form submission states

```
Idle → Submitting (button loading, fields disabled)
     → Success (toast + redirect OR success state on form)
     → Error (toast + error summary at top of form + per-field errors)
```

On error: scroll to first error, focus first invalid field.

### 2.18 · Accessibility checklist

- [ ] Every input has a `<label>` (visible or sr-only)
- [ ] Required fields are marked + use `required` attribute
- [ ] Error messages use `aria-invalid` + `aria-describedby`
- [ ] Submit button focusable via keyboard
- [ ] Form submits on Enter in any field
- [ ] Tab order matches visual order
- [ ] Focus moves to first error on submit failure
- [ ] Loading state announces via `aria-busy`
- [ ] Success message uses `role="status"` live region

### 2.19 · Form recovery (unsaved changes warning)

```tsx
useBeforeUnload(form.formState.isDirty, 'You have unsaved changes. Leave anyway?');
```

For modal forms: AlertDialog "Discard changes?" on close attempt when dirty.

### 2.20 · Performance — uncontrolled forms

For large forms (> 20 fields), use **react-hook-form** with uncontrolled inputs to avoid re-rendering the entire form on every keystroke. Combined with Zod for schema validation.

```tsx
const form = useForm({ resolver: zodResolver(schema), mode: 'onTouched' });
```

---

## 3 · OET FORM COMPONENT API (production-ready)

```tsx
// components/admin/form/field.tsx
type FieldProps = {
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  optional?: boolean;
  children: React.ReactNode;
};

// components/admin/form/input.tsx
type InputProps = {
  label: string;
  hint?: string;
  error?: string;
  required?: boolean;
  loading?: boolean;
  startIcon?: React.ReactNode;
  endIcon?: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
} & React.InputHTMLAttributes<HTMLInputElement>;

// components/admin/form/select.tsx (uses Radix Select)
type SelectProps = {
  label: string;
  options: Array<{ value: string; label: string; icon?: React.ReactNode; disabled?: boolean }>;
  // … all of FieldProps
};

// components/admin/form/combobox.tsx (uses cmdk)
type ComboboxProps = SelectProps & {
  searchable?: boolean;
  multiple?: boolean;
};

// components/admin/form/checkbox.tsx (uses Radix Checkbox)
type CheckboxProps = {
  label: string;
  description?: string;
  indeterminate?: boolean;
  // …
};

// components/admin/form/switch.tsx (uses Radix Switch)
// components/admin/form/radio-group.tsx (uses Radix RadioGroup)
// components/admin/form/textarea.tsx
// components/admin/form/date-picker.tsx (uses react-day-picker)
// components/admin/form/file-upload.tsx (uses react-dropzone)
// components/admin/form/form.tsx (wraps react-hook-form FormProvider)
```

## 4 · QA checklist

- [ ] Every input has all 8 states (default, hover, focus-visible, active, disabled, loading, error, success)
- [ ] Focus ring matches OET primary (not Bootstrap blue)
- [ ] Required fields marked with one consistent pattern
- [ ] Errors have icon + text + ARIA wiring
- [ ] Validation mode = onTouched
- [ ] Submit button shows loading state, prevents double-click
- [ ] Touch targets ≥ 44px on mobile
- [ ] Autofill background overridden in dark mode
- [ ] Form recoverable on accidental navigation (dirty-state warning)
- [ ] Inline cell-edit + modal-edit patterns codified
- [ ] All form widgets have keyboard navigation
- [ ] Floating-label used only for auth, not admin forms

**Confidence upgrade**: LOW → **HIGH** ✅
