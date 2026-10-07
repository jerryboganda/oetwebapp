# Frontend / BFF optimizations (2026-10-06)

Layer 05 (`stack/05-front`) of the optimisation program. The web app and its `/api/backend` proxy were
spending requests, bytes and connections the learner never sees; every item below removes some of
that. Nothing here changes a score, an entitlement or a payment outcome. Items that do change what a
user can observe are listed under "Behaviour changes".

**Verification status: not tested - owner QA** (owner directive 2026-10-06: no automated QA runs
anywhere). The only automated check on this change is compilation inside `Build images`
(`next build`, `dotnet publish`); nothing was built, run or benchmarked locally. The `*.test.*` /
`__tests__` files it adds were deleted on 2026-10-08 with all test code (still in git history at tag `last-commit-with-tests`); no CI
lane runs tests and none has been run for this change.

## 1. Bytes on the wire

| Change | Where | Effect |
| --- | --- | --- |
| Writing message bundle (~50 KB en / ~65 KB ar) is no longer inlined into every HTML response | `i18n.ts` (`loadAllMessages(locale, { includeWriting: false })`, `loadWritingMessages`), `app/layout.tsx`, `app/(learner)/layout.tsx`, `components/providers/extra-messages-provider.tsx`, `lib/i18n/message-fallback.ts` | Sign-in, admin, expert and public pages stop carrying Writing copy. The learner layout supplies it through a nested provider that merges into the root messages (a nested next-intl provider replaces, so the parent is read back and merged). The default of `loadAllMessages` is unchanged. Merged bundles are also memoized per (locale, modules) per server process. |
| PayPal and Whop checkout SDKs load only when their checkout is shown | `components/billing/lazy-paypal-expanded-checkout.tsx`, `components/checkout/lazy-whop-embedded-checkout.tsx`, `lib/billing/whop-ids.ts`, `app/checkout/review/page.tsx` | The SDKs left the first-load bundle of checkout and private-speaking booking. `CheckoutLoadBoundary` turns a failed chunk download into the page's existing hosted / redirect fallback (`onUnavailable`) instead of an error page. `whop-embedded-checkout.tsx` still re-exports the id checks. |
| AI-assistant SignalR hub connects lazily | `contexts/ai-assistant-context.tsx` (`activate()`), `hooks/use-ai-assistant.ts` (`autoConnect`), `AiAssistantPanel.tsx`, `app/(learner)/companion/page.tsx` | Before, every learner, expert and admin opened a long-poll hub plus two REST reads on every page load. Now nothing connects until the panel or the companion page asks for it; it stays active until sign-out and is keyed by user. |

## 2. Requests per page load

| Change | Where | Effect |
| --- | --- | --- |
| One batched feature-flag request | `hooks/use-feature-flag-map.ts` (10 ms coalescing window), `lib/api/gamification.ts` (`fetchLearnerFeatureFlags`), API `GET /v1/features?keys=a,b,c` | The shell read several flags from several places, one request per flag per key set. Now one request, at most 16 distinct keys per call (larger sets are split). A key the API does not expose is absent and reads disabled, as the single route's 404 did. |
| Shared entitlement query, no double credit fetch | `app/(learner)/private-speaking/page.tsx`, `components/domain/catalog/*`, `lib/credit-feedback.ts` (`seedCreditCaches`) | The private-speaking page and the catalog read the dashboard's shared entitlement query instead of a private fetch; the private-speaking page still revalidates it on every visit (`staleTime: 0`), so booking eligibility and the remaining-session count are never up to 2 minutes stale there. After a credit usage the fresh snapshot is written into the existing credit-card caches instead of invalidating them (which made every observer fetch the same snapshot again). |
| Notification preferences and push configuration load on first use | `contexts/notification-center-context.tsx` (`ensureSettingsLoaded`), the two settings surfaces | The bell no longer pays for them at app start. A learner who already granted browser push keeps the push-configuration fetch at mount. |
| Notification hub: no second connection, hidden tab does not poll | `contexts/notification-center-context.tsx` | A tab regaining focus while the first hub was still connecting used to open a second hub and leak the first. The fallback poll skips ticks while the tab is hidden (Page Visibility) and catches up when it is visible. |
| Product analytics and Listening attempt events are batched | `lib/telemetry/event-batcher.ts`, `lib/analytics.ts`, `lib/listening-api.ts`; API `POST /v1/analytics/events/batch`, `POST /v1/listening-papers/attempts/{id}/integrity-events/batch` | See section 4. |
| Per-account module caches are reset at sign-out | `hooks/use-exam-date-gate.ts`, `hooks/use-placement-access.ts` (`registerResettable`) | Not a load saving: the module-level answers were not keyed by user, so the next account on the same tab inherited them. |

## 3. Failure behaviour under load

| Change | Where | Effect |
| --- | --- | --- |
| Retry delay is jittered (50-100% of the 1 s / 3 s slot) and honours `Retry-After` | `lib/api/client.ts` | Fixed delays made every client that failed together retry together (the 30 Sep 2026 connection-pool incident). A `Retry-After` longer than 10 s (`MAX_RETRY_AFTER_MS`) fails the call now instead of holding a spinner. |
| A 429 on a write is not replayed | `lib/api/client.ts` | A shed write replayed only adds load. `ApiError.retryable` is unchanged, so a user-initiated retry still works. Reads and 5xx / 408 / network errors keep their two retries. |
| React Query does not retry what `apiRequest` already retried | `components/providers/query-provider.tsx` (`shouldRetryQuery`) | A retryable `ApiError` reaching a query is the budget spent; a second round made it up to six attempts. Everything else keeps its one retry (a 401 caught mid token-refresh recovers on it). |
| Large uploads opt out of the 30 s / replay defaults | `lib/api.ts` (`postForm(..., options)`), `lib/content-upload-api.ts` (5 min per part), `app/admin/content/papers/import/page.tsx` (ZIP staging: no retry, 30 min) | A replayed ZIP re-uploaded up to 1 GiB three times; a 30 s timer aborted a chunk on any link slower than about 2 Mbps. |
| Service worker never intercepts SignalR hubs; cache version `oet-v8` | `public/sw.js`, `lib/service-worker-cache.ts`, `lib/auth-storage.ts` | Every long-poll URL is unique (`&_=<ms>`), so the API cache gained one entry per poll per tab. `oet-v8` purges the v7 entries on activate. `clearStoredSession` (every sign-out and session loss) now tells the worker to drop its API cache, which is keyed by URL only and would otherwise serve the previous account's bodies. |
| Writing hubs ask for a fresh access token on every (re)connect | `lib/writing/realtime.ts` | A token captured once expired and every automatic reconnect after that failed with 401. Falls back to the connect-time token only if refreshing fails. |

## 4. Telemetry batching

- **Contract** (`lib/telemetry/event-batcher.ts`): best effort and never retried (a failed batch is
  dropped); one batch in flight at a time, oldest first; a send still pending after 20 s stops blocking
  the queue; at most 500 unsent items (oldest dropped); on `visibilitychange: hidden` / `pagehide`
  everything queued is sent at once with `keepalive: true`.
- **Analytics**: batches of up to 20 or 3 s, bodies above 48 KiB are split (the API ignores a body above
  64 KiB). A lone event still uses `POST /v1/analytics/events`. Sign-out sends what is queued first and
  waits at most 1.5 s (`flushAnalytics`), because a batch sent after sign-out has no token left. The BFF
  treats `/v1/analytics/events/batch` exactly like the single route (an unreadable body is a 204).
- **Listening attempt events**: only the record-only events are batched (`answer_changed`, `highlight`,
  `strikethrough`, buffering start / end / stalled, reading time start / end), 20 or 1.5 s, one request
  per attempt in the order they happened. The integrity-lock signals, `audio_error`,
  `audio_started` / `audio_progress` / `audio_ended`, `section_transition` and `auto_submit` still go one
  at a time, immediately. The API applies a batch event by event through the same service call as the
  single route (at most 50 per batch; a larger one is rejected, not truncated).
- **Mixed-version safety**: if the API answers a batch route with "no such route" (404 with no JSON
  body, 405 or 501) the client stops batching for the rest of that page load and sends singly; the flag
  batch does the same per key. Web and API are promoted as a pair, so this only matters for an API-only
  rollback or the moment between promotions.

## 5. BFF (`/api/backend`)

| Change | Where | Effect |
| --- | --- | --- |
| Request bodies with a declared length above 1 MiB are streamed to the API | `app/api/backend/[...path]/route.ts`, `lib/backend-proxy.ts` (`streamedBodyLength`) | Speaking recordings, 8 MB admin upload chunks and whole ZIP imports were buffered whole in the web container (1 GB per blue/green slot) per request. The declared `content-length` is restored on the upstream request and `duplex: 'half'` is set, so the API sees the same framing as before. Smaller bodies, bodies of unknown length and analytics events are buffered as before. **Kill switch: put `BFF_STREAM_REQUEST_BODIES=0` in `.env.production`.** `docker-compose.production.yml` forwards it to both web slots (the `&web-env` block of `x-web-slot`, default empty = streaming on; the slots take an explicit env list, so a key that is not declared there never reaches the container). The route reads it per request, but a container only sees a changed value when it is recreated, i.e. at the next pipeline rollout of the idle slot, so it is a no-code rollback, not an instant one. The code-level alternative is the normal rollback, `gh workflow run production-deploy.yml -f sha=<previous-sha>`. |
| The tutor-room cue hub is exempt from the proxy CSRF check | `lib/backend-proxy.ts` (`SIGNALR_HUB_PATH_PATTERN`) | `/v1/speaking/live-rooms/hub` has two path segments before `hub`, which the single-segment hubs never had. Its negotiate POST carries only the bearer token, so a browser holding the `oet_rt` cookie failed the CSRF check and the cue channel never connected. The hub is `RequireAuthorization()`. |

## 6. API surface added (backend)

| Route | Notes |
| --- | --- |
| `POST /v1/analytics/events/batch` | `{ events: [...] }`, at most 50 events and 64 KiB; same tolerance as the single route (empty, malformed or oversized body is a 204; an invalid entry is skipped without costing the others). The body is read through a bounded buffer, so a chunked request with no `Content-Length` is never read past the cap. `AnalyticsIngestionService.RecordBatchAsync` saves once. |
| `GET /v1/features?keys=a,b,c` | `LearnerOnly`, same allow-list and per-flag code as `GET /v1/features/{key}` (`ResolveLearnerFeatureFlagAsync`), answered as `{ flags: [{ key, enabled }] }`; at most 16 distinct keys; sequential on purpose (the services share one scoped `DbContext`). |
| `POST /v1/listening-papers/attempts/{id}/integrity-events/batch` | `PerUserWrite` rate limit, at most 50 events, applied in order through `ListeningLearnerService.RecordIntegrityEventAsync`. |

No migration, no schema change, no new environment key other than the `BFF_STREAM_REQUEST_BODIES` kill
switch above (declared in `docker-compose.production.yml`; unset keeps streaming on). The API additions are described in `docs/product-manual/route-api-domain-surface-index.md`.

## 7. Behaviour changes (what an owner can observe)

1. An analytics event can reach the server up to 3 s after it happened (immediately on page hide), and an
   event whose request fails is dropped, never retried.
2. A Listening `answer_changed` / highlight / buffering event can be recorded up to 1.5 s late; its own
   `occurredAt` is kept. Audio and integrity-lock events are unchanged.
3. The assistant bell / floating panel opens its hub on first use, so its first open connects a moment
   later than before.
4. A write that the API sheds with 429 now fails at once (and can be retried by the user) instead of being
   replayed twice behind the scenes; a `Retry-After` above 10 s is no longer waited out.
5. The service worker cache is renamed (`oet-v8`): the first load after this release re-fetches cached
   API reads once.

## 8. What the owner should try by hand

- Sign in, open the dashboard, Writing hub and a Writing task: Writing copy shows (no `writing.*` key text).
  Sign-in and admin pages still render.
- Checkout with PayPal and with Whop (embedded) shows the skeleton, then the payment element; the hosted /
  redirect option still works.
- Open the assistant panel (and `/companion` when the flag is on): it connects after opening, and not before.
- Network tab on a cold dashboard load: one `GET /v1/features?keys=...`, no per-flag requests; a few seconds
  of activity produce one `POST /v1/analytics/events/batch`, not one request per click.
- Listening attempt: answer changes, highlights and buffering arrive in a `.../integrity-events/batch`
  call; a blur, fullscreen exit or audio event is still its own request.
- Admin: upload a large paper PDF chunk and a ZIP import (bodies above 1 MiB take the streaming path).
- Sign out, sign in as a different account on the same tab: no previous-account placement / exam-date
  state, and no cached API bodies from the earlier account.
- Tutor live room: the cue hub connects (no CSRF failure on negotiate).
- If streaming misbehaves in production, set `BFF_STREAM_REQUEST_BODIES=0` in `.env.production`; it takes effect when the next pipeline rollout recreates the web slot (or roll back with `gh workflow run production-deploy.yml -f sha=<previous-sha>`).
