# Listening exam modes

OET delivers Listening in three real-world configurations. The platform
mirrors all three on a single FSM player; the **mode value** decides which
behaviours unlock and which UI skin is rendered. Mode lives on
`ListeningAttempt.Mode` (`Exam` | `Learning` | `Drill` | `MiniTest` |
`ErrorBank` | `Home` | `Paper` | `Diagnostic`).

| Real exam | Platform mode | Skin (planned) | Replay | Navigation |
|---|---|---|---|---|
| OET on Computer (test centre) | `Exam` | `ComputerSkin` | Disabled | Forward-only, locks per section |
| OET@Home (remote proctored) | `Home` | `HomeSkin` | Disabled | Forward-only + kiosk fullscreen + paste block |
| OET on Paper (booklet) | `Paper` | `PaperSkin` | N/A (one play of room audio) | Free within section + final 2-min all-parts review |
| Learning / Drill (practice) | `Learning` / `Drill` / `MiniTest` / `ErrorBank` | `ComputerSkin` | Allowed | Free; transcript loop available |
| Diagnostic (placement) | `Diagnostic` | `ComputerSkin` | Disabled | Forward-only; routes to pathway recommendation |

## What the modes share

- **Audio source** — same MP3, same transcript timing.
- **Scoring** — `ListeningGradingService` is mode-agnostic. Raw → scaled
  always routes through `OetScoring.OetRawToScaled`.
- **FSM** — `ListeningFsmTransitions` + `ListeningSessionService`.
  `ListeningModePolicy` adjusts the policy (one-way locks, confirm dialog,
  unanswered warning) per mode.
- **Annotations** — highlight + strikethrough persistence works in any mode.
  Server caps the payload at 64 KB; see [`hooks/use-listening-annotations.ts`](../../hooks/use-listening-annotations.ts).

## What the skins change

The Wave 3 implementation pulls existing player surface into three sibling
components under `components/domain/listening/player/skins/`. The current
production build still renders the unified ComputerSkin path; Home and
Paper skins are scoped in this iteration but not yet selected by the player.

| Behaviour | Computer | Home | Paper |
|---|---|---|---|
| Audio player visible | Yes | Yes | No (room audio implied) |
| Scrub allowed | No | No | N/A |
| Fullscreen required | No | Yes (`requestFullscreen`) | No |
| Paste / context-menu blocked | No | Yes | No |
| Background | Surface | Black distraction-free | Paper-tone |
| Bubble-sheet style B/C | No | No | Yes |
| Final review banner | C2 review window | C2 review window | All-parts (`FinalReviewAllPartsMsPaper`) |
| Print stylesheet | No | No | Yes |

## Policy fields per mode

| Policy field | Used by |
|---|---|
| `ExamReplayAllowed` | All exam-strict modes — defaults `false` |
| `LearningReplayAllowed` | `Learning`, `Drill`, `MiniTest`, `ErrorBank` |
| `OneWayLocksEnabled` | `Exam`, `Home`, `Diagnostic` |
| `ConfirmDialogRequired` | `Exam`, `Home` |
| `UnansweredWarningRequired` | `Exam`, `Home`, `Paper`, `Diagnostic` |
| `ReviewWindowMsC2FinalCbt` | `Computer` skin |
| `ReviewWindowMsC2FinalPaper` | `Paper` skin |
| `FinalReviewAllPartsMsPaper` | `Paper` skin only (last 2 min banner) |

## OET@Home specifics

- Kiosk visual layer only. Real remote proctoring (camera, screen-record,
  ID verification) is a separate initiative.
- Paste-block listener mirrors the Speaking proctoring listener at
  [`components/domain/listening/player/`](../../components/domain/listening/player/).
- `requestFullscreen` is requested at `intro → a1_preview` transition. If
  the learner exits fullscreen during audio, the session emits a
  `home.fullscreen.exited` audit event (TODO — wire to existing proctoring
  log when proctoring lands).

## Out of scope

- PDF generator for Paper-skin booklet — browser `Print → PDF` is the
  v1 path. A dedicated PDF service is tracked in the broader content
  pipeline backlog.
- Adaptive (CAT/IRT) Diagnostic — deferred to v2.1 (note already in
  `ListeningAttemptMode.Diagnostic` doc comment).
