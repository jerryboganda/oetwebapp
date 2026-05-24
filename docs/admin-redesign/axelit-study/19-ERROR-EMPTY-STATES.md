# 19 — ERROR PAGES + EMPTY STATES: Complete spec + May 2026 industry guidelines

**Gap closed**: 404, 500, maintenance, offline, permission-denied + universal empty-state pattern library
**Method**: Captured 404 page during crawl (visible at non-existent routes — generic copy "Website owners should regularly check for and fix broken links"). Spec synthesized from 2026 industry consensus + Axelit's design tokens.
**Confidence**: **HIGH** ✅

---

## 1 · What Axelit ships

- **404 page**: visible at non-existent routes (e.g. `/ui-kit/forms-elements` returns 404)
- **Error Pages sidebar group**: lists "404", "500", possibly "503"
- **Other Pages**: maintenance, coming soon, lock screen variants
- **Empty states**: NOT present in demo data (every page is populated)

Axelit's error pages follow the template-marketing illustration-heavy pattern. OET admin should be more functional + helpful.

---

## 2 · MAY 2026 INDUSTRY STANDARD — Error + Empty State Guidelines

### 2.1 · The complete error/empty state taxonomy

| Type | When triggered | Recovery action |
| ---- | -------------- | --------------- |
| **404 — Not Found** | URL doesn't match any route | Suggest similar pages / link to home |
| **403 — Forbidden** | Authenticated but not authorized | Show requesting access UI |
| **401 — Unauthorized** | Not authenticated | Redirect to sign-in |
| **500 — Server Error** | Backend crashed | Apologize, log, surface retry, link to status page |
| **503 — Maintenance** | Planned downtime | Show ETA, status page link |
| **Offline** | No network | Cached state if available, retry button |
| **Empty state (first-time)** | User has zero items | Onboarding CTA: "Create your first X" |
| **Empty state (filtered)** | Filter/search returns 0 | "No matches" + clear filters action |
| **Empty state (loading)** | Data not yet fetched | Skeleton, not error |
| **Empty state (error)** | Fetch failed | ⚠ Retry + error details |

### 2.2 · Universal anatomy

```
┌──────────────────────────────────────┐
│                                      │
│           [Illustration]             │  ← visual focal point (40-200px)
│                                      │
│         Title (H2 or H3)             │  ← what happened
│                                      │
│         Subtitle / description       │  ← why + context
│         (1-3 lines, muted)           │
│                                      │
│         [Primary CTA]                │  ← recovery action
│         [Secondary action]           │  ← alternate path
│                                      │
│         Help link / support email    │  ← escape hatch
│                                      │
└──────────────────────────────────────┘
```

### 2.3 · 404 — Not Found

```tsx
<EmptyState
  illustration={<NotFoundIllustration />}
  title="Page not found"
  description="The page you're looking for doesn't exist or has been moved."
  primaryAction={{ label: 'Back to dashboard', href: '/admin' }}
  secondaryAction={{ label: 'Search admin', onClick: openCommandPalette }}
  supportLink={{ label: 'Report a broken link', href: 'mailto:support@oet-prep.com' }}
/>
```

**Copy rules**:
- Title: "Page not found" (NOT "404", NOT "Oops!")
- Description: factual, brief
- Primary CTA: takes user somewhere productive (home/dashboard)
- Secondary CTA: helpful alternative (search, recent pages)
- No exclamation marks, no humor for admin (humor OK on consumer 404s)

Show the requested URL for debugging:
```tsx
<code className="text-xs text-muted mt-4 block">{pathname}</code>
```

### 2.4 · 403 — Forbidden

For authenticated users hitting unauthorized routes:

```tsx
<EmptyState
  illustration={<LockIllustration />}
  title="You don't have access to this page"
  description="This area requires the {role} permission. Ask your admin to grant access, or switch to a different account."
  primaryAction={{ label: 'Request access', onClick: openRequestAccessDialog }}
  secondaryAction={{ label: 'Back to my dashboard', href: '/admin' }}
  supportLink={{ label: 'Contact your admin', href: 'mailto:admin@oet-prep.com' }}
/>
```

OET admin already has `AdminPermissionDenied` component in `app/admin/layout.tsx` — align with this pattern.

### 2.5 · 500 — Server Error

```tsx
<EmptyState
  illustration={<ServerErrorIllustration />}
  title="Something went wrong"
  description="We've been notified and are working on it. You can try again or come back in a few minutes."
  primaryAction={{ label: 'Try again', onClick: reload }}
  secondaryAction={{ label: 'Check status page', href: 'https://status.oet-prep.com' }}
  errorDetails={
    isDev && (
      <details className="mt-6 text-xs text-muted">
        <summary>Error details (visible in development)</summary>
        <pre className="mt-2 p-3 bg-tertiary rounded overflow-x-auto">{error.stack}</pre>
      </details>
    )
  }
/>
```

**Rules**:
- Apology baked into copy (don't dodge responsibility)
- Surface the retry option prominently
- Provide a status page link
- Hide stack traces in production (security)
- Auto-report to Sentry/error tracker on mount

### 2.6 · 503 — Maintenance

```tsx
<EmptyState
  illustration={<MaintenanceIllustration />}
  title="We'll be right back"
  description="OET Admin is undergoing scheduled maintenance. We'll be back online by ~{eta}."
  primaryAction={{ label: 'Status updates', href: 'https://status.oet-prep.com' }}
/>
```

Optionally: show countdown timer to ETA.

### 2.7 · Offline state

Detect via `navigator.onLine` + `online`/`offline` events:

```tsx
useEffect(() => {
  const handleOnline = () => toast.success('Back online');
  const handleOffline = () => toast.error('You\'re offline', { duration: Infinity });
  window.addEventListener('online', handleOnline);
  window.addEventListener('offline', handleOffline);
  return () => {
    window.removeEventListener('online', handleOnline);
    window.removeEventListener('offline', handleOffline);
  };
}, []);
```

For data-dependent pages while offline:
```tsx
<EmptyState
  illustration={<OfflineIllustration />}
  title="You're offline"
  description="Some features need an internet connection. Reconnect to continue."
  primaryAction={{ label: 'Retry', onClick: refetch }}
/>
```

If using a service worker with caching: show cached data + a banner "Showing cached data — last updated {timestamp}".

### 2.8 · Empty states — the 4 flavors

#### Flavor 1: First-time (zero items, never created)
```tsx
<EmptyState
  illustration={<FirstTimeIllustration />}
  title="No reading papers yet"
  description="Create your first reading paper to start authoring questions for learners."
  primaryAction={{ label: 'Create reading paper', href: '/admin/content/reading/new' }}
  secondaryAction={{ label: 'Import from CSV', href: '/admin/content/import' }}
  helpLink={{ label: 'How to author reading papers', href: '/docs/reading-authoring' }}
/>
```

**Copy rules**:
- Title: "No X yet" — positive framing
- Description: orient the user — explain what this page is for, why creating one matters
- Primary CTA: the most direct path to filling the empty state
- Optional secondary: alternate path (import, bulk add, learn more)

#### Flavor 2: Filtered (had items, filter removed all)
```tsx
<EmptyState
  size="sm"
  illustration={<SearchIllustration />}
  title="No matching results"
  description="Try adjusting your filters or search terms."
  primaryAction={{ label: 'Clear filters', onClick: clearFilters }}
/>
```

**Difference from first-time**: smaller, simpler. The user knows the context — just show the way out.

#### Flavor 3: Loading (data not yet fetched)
```tsx
<Skeleton rows={5} variant="table" />
```

NOT an empty state — a skeleton. Different component. Use shadcn `<Skeleton>` or framer-motion shimmer.

#### Flavor 4: Error (fetch failed)
```tsx
<EmptyState
  size="md"
  variant="error"
  illustration={<ErrorIllustration />}
  title="Couldn't load reading papers"
  description={error.message ?? 'Please try again.'}
  primaryAction={{ label: 'Retry', onClick: refetch }}
  secondaryAction={{ label: 'Report this issue', href: 'mailto:support@oet-prep.com' }}
/>
```

### 2.9 · Illustration strategy

Three approaches:

| Strategy | Pros | Cons |
| -------- | ---- | ---- |
| **Custom illustrations** (per state, branded) | On-brand, polished | Expensive to produce |
| **Icon-only** (large lucide icon, 64-120px) | Cheap, consistent | Less personality |
| **Spot illustrations** from a kit (unDraw, Storyset) | Free, plentiful | Generic feel |

**OET recommendation**: Icon-only for admin (clinical, consistent). Reserve custom illustrations for marketing/auth pages.

Icon spec:
```tsx
<div className="rounded-full bg-primary/10 p-4 inline-flex">
  <FileSearch className="h-12 w-12 text-primary" strokeWidth={1.5} />
</div>
```

Wrap the icon in a tinted circle for visual weight.

### 2.10 · Sizing

| Size | Container | Use |
| ---- | --------- | --- |
| `sm` | 200-300px wide | Inline (inside table card, dropdown) |
| `md` | 400-500px wide | Section-level (inside a page card) |
| `lg` | 600-800px wide, full viewport | Page-level (404, 500, maintenance) |

### 2.11 · Animation

- Illustration: fade-in + slight scale (0.95 → 1) on mount, 250ms ease-out
- Text: stagger fade-in (50ms per line)
- Respect `prefers-reduced-motion`

### 2.12 · Accessibility

- Container: `role="status"` (announces to screen reader on mount)
- Heading: proper hierarchy (h2 in page-level, h3 in section-level)
- Illustration: `aria-hidden="true"` (decorative)
- Buttons: explicit labels (not just "Retry" — "Retry loading reading papers")

### 2.13 · Onboarding-style empty state (extended)

When the empty state is also onboarding:

```tsx
<EmptyState
  variant="onboarding"
  illustration={<WelcomeIllustration />}
  title="Welcome to Content Hub"
  description="Author papers, manage media, and publish learning content."
  primaryAction={{ label: 'Take a tour', onClick: startTour }}
  steps={[
    { icon: <FileText />, title: 'Create papers', description: 'Author reading, listening, writing content' },
    { icon: <Upload />, title: 'Manage media', description: 'Upload audio, images, PDFs' },
    { icon: <Send />, title: 'Publish', description: 'Push to learners with one click' },
  ]}
/>
```

3-step "what you can do here" preview makes the empty state instructive.

### 2.14 · Error boundary (React 19)

```tsx
// app/admin/error.tsx (Next.js App Router error boundary)
'use client';

import { useEffect } from 'react';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AlertTriangle } from 'lucide-react';

export default function AdminError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => {
    // Sentry / error tracker
    captureException(error);
  }, [error]);

  return (
    <div className="min-h-[60vh] flex items-center justify-center p-6">
      <EmptyState
        variant="error"
        illustration={<AlertTriangle className="h-12 w-12 text-destructive" />}
        title="Something went wrong in the admin"
        description="We've logged the error. You can try again or return to the dashboard."
        primaryAction={{ label: 'Try again', onClick: reset }}
        secondaryAction={{ label: 'Back to dashboard', href: '/admin' }}
        errorRef={error.digest}  // show error ID for support reference
      />
    </div>
  );
}
```

### 2.15 · Not-found (Next.js App Router)

```tsx
// app/admin/not-found.tsx
import Link from 'next/link';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { FileQuestion } from 'lucide-react';

export default function AdminNotFound() {
  return (
    <div className="min-h-[60vh] flex items-center justify-center p-6">
      <EmptyState
        illustration={<FileQuestion className="h-12 w-12 text-primary" />}
        title="Admin page not found"
        description="The page you're looking for doesn't exist."
        primaryAction={{ label: 'Back to dashboard', href: '/admin' }}
      />
    </div>
  );
}
```

### 2.16 · Anti-patterns

- **"Oops! Something went wrong"** (cliche, infantilizing)
- **Sad face / surprised face emoji** (unprofessional for admin)
- **No recovery action** (always provide a way out)
- **Tech jargon in user-facing copy** ("HTTP 503 ECONNREFUSED")
- **Generic stock illustration of a person scratching head** (overused)
- **Loading spinner shown for > 1s without a skeleton** (use skeleton instead)
- **Empty state that LIES** ("Welcome!" when actually it's a permission error)
- **Auto-redirect on 404** (let user decide where to go)

### 2.17 · Empty state content vs CTA — the ratio

In a populated table, empty rows take ~50px each. In an empty state, the entire card area is occupied. Don't:
- Show 5 lines of marketing copy in an empty state
- Show 4 CTAs (decision paralysis — max 2 actions: 1 primary, 1 secondary)
- Hide the empty state behind a tooltip (always visible)

Do:
- Keep description under 30 words
- One primary CTA, one secondary
- Add a help link as the escape hatch

### 2.18 · Empty state inside tables

When a table loads but has 0 rows:

```tsx
<table>
  <thead>
    <tr><th>Name</th><th>Status</th><th>Created</th></tr>
  </thead>
  <tbody>
    {data.length === 0 ? (
      <tr>
        <td colSpan={3} className="p-12 text-center">
          <EmptyState size="sm" {...emptyProps} />
        </td>
      </tr>
    ) : (
      data.map(row => <TableRow key={row.id} {...row} />)
    )}
  </tbody>
</table>
```

### 2.19 · Empty state inside cards

```tsx
<Card>
  <CardHeader title="Recent Activity" />
  <CardBody className="min-h-[200px] flex items-center justify-center">
    {data.length === 0
      ? <EmptyState size="sm" title="No activity yet" />
      : <ActivityList items={data} />}
  </CardBody>
</Card>
```

### 2.20 · Loading skeletons (the partner pattern)

Match skeleton to the eventual content shape:
```tsx
{loading ? (
  <div className="space-y-3">
    {Array.from({ length: 5 }).map((_, i) => (
      <div key={i} className="flex items-center gap-3">
        <Skeleton className="h-10 w-10 rounded-full" />  {/* avatar */}
        <div className="flex-1">
          <Skeleton className="h-4 w-3/4" />              {/* name */}
          <Skeleton className="h-3 w-1/2 mt-2" />         {/* metadata */}
        </div>
      </div>
    ))}
  </div>
) : (
  <RealList items={data} />
)}
```

---

## 3 · OET ERROR / EMPTY STATE COMPONENT API

```tsx
// components/admin/ui/empty-state.tsx
type EmptyStateProps = {
  variant?: 'default' | 'error' | 'onboarding';
  size?: 'sm' | 'md' | 'lg';
  illustration?: React.ReactNode;  // icon or illustration
  title: string;
  description?: string;
  primaryAction?: { label: string; href?: string; onClick?: () => void };
  secondaryAction?: { label: string; href?: string; onClick?: () => void };
  supportLink?: { label: string; href: string };
  helpLink?: { label: string; href: string };
  errorRef?: string;                // error tracking reference id
  steps?: Array<{ icon: React.ReactNode; title: string; description: string }>;  // onboarding only
  className?: string;
};
```

```tsx
// app/admin/error.tsx   — error boundary
// app/admin/not-found.tsx — 404 boundary
// app/admin/loading.tsx  — loading boundary

// app/global-error.tsx   — root error boundary (for layout crashes)
```

## 4 · QA checklist

- [ ] 404, 403, 500, 503 boundary components exist
- [ ] Every list/table has an empty state for zero items
- [ ] Every filterable list has a "no matching results" state
- [ ] Loading uses skeleton, NOT spinner-only
- [ ] Error states surface Retry + Report-issue actions
- [ ] Error boundary captures to Sentry on mount
- [ ] Stack traces hidden in production
- [ ] Empty state copy uses positive framing ("No X yet" not "You have no X")
- [ ] Illustrations consistent (icon-in-tinted-circle pattern)
- [ ] Max 1 primary + 1 secondary CTA
- [ ] Mobile responsive (sm size used on mobile)
- [ ] WCAG: role="status", aria-hidden on illustrations
- [ ] Offline state detected and surfaced via toast
- [ ] Session-expired state redirects gracefully to sign-in

**Confidence upgrade**: NOT CAPTURED → **HIGH** ✅
