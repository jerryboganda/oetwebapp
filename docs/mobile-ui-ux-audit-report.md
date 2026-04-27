# Mobile UI/UX Audit Report — Learner Dashboard (A-Z)

> **Date:** 2026-04-16  
> **Device:** iPhone 14 Pro (393×852)  
> **Production URL:** `app.oetwithdrhesham.co.uk`  
> **User:** `mindreader420123@gmail.com` (Premium learner)  
> **Modes Tested:** Light mode + Dark mode  
> **Pages Audited:** 25+ pages / 30+ screenshots

---

## Executive Summary

| Severity | Count |
| ---------- | ------- |
| 🔴 Critical | 3 |
| 🟠 Major | 5 |
| 🟡 Minor | 7 |
| 💡 Enhancement | 4 |
| **Total** | **19** |

The platform's learner dashboard is largely functional and visually cohesive in mobile viewport. The design system (Manrope font, violet accent, cream canvas, rounded cards) is consistently applied across most pages. **Dark mode is well-implemented** with proper surface colours and contrast.

However, there are **3 critical issues** that must be fixed before public launch:

1. **Developer/internal copy is visible to end-users** across 5+ pages
2. **Four lesson sub-routes (grammar, vocabulary, strategies, pronunciation) return errors** despite being linked in navigation — **pronunciation: RESOLVED** (see `/pronunciation` hub + drill recorder + listening-game routes and `/admin/content/pronunciation` CMS)
3. **The `/practice` route returns a 404** page

---

## 🔴 Critical Issues (Must Fix Before Launch)

### C-1: Developer / Internal Copy Exposed to Users

**Severity:** 🔴 Critical  
**Impact:** Unprofessional UX — users see internal design notes and developer instructions  
**Scope:** 5+ pages affected

| Page | File | Line(s) | Exposed Dev Text |
| ------ | ------ | --------- | ----------------- |
| AI Conversation | `app/conversation/page.tsx` | 104 | "Keep the launch cards aligned with the rest of the learner dashboard" |
| AI Conversation | `app/conversation/page.tsx` | 141 | "Keep the history items on soft surfaces so they match the dashboard language" |
| Video Lessons | `app/lessons/page.tsx` | 69 | "Use the same chip-and-card language as the dashboard instead of browser-style controls" |
| Video Lessons | `app/lessons/page.tsx` | 109 | "Each card keeps the same border, radius, and shadow language as the dashboard" |
| Settings Profile | `app/settings/[section]/page.tsx` | 197 | "What changes here" (section heading) |
| Settings Profile | `app/settings/[section]/page.tsx` | 198 | "Use this page to keep learner identity data accurate before the rest of the app depends on it." |
| Settings Profile | `app/settings/[section]/page.tsx` | 147 | Hero description: "Use this section to review profile controls with the outcome of each change kept explicit" |
| Settings Profile | `app/settings/[section]/page.tsx` | 146 | Hero title: "Keep profile settings clear before you change them" |

**Root Cause:** The `LearnerPageHero.description` prop and section helper texts contain implementation notes from design tokens that were never replaced with proper user-facing copy.

**Fix:** Replace all developer notes with proper user-facing descriptions:

- Conversation Session Builder: → "Choose a conversation type to begin practising"
- Conversation History: → "Review and continue your previous practice sessions"
- Lessons Filters: → "Filter lessons by exam type and sub-test"
- Settings Profile Hero: → "Your Profile" / "Review and update your personal details"
- Settings Helper: → "Personal Information" / "Your identity details used across the platform"

---

### C-2: Lesson Sub-Routes All Return "Video lesson not found"

**Severity:** 🔴 Critical  
**Impact:** Four navigation menu links lead to error pages — broken user journey  
**URLs Affected:**

- `/lessons/grammar` → "Video lesson not found"
- `/lessons/vocabulary` → "Video lesson not found"
- `/lessons/strategies` → "Video lesson not found"
- `/lessons/pronunciation` → "Video lesson not found"

**File:** `app/lessons/[id]/page.tsx` (line 69)

**Root Cause:** The hamburger menu links to `/lessons/grammar`, `/lessons/vocabulary`, etc. but the dynamic route `[id]` expects numeric IDs. These slug-based URLs don't resolve to any lesson content.

**Fix Options:**

1. **Remove dead nav links** — Hide Grammar, Vocabulary, Strategies, Pronunciation from hamburger menu until content exists
2. **Add proper routing** — Create a slug-to-ID mapping or change the dynamic route to support both IDs and slugs
3. **Add landing pages** — Create category landing pages at `/lessons/grammar`, `/lessons/vocabulary`, etc.

---

### C-3: `/practice` Route Returns 404

**Severity:** 🔴 Critical  
**Impact:** Dead route if ever linked to from any part of the app  
**URL:** `/practice`

**Root Cause:** The `app/practice/` directory has sub-routes (`interleaved/`, `quick-session/`) but no root `page.tsx`.

**Fix:** Either add a `page.tsx` landing page or ensure no navigation points to `/practice` directly.

---

## 🟠 Major Issues

### M-1: Text Truncation in Hero Stat Badges

**Severity:** 🟠 Major  
**Impact:** Key data values are cut off with "..." making them unreadable on mobile  
**File:** `components/domain/learner-surface.tsx` (line 186)

**Affected Pages & Truncated Values:**

| Page | Stat Value | Shows As |
| ------ | ----------- | ---------- |
| Settings | Dr Faisal Maqsood Anwar | "Dr Faisal M..." |
| Settings | `mindreader420123@gmail.com` | "mindreade..." |
| Billing | Premium Monthly | "Premium M..." |
| Speaking | Browse library | "Browse libr..." |
| Progress | 6 checkpoints | "6 checkpoi..." |
| Writing | Choose a task | "Choose a t..." |

**Root Cause:** The highlight value `<p>` uses Tailwind's `truncate` class (`overflow: hidden; text-overflow: ellipsis; white-space: nowrap`) within a fixed-width badge container.

**Fix:** Either:

1. Allow text wrapping for longer values (`whitespace-normal` instead of `truncate`)
2. Increase badge width on mobile
3. Use shorter stat values (e.g., "Premium" instead of "Premium Monthly")
4. Add a tooltip on tap for full text

---

### M-2: Diagnostic "Begin Diagnostic" Button Overlaps Bottom Navigation

**Severity:** 🟠 Major  
**Impact:** CTA button floats over content and potentially overlaps with bottom nav bar  
**File:** `app/diagnostic/page.tsx` (line ~220)

**Root Cause:** Button uses `sticky bottom-4 z-10` positioning which places it 16px from the bottom of the viewport, exactly where the mobile bottom navigation bar sits.

**Fix:** Add `pb-20` (80px) bottom padding to the diagnostic page's main scrollable content area, or use `bottom-20` instead of `bottom-4` to position above the nav bar.

---

### M-3: Reading "Featured Tasks" Permanently Shows "Loading..."

**Severity:** 🟠 Major  
**Impact:** Users see perpetual loading spinner for featured tasks even after page fully loads  
**File:** `app/reading/page.tsx` (line 111)

**Root Cause:** Hero highlight value defaults to `'Loading...'` when `tasks.length` is falsy. The tasks array may never be populated if the API returns empty or the data isn't fetched for this user.

**Fix:** Change fallback from `'Loading...'` to a proper empty state like `'None yet'` or `'0 available'`.

---

### M-4: SignalR Notifications Hub Consistently Fails (504)

**Severity:** 🟠 Major  
**Impact:** Real-time notifications don't work; every page load shows a failed hub connection  
**Evidence:** Every page navigation triggers `GET /api/backend/v1/notifications/hub` → `net::ERR_ABORTED` (504)

**Root Cause:** SignalR WebSocket hub on the backend is either not running or not accessible through the Nginx proxy configuration.

**Fix:** Verify SignalR hub is configured in `docker-compose.production.yml` and Nginx Proxy Manager allows WebSocket upgrade for the API domain.

---

### M-5: Analytics Events Consistently Fail

**Severity:** 🟠 Major  
**Impact:** No analytics data being collected from production users  
**Evidence:** Every page triggers `POST /api/backend/v1/analytics/events` → `net::ERR_ABORTED`

**Root Cause:** Analytics endpoint is not responding or is blocked.

**Fix:** Investigate the analytics endpoint status on the backend. Ensure the route exists and is properly configured.

---

## 🟡 Minor Issues

### m-1: Duplicate "Notifications" Heading in Panel

**Severity:** 🟡 Minor  
**Impact:** Redundant title text — "Notifications" appears as both the slide-out panel title and section heading  
**File:** `components/layout/notification-center.tsx` (line 235)

**Fix:** Remove the inner section heading since the panel title already provides context.

---

### m-2: CSP frame-ancestors Warning on Every Page

**Severity:** 🟡 Minor  
**Impact:** Console warnings on every page load — does not affect functionality  
**Evidence:** "The Content Security Policy directive 'frame-ancestors' is ignored when delivered via a `<meta>` element."

**Fix:** Move the `frame-ancestors` directive from the `<meta>` tag to the HTTP response header (`Content-Security-Policy` header in `next.config.ts` or Nginx).

---

### m-3: Developer Tags Visible on Settings Profile Fields

**Severity:** 🟡 Minor  
**Impact:** Internal classification tags (IDENTITY, ACCOUNT, SIGN-IN, STUDY CONTENT, ROUTE CONTEXT, DEVICE VISIBILITY, SESSION NOTE, SET) are shown as badges next to each field  
**File:** `app/settings/[section]/page.tsx` (lines 206-244)

**Fix:** Hide these internal classification tags from the rendered UI or style them as tooltips/help text only visible on interaction.

---

### m-4: "Session Visibility Label" Field on Profile Page

**Severity:** 🟡 Minor  
**Impact:** Exposes an internal/debug field ("Audit desktop session") that has no meaning to regular users  
**File:** `app/settings/[section]/page.tsx` (line ~243)

**Fix:** Hide this field from learner users; it should only be visible in admin/debug context.

---

### m-5: Bottom Nav Bar Overlap on Several Pages

**Severity:** 🟡 Minor  
**Impact:** The last few pixels of page content are hidden behind the fixed bottom navigation bar  
**Affected Pages:** Study Plan, Speaking drills, Achievements (bottom cards partially hidden)

**Fix:** Add consistent `pb-20` or `pb-24` bottom padding to all page main content containers to account for the fixed bottom nav height.

---

### m-6: Hamburger Menu Escalations Item Highlight Color Bleed

**Severity:** 🟡 Minor  
**Impact:** The Escalations menu item shows a pink/orange highlight that bleeds through the background in an unexpected way

**Fix:** Check the active/hover state styles for the Escalations menu item and ensure consistent styling with other items.

---

### m-7: Empty States Could Be More Engaging

**Severity:** 🟡 Minor  
**Impact:** Multiple pages show plain text empty states that could be more inviting  
**Examples:**

- Writing Workspace: "No tasks match your filters"
- Community: "Start by creating a new thread in any category"
- Achievements: All cards locked with no progress indication
- Escalations: "No escalations filed yet" with bare CTA

**Fix:** Add illustrations, encouraging copy, or guided first-action CTAs to empty states.

---

## 💡 Enhancement Suggestions

### E-1: Dark Mode Is Well-Implemented ✅

The dark mode implementation is solid:

- Proper dark surface colors (navy/dark blue cards on dark background)
- Violet accent remains visible and accessible
- Text contrast is good (white/light gray on dark)
- All interactive elements remain visible
- The Weakest Area alert card properly adjusts

Only minor note: The "Loading..." and some badge colors could use slightly more contrast in dark mode.

---

### E-2: Hero Section Descriptions Could Be More Concise

Many hero section descriptions are wordy and read like product specs rather than quick motivational copy. On mobile where space is premium, shorter descriptions would improve scannability.

**Current (verbose):** "Use the dashboard to decide the next action, check readiness evidence, and move without guesswork."  
**Suggested:** "Your daily practice hub — plan, track, and improve."

---

### E-3: Notification Badge Count Could Use Better Positioning

The notification bell badge (showing "8") works but is slightly tight against the avatar circle. A small margin adjustment would improve visual clarity.

---

### E-4: Add Pull-to-Refresh on Mobile

For a mobile-first experience, implementing pull-to-refresh on the dashboard and skill pages would feel more native.

---

## Pages Audited (Complete List)

| # | Page | Status | Mode |
| --- | ------ | -------- | ------ |
| 1 | Dashboard (hero) | ✅ Audited | Light + Dark |
| 2 | Dashboard (study plan) | ✅ Audited | Light + Dark |
| 3 | Dashboard (readiness) | ✅ Audited | Light + Dark |
| 4 | Dashboard (streak) | ✅ Audited | Light + Dark |
| 5 | Hamburger Menu | ✅ Audited | Light |
| 6 | Writing (top) | ✅ Audited | Light |
| 7 | Writing (workspace) | ✅ Audited | Light |
| 8 | Speaking (top) | ✅ Audited | Light |
| 9 | Speaking (drills) | ✅ Audited | Light |
| 10 | Reading | ✅ Audited | Light |
| 11 | Listening | ✅ Audited | Light |
| 12 | Diagnostic | ✅ Audited | Light |
| 13 | Mocks | ✅ Audited | Light |
| 14 | Submissions | ✅ Audited | Light |
| 15 | Billing | ✅ Audited | Light |
| 16 | Progress | ✅ Audited | Light |
| 17 | Goals | ✅ Audited | Light |
| 18 | Community | ✅ Audited | Light |
| 19 | Achievements | ✅ Audited | Light |
| 20 | Settings | ✅ Audited | Light |
| 21 | Escalations | ✅ Audited | Light |
| 22 | Study Plan | ✅ Audited | Light |
| 23 | Readiness | ✅ Audited | Light |
| 24 | AI Conversation | ✅ Audited | Dark |
| 25 | Video Lessons | ✅ Audited | Dark |
| 26 | Settings Profile | ✅ Audited | Light |
| 27 | Notifications Panel | ✅ Audited | Light |
| 28 | Grammar Lessons | ✅ Audited (error) | Light |
| 29 | Vocabulary Lessons | ✅ Audited (error) | Light |
| 30 | Strategies Lessons | ✅ Audited (error) | Light |
| 31 | Pronunciation Lessons | ✅ Audited (error) | Light |
| 32 | /practice | ✅ Audited (404) | Light |

---

## What Works Well ✅

1. **Design System Consistency** — Manrope font, violet accent, cream canvas, rounded cards applied uniformly across all pages
2. **Mobile Navigation** — Bottom nav bar with 5 key skills is intuitive; hamburger menu organizes all other pages well
3. **Dark Mode** — Full dark mode support with proper contrast, readable text, and visible interactive elements
4. **Card-Based Layout** — Consistent card design language across dashboard, skill modules, and settings
5. **Hero Section Pattern** — Unified hero with icon, title, description, and stat badges gives each page identity
6. **Progress Visualization** — Readiness gauge (65%), skill breakdown bars, and streak counter are well-designed
7. **Empty States** — Present on all pages, providing basic guidance even when no data exists
8. **Notification System** — Panel with All/Unread tabs, Mark All Read, and individual notification cards works well
9. **Responsive Typography** — Text scales appropriately for mobile viewport
10. **Study Plan Integration** — Dashboard prominently shows today's tasks with clear Start CTAs

---

## Priority Fix Order

1. **C-1** Developer copy exposure (5+ pages, 30 min fix — string replacements)
2. **C-2** Lesson sub-routes errors (nav links + routing, 1-2 hour fix)
3. **C-3** /practice 404 (add page.tsx or remove links, 15 min fix)
4. **M-1** Text truncation in hero badges (CSS change, 15 min fix)
5. **M-2** Diagnostic button overlap (CSS padding, 10 min fix)
6. **M-3** Reading "Loading..." fallback (string change, 5 min fix)
7. **M-4** SignalR hub 504 (backend/infra investigation, 1+ hour)
8. **M-5** Analytics endpoint failure (backend investigation, 30+ min)
9. **m-1 through m-7** Minor issues (batch fix, 2-3 hours total)
