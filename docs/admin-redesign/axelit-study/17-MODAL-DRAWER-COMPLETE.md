# 17 — MODAL / DRAWER / DIALOG: Complete spec + May 2026 industry guidelines

**Gap closed**: Modal variants, off-canvas drawers, alert/confirm dialogs, sweet-alert
**Method**: Welcome-modal capture from `/dashboard/project` + customizer-drawer DOM probe + Bootstrap 5 modal/offcanvas inheritance + 2026 dialog UX synthesis.
**Confidence**: **HIGH** ✅

---

## 1 · What Axelit ships

### Modal
- Standard Bootstrap 5 `.modal` (`.modal-dialog > .modal-content`)
- Welcome modal observed on page load — anti-pattern (fires every refresh)
- Sweet Alert integration via `/advance-ui/sweet-alert` (separate plugin)

### Off-canvas drawer
- Right-side drawer for customizer (`.offcanvas .offcanvas-end`)
- Listed routes: `/advance-ui/offcanvas_toggle`
- Used for: settings, filters, detail preview

### Variants implied from sidebar
- `/advance-ui/modals` — modal gallery (basic, centered, scrolling, fullscreen, large, small)
- `/advance-ui/sweet-alert` — alert/confirm/prompt skinned dialogs
- `/advance-ui/offcanvas_toggle` — drawer variants

### Bootstrap modal CSS extracted (from rule scan)

```css
.modal {
  position: fixed;
  inset: 0;
  z-index: 1055;
  display: none;  /* JS sets to block + .show class */
  overflow: hidden;
  outline: 0;
}

.modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 1050;
  background-color: rgba(0, 0, 0, 0.5);
}

.modal-backdrop.show { opacity: 0.5; }

.modal-dialog {
  position: relative;
  width: auto;
  margin: 0.5rem;
  pointer-events: none;
}

@media (min-width: 576px) {
  .modal-dialog { max-width: 500px; margin: 1.75rem auto; }
}

.modal-content {
  position: relative;
  display: flex;
  flex-direction: column;
  background-color: var(--bs-body-bg);
  border: 1px solid var(--bs-border-color-translucent);
  border-radius: var(--bs-border-radius);  /* 28.8px in Axelit — large */
  outline: 0;
  pointer-events: auto;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  padding: 1rem;
  border-bottom: 1px solid var(--bs-border-color);
}

.modal-body { padding: 1rem; flex: 1 1 auto; overflow-y: auto; }
.modal-footer { padding: 0.75rem; border-top: 1px solid var(--bs-border-color); gap: 0.5rem; }

/* Sizes */
.modal-sm { max-width: 300px; }
.modal-lg { max-width: 800px; }
.modal-xl { max-width: 1140px; }

/* Centered */
.modal-dialog-centered {
  display: flex;
  align-items: center;
  min-height: calc(100% - 1rem);
}

/* Scrolling */
.modal-dialog-scrollable .modal-content { max-height: 100%; overflow: hidden; }
.modal-dialog-scrollable .modal-body { overflow-y: auto; }

/* Fullscreen */
.modal-fullscreen {
  width: 100vw; max-width: none; height: 100%; margin: 0;
}
.modal-fullscreen .modal-content { height: 100%; border: 0; border-radius: 0; }
```

### Bootstrap off-canvas CSS

```css
.offcanvas {
  position: fixed;
  z-index: 1045;
  display: flex;
  flex-direction: column;
  visibility: hidden;
  background-color: var(--bs-body-bg);
  transition: transform 0.3s ease-in-out;
}

.offcanvas-end {
  top: 0; right: 0;
  width: 400px;
  border-left: 1px solid var(--bs-border-color);
  transform: translateX(100%);
}

.offcanvas-start { /* mirror of -end */ }
.offcanvas-top, .offcanvas-bottom { /* horizontal slabs */ }

.offcanvas.show { transform: none; visibility: visible; }
```

---

## 2 · MAY 2026 INDUSTRY STANDARD — Dialog Design Guidelines

### 2.1 · The four dialog families

| Family | Use | Modal? | Library 2026 |
| ------ | --- | ------ | ------------ |
| **Modal** (popup, centered) | Multi-field forms, focused tasks | Yes (blocking) | **Radix Dialog** |
| **Drawer** (off-canvas, edge) | Settings, filters, detail preview, secondary nav | Optional | **Vaul** (mobile-first sheets) or Radix Dialog with custom positioning |
| **Alert Dialog** (confirm) | Destructive action confirmation | Yes (blocking, requires user action) | **Radix AlertDialog** |
| **Popover** (anchored) | Tooltips, menus, lightweight inline | No (non-modal) | **Radix Popover** |

**OET stack**: **Radix UI primitives** for all four. Drop SweetAlert (legacy, jQuery).

### 2.2 · Modal anatomy (universal)

```
┌─────────────────────────────────────────┐
│ ← scrim (overlay, 50% black)           │
│                                         │
│   ┌─────────────────────────────────┐   │
│   │ Title                       ✕  │   │ ← header (close button right)
│   ├─────────────────────────────────┤   │
│   │                                 │   │
│   │ Body content                    │   │ ← body (scrollable if needed)
│   │                                 │   │
│   ├─────────────────────────────────┤   │
│   │              [Cancel]  [Save]   │   │ ← footer (actions right)
│   └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

### 2.3 · Modal size matrix

| Size | Width | Use |
| ---- | ----- | --- |
| `sm` | 320px | Confirms, simple alerts |
| `md` | 480-560px | Standard forms (1-5 fields) |
| `lg` | 720-800px | Complex forms, tables, multi-section |
| `xl` | 1024-1140px | Dashboards-in-modal (rare) |
| `fullscreen` | 100vw × 100vh | Mobile, multi-step wizards |

**OET default**: `md` (560px) unless content requires otherwise. On mobile (< 640px), all modals go full-screen.

### 2.4 · Drawer size matrix

| Size | Width | Use |
| ---- | ----- | --- |
| `sm` | 320px | Filter panel, quick settings |
| `md` | 400-480px | Detail preview, mini-form |
| `lg` | 560-640px | Full form, multi-section |
| `xl` | 720-800px | Item editor, settings panel |

Mobile: drawer takes 90vw with backdrop visible at the edge (`Vaul` pattern).

### 2.5 · When to use modal vs drawer vs popover

```
Is the action blocking AND requires confirmation?  → AlertDialog (modal)
Is the user editing/creating an item?              → Modal (md)
Is the user filtering/configuring secondary?       → Drawer (right side)
Is the user navigating secondary content?          → Drawer (right side)
Is the user picking from a small list?             → Popover (Combobox/Menu)
Is the action transient and informational?         → Toast (Sonner)
Is the user reading help text?                     → Tooltip (hover)
```

### 2.6 · Animation spec

**Modal entrance**:
- Scrim: fade in 200ms ease-out
- Dialog: fade + scale (0.95 → 1) + slide up (8px) 200ms cubic-bezier(0.16, 1, 0.3, 1)

**Modal exit**:
- Reverse, 150ms ease-in

**Drawer entrance**:
- Scrim: fade in 200ms ease-out
- Panel: slide in from edge, 300ms cubic-bezier(0.32, 0.72, 0, 1) — iOS sheet feel

**Drawer exit**:
- Reverse, 250ms ease-in

**`prefers-reduced-motion`**: hard cut, no animation

### 2.7 · Focus trap (CRITICAL — Radix handles this automatically)

When dialog opens:
1. Move focus to first focusable element inside (or `autoFocus` marked element)
2. Trap Tab/Shift+Tab within dialog — wrap from last to first
3. Save the previously-focused element

When dialog closes:
4. Restore focus to the element that opened the dialog

**Never skip this** — keyboard users get stuck behind the modal otherwise.

### 2.8 · Escape + scrim-click closing

| Dialog type | Esc closes? | Click scrim closes? |
| ----------- | :---------: | :-----------------: |
| Modal (form) | ✓ (warn if dirty) | ✓ (warn if dirty) |
| AlertDialog (confirm) | ✓ (treats as cancel) | ✗ (must click button) |
| Drawer | ✓ | ✓ |
| Popover | ✓ | ✓ |
| Fullscreen modal | ✓ | (no scrim) |

For dirty forms: intercept close and show AlertDialog "Discard changes?".

### 2.9 · Scrolling behavior

- **Body scroll lock** when dialog opens (preserve scroll position)
  - Radix UI handles this via Floating UI
  - Add padding-right equal to scrollbar width (avoids layout shift)
- **Modal body internal scroll** if content > viewport
  - `max-height: calc(100vh - 200px)` + `overflow-y: auto`
  - Header + footer remain sticky

### 2.10 · Accessibility checklist

- [ ] `role="dialog"` (or `role="alertdialog"` for confirms)
- [ ] `aria-modal="true"`
- [ ] `aria-labelledby` points at title id
- [ ] `aria-describedby` points at description id (optional)
- [ ] Focus trap (Radix automatic)
- [ ] Esc to close (where applicable)
- [ ] Initial focus moves to first interactive element
- [ ] Close button has `aria-label="Close"`
- [ ] Scrim is `aria-hidden="true"`
- [ ] Background content has `aria-hidden="true"` when modal open

### 2.11 · AlertDialog (confirm) anatomy

```tsx
<AlertDialog>
  <AlertDialogTrigger asChild>
    <Button variant="destructive">Delete</Button>
  </AlertDialogTrigger>
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>Delete "{itemName}"?</AlertDialogTitle>
      <AlertDialogDescription>
        This will permanently remove the item and all its associated data. This action cannot be undone.
      </AlertDialogDescription>
    </AlertDialogHeader>
    <AlertDialogFooter>
      <AlertDialogCancel>Cancel</AlertDialogCancel>
      <AlertDialogAction
        onClick={handleDelete}
        className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
      >
        Yes, delete {itemName}
      </AlertDialogAction>
    </AlertDialogFooter>
  </AlertDialogContent>
</AlertDialog>
```

**Confirm copy rules**:
- Title is a question or imperative ("Delete this user?" not "Confirm delete")
- Description explains consequence + reversibility ("cannot be undone")
- Confirm button explicitly names the action ("Yes, delete user" not "OK")
- Destructive confirm button = destructive variant (red)
- Cancel is the default focus

### 2.12 · Mobile sheet (drawer alternative on mobile)

For mobile, drawers should slide from the **bottom** as iOS-style sheets:

```tsx
<Drawer>
  <DrawerTrigger>Open</DrawerTrigger>
  <DrawerContent>
    {/* drag handle at top */}
    <DrawerHeader>
      <DrawerTitle>Filter products</DrawerTitle>
    </DrawerHeader>
    <DrawerBody>...</DrawerBody>
    <DrawerFooter>
      <Button>Apply</Button>
    </DrawerFooter>
  </DrawerContent>
</Drawer>
```

**Vaul library** handles drag-to-dismiss, momentum, and snap points. Use it.

### 2.13 · Toast notifications (transient feedback)

For non-blocking feedback after user action:

```tsx
toast.success('Item saved', { description: 'Your changes are live.' });
toast.error('Failed to save', { description: 'Network error — please retry.', action: { label: 'Retry', onClick: retry } });
toast.loading('Uploading...');
toast.promise(uploadFile(), {
  loading: 'Uploading…',
  success: 'Upload complete!',
  error: 'Upload failed',
});
```

**Sonner** is the 2026 React standard. Placement: top-right (desktop), bottom-center (mobile). Auto-dismiss: 5s for success, 8s for error (or sticky until clicked).

### 2.14 · Promise-based dialogs (modern pattern)

```ts
const confirmed = await confirm({
  title: 'Delete user?',
  description: 'This cannot be undone.',
  confirmText: 'Yes, delete',
  variant: 'destructive',
});
if (confirmed) await deleteUser();
```

Saves nested setState gymnastics. Implement via a global confirm() function that returns a Promise.

### 2.15 · Stack management (modal-from-modal)

Generally avoid. If unavoidable:
- New modal goes ABOVE existing one (z-index stacks)
- Old modal's focus trap pauses
- Closing top modal returns focus to second modal

Better pattern: **multi-step modal** (one modal, internal step state) instead of stacked modals.

### 2.16 · Anti-patterns to avoid

- **Welcome modal on every page load** ← Axelit ships this, OET must NOT
- **Modal-as-page** (huge modal containing entire workflow) — use a route instead
- **Nested modals** (avoid; use multi-step)
- **Disable close while loading** (always allow Cancel; show "Are you sure?" if action is in flight)
- **Custom scrim color/opacity per modal** — keep consistent
- **Auto-dismiss timer on modal** (never; user controls dismiss)
- **Cookie banner as modal** (use a slim banner; modal is too aggressive)

### 2.17 · Performance

- Lazy-load modal contents (`<Suspense>` inside modal body)
- Render dialog on demand (don't keep all dialogs in DOM)
- Radix uses Portal — renders dialog at `document.body`, not in parent tree

### 2.18 · Specialty dialog patterns

#### Command palette (Cmd+K)
Floating modal at top-third of viewport, ~640px wide, with search input + command list. **`cmdk`** library is the standard.

#### Lightbox (image viewer)
Full-screen modal with prev/next arrows + close. **`yet-another-react-lightbox`** for galleries.

#### Slide-over (alternate term for right drawer)
Same as drawer-right, narrower (320-400px), with persistent close button.

---

## 3 · OET DIALOG COMPONENT API (production-ready)

```tsx
// components/admin/dialog/dialog.tsx — wraps Radix Dialog
type DialogProps = {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  size?: 'sm' | 'md' | 'lg' | 'xl' | 'fullscreen';
  closeOnOverlayClick?: boolean;
  closeOnEsc?: boolean;
  trapFocus?: boolean;  // default true
  children: React.ReactNode;
};

// components/admin/dialog/alert-dialog.tsx — wraps Radix AlertDialog
type AlertDialogProps = {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  title: string;
  description: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'default' | 'destructive';
  onConfirm: () => void | Promise<void>;
};

// components/admin/dialog/drawer.tsx — wraps Vaul (mobile) + Radix Dialog (desktop)
type DrawerProps = {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  side?: 'left' | 'right' | 'top' | 'bottom';
  size?: 'sm' | 'md' | 'lg' | 'xl';
  closeOnOverlayClick?: boolean;
  children: React.ReactNode;
};

// hooks/use-confirm.ts — promise-based confirm
export async function confirm(opts: {
  title: string;
  description?: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'default' | 'destructive';
}): Promise<boolean> { ... }

// Usage: const yes = await confirm({ title: 'Delete?', variant: 'destructive' });
```

## 4 · QA checklist

- [ ] Every dialog uses Radix primitives (focus trap, ARIA, portal — all automatic)
- [ ] Sizes follow the matrix (sm/md/lg/xl/fullscreen)
- [ ] AlertDialog used for destructive confirms (not regular Dialog)
- [ ] Confirm copy follows the 3-rule pattern (question title, consequence description, explicit action label)
- [ ] Esc closes (with dirty-form warning where applicable)
- [ ] Scrim click closes (or warns if dirty)
- [ ] Initial focus moves into dialog
- [ ] Focus returns to trigger on close
- [ ] Body scroll locked while open
- [ ] No nested modals
- [ ] No welcome-modal-on-page-load anti-pattern
- [ ] Mobile drawers use Vaul bottom-sheet
- [ ] `prefers-reduced-motion` disables transitions

**Confidence upgrade**: LOW → **HIGH** ✅
