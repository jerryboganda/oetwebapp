# Admin AI Assistant — UX Specification

> Produced by the `agency-designer` subagent. Implementation-ready UX/UI spec.
> Honors `DESIGN.md`, motion tokens (`lib/motion.ts`), Tailwind 4, motion v12 (`motion/react`).

## 1. Component Tree (Phase 1 — Widget MVP)

```text
AiAssistantProvider                              # contexts/ai-assistant-context.tsx
└── AiAssistantRoleGate                          # role==='admin' check (server + client)
    └── AiAssistantHotkeyProvider                # Cmd/Ctrl+J, Esc, Cmd+K within panel
        ├── AiAssistantLauncher                  # 56px floating action button (FAB)
        └── AiAssistantPanel                     # animated slide-in overlay
            ├── AiAssistantPanelHeader
            │   ├── ThreadTitle (editable, generated)
            │   ├── ProviderModelChip
            │   ├── QuickMenu (settings, threads, indexing, audit, close)
            │   └── PanelControls (minimize, expand, dismiss)
            ├── ThreadSidebar (optional, collapsible)
            │   ├── NewChatButton
            │   ├── SearchInput
            │   ├── ThreadList (virtualized, infinite scroll)
            │   │   └── ThreadListItem
            │   └── ThreadSidebarFooter (archived, retention info)
            ├── PanelMain
            │   ├── ChatMessageList (virtualized)
            │   │   ├── EmptyStateHero (no messages)
            │   │   ├── MessageGroup
            │   │   │   ├── UserMessage
            │   │   │   ├── AssistantMessage
            │   │   │   │   ├── StreamingTokenBlock
            │   │   │   │   ├── ToolCallCard (collapsible)
            │   │   │   │   ├── ApprovalRequestInline
            │   │   │   │   ├── ErrorBanner
            │   │   │   │   └── MessageActions (copy, retry, branch, rate)
            │   │   │   └── SystemMessage (rare; kill switch, model swap)
            │   │   └── TypingIndicator
            │   ├── ApprovalModal (portal — focus-trapped)
            │   ├── AttachmentPicker (file / paste / index / template)
            │   ├── SlashCommandPalette (Cmd+K within panel)
            │   └── DegradedStateBanner (provider down, quota near cap)
            ├── PanelComposer
            │   ├── ComposerToolbar (attach, mention, mode toggle, model picker)
            │   ├── ComposerTextarea (auto-grow, paste-aware)
            │   ├── ComposerHints (token count, cost estimate, hotkeys)
            │   └── ComposerSendButton
            └── PanelResizer (horizontal drag handle)
```

## 2. Visual Layout (Phase 1)

### 2.1 Launcher FAB

- **Position:** fixed bottom-right, `bottom-6 right-6` desktop, `bottom-4 right-4` mobile.
- **Size:** 56×56 desktop, 48×48 mobile. Circular.
- **Z-index:** `z-[60]` (above shell `z-50`, below modals `z-[100]`).
- **Visual:** brand gradient bg, sparkle icon, animated subtle pulse when admin has unread (post-Phase 1).
- **Tooltip:** "AI Assistant (⌘J)".
- **Hidden:** when panel open; replaced by close affordance in panel.
- **Suppressed during deploy/maintenance** via `AiAssistant:GlobalEnabled` runtime setting.

### 2.2 Panel (desktop)

- **Position:** fixed right edge, full viewport height, slides in from right.
- **Default width:** 480px. Resizable 320–800px via `PanelResizer`. Persisted to `localStorage` key `aiAssistant.panelWidth`.
- **Background:** `bg-surface dark:bg-surface-dark`, `border-l border-border`, `shadow-2xl`.
- **Header height:** 56px sticky.
- **Composer height:** auto-grow 56px → 200px max.
- **Main content:** flex-1, vertical scroll, virtualized.

### 2.3 Panel (mobile <768px)

- **Bottom sheet:** 90vh tall, slides up from bottom.
- **Snap points:** 60vh (compact), 90vh (full). Drag handle on top.
- **Sidebar:** hidden behind hamburger; opens as full-screen overlay.

### 2.4 Sidebar (`xl` only — ≥1280px)

- Collapsible. Default expanded on `xl`, collapsed below.
- **Width:** 240px. Sticky inside panel.

## 3. Message Types

| Type | Visual | Key Props |
|---|---|---|
| **UserMessage** | Right-aligned, brand bg, white text, rounded-2xl, max-w 80% | content, timestamp, attachments[], status |
| **AssistantMessage** | Left-aligned, surface bg, border, rounded-2xl, max-w 90% | content (streamed), tool calls[], model, tokens, cost, status |
| **ToolCallCard** | Inline within assistant message, collapsible accordion | tool, args (formatted), result, status, approval status, duration |
| **ApprovalRequestInline** | Yellow border, action buttons (Approve / Deny / Edit Args) | tool, args preview, dangerLevel, requiresUnrestricted |
| **ErrorBanner** | Red border, error message, retry button | error code, message, recoverable, suggestedAction |
| **SystemMessage** | Centered, muted text, icon | type (kill_switch / model_swap / quota_reached), message |
| **EmptyStateHero** | Centered, illustration, suggested prompts | suggestions[], onSelect |

### Streaming animation

- Token-by-token via SignalR `TokenDelta` frames.
- Markdown re-rendered on every frame (memoized for speed).
- Cursor `▍` blinks at end of streaming text.
- Smooth opacity fade-in for new tokens (`motion.span animate={{opacity: [0, 1]}}` with `tokens.subtle.swift`).

## 4. Composer

### 4.1 Composer Layout

```text
┌──────────────────────────────────────────────────────────────┐
│ [📎] [#] [@] [Mode ▾] [Model ▾]                              │
├──────────────────────────────────────────────────────────────┤
│  Ask anything about the codebase, deploy, content, or tests… │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│ 12 / 4000 tokens • ~$0.001 est • Cmd+Enter to send          │
└────────────────────────────────────────────────────────[Send]┘
```

### 4.2 Composer Toolbar

- **📎 Attach:** opens `AttachmentPicker` (file/folder/code/template tabs).
- **# Index:** quick-link to currently focused admin page in context.
- **@ Mention:** opens `SlashCommandPalette` with mentions (admin, route, content paper, user).
- **Mode:** `Chat / Plan / Execute / Review`. Adjusts model temperature, tool availability, system prompt.
- **Model picker:** chips of configured providers. Default = admin's preferred from settings.

### 4.3 Slash Commands

| Command | Action |
|---|---|
| `/help` | Show all commands |
| `/threads` | Open thread sidebar |
| `/new` | Start fresh thread |
| `/model [provider/model]` | Switch model for next turn |
| `/clear` | Clear current thread context (creates new thread) |
| `/index [path]` | Trigger re-index of path |
| `/audit [filter]` | Open audit log filtered |
| `/role [admin\|expert\|learner]` | Simulate role context (admin-only, read-only) |
| `/kill` | (system_admin only) trigger kill switch |
| `/export [format]` | Export thread (markdown/json) |

## 5. Attachment Tray

4 tabs in `AttachmentPicker`:

1. **File Upload:** drag/drop or browse. Limits: 8 files/message, 5MB/file, allowed mimes (text, image, pdf, json, md, code). Stored via `IFileStorage`.
2. **Paste from Clipboard:** detects code / URL / text. Auto-extracts.
3. **From Index:** semantic search → pick chunk(s) to inject as context.
4. **Templates:** saved prompts ("Generate migration", "Review PR diff", "Plan deploy").

## 6. Thread Sidebar

- **NewChatButton:** primary CTA.
- **SearchInput:** debounced (300ms), searches titles + first messages.
- **ThreadList:** virtualized (`react-virtuoso` or built-in), grouped by relative time (Today, Yesterday, Last 7 days, Earlier).
- **ThreadListItem:** title (truncated), preview (last message, 60 chars), timestamp, model chip, unread dot.
- **Context menu (right-click):** rename, archive, export, delete.
- **Footer:** "Archived (12)" link, retention info ("Threads auto-delete after 90 days").

## 7. Settings Quick Menu (top-right gear)

Inline (not full settings page):
- Provider/model preference
- Mode default (Chat/Plan/Execute/Review)
- Approval mode (Always / Unrestricted for write/shell)
- Token budget warning threshold
- Theme (auto/light/dark — usually inherits)
- Notifications (in-panel toast vs OS notification)
- "Open full settings" link → `/admin/settings/ai-assistant`

## 8. States (every component)

### 8.1 Empty

- **EmptyStateHero:** centered, brand illustration, "How can I help today?" headline, 4 suggested prompts as clickable cards. Suggestions context-aware (current admin page, recent content papers).

### 8.2 Loading

- **Hub connecting:** subtle top progress bar.
- **Sending message:** composer disabled, send button → spinner. Optimistic user message renders immediately.
- **Streaming response:** typing indicator below last assistant message, tokens stream in. Cancel button visible.
- **Tool executing:** ToolCallCard shows spinner + elapsed timer (5s+ shows progress messages "Reading file...", "Searching codebase..." from SignalR).
- **Indexing:** progress bar (n/total chunks), per-file activity. Background job — non-blocking.

### 8.3 Error

- **Network failure:** banner "Connection lost. Retrying..." with manual retry. SignalR auto-reconnect with exponential backoff (1s, 2s, 5s, 10s, 30s max).
- **LLM provider error:** `ErrorBanner` "Provider unavailable. Try another?" with provider switcher inline.
- **Approval denied:** ToolCallCard shows red border, "Denied by you" message, "Retry with different args" button.
- **Quota exceeded:** banner "Daily token budget reached. Resets at 00:00 UTC." with admin link to budgets.
- **Kill switch active:** prominent banner "AI Assistant disabled by admin", composer disabled, panel still viewable for history.

### 8.4 Degraded

- **One provider down:** `DegradedStateBanner` "OpenAI unavailable; using Anthropic". Subtle, non-blocking.
- **Slow streaming (>10s no tokens):** "Still working..." indicator with cancel.
- **Tool timeout:** ToolCallCard shows "Timed out after 60s. Retry?" with adjusted timeout option.

## 9. Accessibility (WCAG 2.2 AA target)

### 9.1 Keyboard

- **Global:** `Cmd/Ctrl+J` toggles panel.
- **Within panel:** `Esc` closes. `Cmd+K` opens slash palette. `Cmd+Enter` sends. `Cmd+Up/Down` navigates messages. `Tab` cycles focusable elements. `Cmd+B` toggles sidebar.
- **Composer:** `Shift+Enter` newline. `Enter` sends (if `sendOnEnter=true`). `Up arrow` recalls last message.
- **Approval modal:** focus-trapped. `Tab`/`Shift+Tab` cycles. `Enter`=Approve. `Esc`=Deny.

### 9.2 ARIA

- Panel: `role="complementary" aria-label="AI Assistant"`.
- Messages: `role="log" aria-live="polite" aria-atomic="false"`.
- Streaming text: `aria-busy="true"` during streaming.
- Tool cards: `role="region" aria-label="Tool: read_file"`, collapsible with `aria-expanded`.
- Approval modal: `role="alertdialog" aria-modal="true" aria-labelledby aria-describedby`.
- Composer: `role="textbox" aria-multiline="true" aria-label="Message AI Assistant"`.

### 9.3 Screen Reader

- New assistant tokens announced via `aria-live="polite"` (debounced, not per-token).
- Tool calls announced: "Tool call: read_file with arguments lib/scoring.ts".
- Approvals: "Approval required for write_file. Press Tab to review."
- Errors via `aria-live="assertive"`.

### 9.4 Contrast / Motion

- All text ≥ 4.5:1 (normal) / 3:1 (large) per `tailwind.config` palette.
- Focus rings: 2px brand color, visible on all interactive elements.
- Tap targets: ≥ 44×44px on mobile.
- `prefers-reduced-motion: reduce` removes pulse, panel slide, token fade.

## 10. Design Tokens (from `DESIGN.md`)

- **Surface:** `bg-surface`, `bg-surface-dark`. Sidebar: `bg-surface-muted`.
- **Borders:** `border-border`, focus `ring-brand-500`.
- **Text:** `text-foreground`, muted `text-muted-foreground`, brand `text-brand-600 dark:text-brand-400`.
- **Brand:** existing scale. Gradient FAB: `from-brand-500 to-brand-600`.
- **Semantic:** success/warning/error/info per token map.
- **Radii:** messages `rounded-2xl`. Cards `rounded-xl`. Buttons `rounded-md`. FAB `rounded-full`.
- **Shadows:** panel `shadow-2xl`, FAB `shadow-lg hover:shadow-xl`.
- **Spacing:** 4px base. Message gap `gap-3`, group gap `gap-6`. Composer padding `p-4`.
- **Typography:** prose `text-sm leading-relaxed`. Code `font-mono text-xs`. Tool args formatted JSON syntax-highlight.

## 11. Motion (`motion/react` + `lib/motion.ts`)

- **Panel slide-in:** `motion.aside` `initial={{ x: '100%' }} animate={{ x: 0 }} exit={{ x: '100%' }} transition={tokens.overlay.swift}` (~220ms cubic-bezier).
- **FAB pulse:** `tokens.subtle.gentle`, every 3s when unread.
- **Token streaming:** `motion.span` per chunk `initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.1 }}`.
- **Approval modal:** `motion.div` scale + fade `transition={tokens.modal.swift}` (~180ms).
- **List item enter:** `tokens.list.subtle` stagger 30ms.
- **Tool card expand:** `tokens.section.swift` (height animation).
- All wrapped in `MotionConfig reducedMotion="user"` for `prefers-reduced-motion`.

## 12. State Management

| State | Where |
|---|---|
| Panel open/closed | Zustand store `useAiAssistantStore` |
| Current thread ID | URL param `?aiThread=<id>` (deep-linkable) |
| Panel width | `localStorage` |
| Composer draft | `sessionStorage` per thread |
| Threads / messages | server via SignalR (canonical) + React Query cache |
| Hub connection | `useAiAssistantHub` hook |
| Settings | server `/v1/admin/ai-assistant/settings` |
| Approvals queue | Zustand (ephemeral) |
| Active streaming msg | Zustand |

## 13. Performance Budgets

- **Bundle (initial):** ≤ 80kB gzipped for widget. Lazy-load message renderer, markdown, syntax-highlighter, attachments after panel open.
- **TTI on admin page:** widget mount ≤ 50ms (FAB only).
- **Panel open → first paint:** ≤ 100ms (skeleton).
- **First token latency:** target ≤ 800ms, surface "Connecting..." after 500ms.
- **Message list virtualization:** required when >50 messages.
- **Streaming render:** debounce markdown re-render to 60fps (16ms).

## 14. Admin CMS Pages (`/admin/ai-assistant/*`) — Phase 2+

1. **Dashboard** (`/admin/ai-assistant`) — overview (active threads, today's tokens/cost, recent approvals, kill-switch toggle, quick links).
2. **Threads** (`/admin/ai-assistant/threads`) — DataTable: title, owner, model, message count, last activity, tokens, cost. Filters: owner, model, date, status. Bulk actions: archive, export, delete.
3. **Thread Detail** (`/admin/ai-assistant/threads/[id]`) — full transcript, tool invocations sidebar, audit timeline, export, replay (re-run prompts against different model).
4. **Providers** (`/admin/ai-assistant/providers`) — list configured providers; CRUD + test connection. Encrypted keys never displayed.
5. **Role Matrix** (`/admin/ai-assistant/role-matrix`) — RBAC editor: which roles get widget, which tools, approval requirements.
6. **Indexing** (`/admin/ai-assistant/indexing`) — codebase indexer status, last index run, per-path stats, manual re-index, exclusion patterns.
7. **Audit** (`/admin/ai-assistant/audit`) — `AiAuditEvent` table: actor, action, target, before/after diff, IP, UA. Filters + CSV export.
8. **Settings** (`/admin/settings/ai-assistant`) — kill-switch, default provider, global budgets, retention, approval policies, feature flags.

All pages use existing `AdminDashboardShell` + `DataTable` + design tokens. Approval flows reuse `Modal` + `Button[variant="primary"|"danger"]`. Per `AGENTS.md`: Badge `variant="danger"` (not `destructive`); Button `variant="primary"` (not `default`).
