# Listening exam modes

OET delivers Listening through computer-based configurations supported by the
platform. The **mode value** decides which
behaviours unlock and which UI skin is rendered. Mode lives on
`ListeningAttempt.Mode` (`Exam` | `Learning` | `Drill` | `MiniTest` |
`ErrorBank` | `Home` | `Diagnostic`). Paper-based simulation is not exposed.

| Real exam | Platform mode | Skin (planned) | Replay | Navigation |
|---|---|---|---|---|
| OET on Computer (test centre) | `Exam` | `ComputerSkin` | Disabled | Forward-only, locks per section |
| OET@Home (platform guidance mode) | `Home` | `HomeSkin` | Disabled | Forward-only; fullscreen/focus guidance + paste block |
| Learning / Drill (practice) | `Learning` / `Drill` / `MiniTest` / `ErrorBank` | `ComputerSkin` | Disabled | Free navigation; transcript review after submit |
| Diagnostic (placement) | `Diagnostic` | `ComputerSkin` | Disabled | Forward-only; routes to pathway recommendation |

Paper-based Listening simulation is out of scope. Legacy paper query values
fail closed to the computer exam surface and are never sent to the API.

## What the modes share

- **Audio source** — same MP3, same transcript timing.
- **Scoring** — `ListeningGradingService` is mode-agnostic. Raw marks are
  deterministic; scaled score and pass status come only from a complete,
  owner-approved, versioned conversion table captured on the attempt.
- **FSM** — `ListeningFsmTransitions` + `ListeningSessionService`.
  `ListeningModePolicy` adjusts the policy (one-way locks, confirm dialog,
  unanswered warning) per mode.
- **Annotations** — highlight + strikethrough persistence works in any mode.
  Server caps the payload at 64 KB; see [`hooks/use-listening-annotations.ts`](../../hooks/use-listening-annotations.ts).

## What the skins change

The Wave 3 implementation pulls the player surface into sibling components
under `components/domain/listening/player/skins/`. Computer and Home are the
supported production skins; no Paper skin or booklet renderer is shipped.

| Behaviour | Computer | Home | Paper |
|---|---|---|---|
| Audio player visible | Yes | Yes | No (room audio implied) |
| Scrub allowed | No | No | N/A |
| Fullscreen required | No | No; guidance only | No |
| Paste / context-menu blocked | No | Yes | No |
| Background | Surface | Black distraction-free | Paper-tone |
| Bubble-sheet style B/C | No | No |
| Final review banner | C2 review window | C2 review window |
| Print stylesheet | No | No |

## Policy fields per mode

| Policy field | Used by |
|---|---|
| `ExamReplayAllowed` | All exam-strict modes — defaults `false` |
| `LearningReplayAllowed` | `Learning`, `Drill`, `MiniTest`, `ErrorBank` |
| `OneWayLocksEnabled` | `Exam`, `Home`, `Diagnostic` |
| `ConfirmDialogRequired` | `Exam`, `Home` |
| `UnansweredWarningRequired` | `Exam`, `Home`, `Diagnostic` |
| `ReviewWindowMsC2FinalCbt` | `Computer` skin |

## OET@Home specifics

- The Home skin is a visual/guidance layer only. Fullscreen is optional and
  never blocks launch, progress, or submission unless a future dedicated
  exam-rehearsal policy is explicitly owner-approved.
- Focus and fullscreen changes are recorded as non-blocking technical guidance
  telemetry; they do not produce a learner lock or warning state.
- Paste/context-menu blocking remains a platform interaction safeguard and does
  not imply remote proctoring. Camera, screen recording, and ID verification
  are separate initiatives.
- The paste-block listener mirrors the Speaking proctoring listener at
  [`components/domain/listening/player/`](../../components/domain/listening/player/).

## Out of scope

- PDF generator for Paper-skin booklet — browser `Print → PDF` is the
  v1 path. A dedicated PDF service is tracked in the broader content
  pipeline backlog.
- Adaptive (CAT/IRT) Diagnostic — deferred to v2.1 (note already in
  `ListeningAttemptMode.Diagnostic` doc comment).
