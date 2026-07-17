using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Seeding;

// Phase 6 of the OET Speaking module roadmap.
//
// Seeds eight foundation Interlocutor Training modules. The first four
// (Onboarding) are required for calibration; the last four (Refresher)
// are surfaced quarterly. All eight rows share the `itm-seed-` Id prefix
// so the seeder is fully idempotent — re-running the bootstrap simply
// upserts content + flips `Status` / `RequiredForCalibration` if the
// admin team has tweaked them in the meantime.
public static class InterlocutorTrainingSeed
{
    public const string IdPrefix = "itm-seed-";

    public static async Task SeedAsync(LearnerDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var existing = await db.InterlocutorTrainingModules
            .Where(m => m.Id.StartsWith(IdPrefix))
            .ToDictionaryAsync(m => m.Id, ct);

        foreach (var spec in Modules)
        {
            if (existing.TryGetValue(spec.Id, out var row))
            {
                // Allow admins to override the seed once they tweak the
                // copy, but always keep the canonical Stage +
                // RequiredForCalibration flag aligned with the seed.
                row.Stage = spec.Stage;
                row.RequiredForCalibration = spec.RequiredForCalibration;
                row.OrderIndex = spec.OrderIndex;
                if (row.Status == ContentStatus.Draft)
                {
                    // Only refresh content on still-Draft seeds — once an
                    // admin has published or archived a row we leave the
                    // body alone.
                    row.Title = spec.Title;
                    row.ContentMarkdown = spec.ContentMarkdown;
                    row.UpdatedAt = now;
                }
            }
            else
            {
                db.InterlocutorTrainingModules.Add(new InterlocutorTrainingModule
                {
                    Id = spec.Id,
                    Title = spec.Title,
                    ContentMarkdown = spec.ContentMarkdown,
                    Stage = spec.Stage,
                    OrderIndex = spec.OrderIndex,
                    RequiredForCalibration = spec.RequiredForCalibration,
                    Status = ContentStatus.Published,
                    PublishedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record Spec(
        string Id,
        string Title,
        InterlocutorTrainingStage Stage,
        int OrderIndex,
        bool RequiredForCalibration,
        string ContentMarkdown);

    private static readonly Spec[] Modules = new Spec[]
    {
        new(
            $"{IdPrefix}welcome",
            "Welcome and Role of the Interlocutor",
            InterlocutorTrainingStage.Onboarding,
            1,
            RequiredForCalibration: true,
            ContentMarkdown:
@"# Welcome and Role of the Interlocutor

Your role is to conduct the test and play the patient, not to score the
candidate. You are the scaffolding that lets the candidate produce their
best performance — you are *not* a teacher, *not* a marker, and *not*
their support system.

## Three things you do

1. **Run the timing.** Five minutes for each role-play, plus three minutes
   of prep for the candidate.
2. **Play the patient.** Stay in character. The candidate's experience
   must feel like a real consultation.
3. **Surface every cue.** If a cue is on your card, the candidate must
   have the opportunity to address it.

## Three things you do **not** do

1. **Score the candidate.** That happens after the session.
2. **Coach.** No nudges, no hints, no recovery.
3. **Break character.** Even if the candidate is struggling, your face
   stays the patient's face."),

        new(
            $"{IdPrefix}following-script",
            "Following the Patient Script Consistently",
            InterlocutorTrainingStage.Onboarding,
            2,
            RequiredForCalibration: true,
            ContentMarkdown:
@"# Following the Patient Script Consistently

The interlocutor card is the canonical truth. Every candidate must face
the same patient, with the same concerns, with the same emotional
register. That is the only way scoring is fair across the cohort.

## Stay in character

* Use the patient's name, age, and occupation if they are on the card.
* Echo the patient's stated concern, in the patient's language. Do not
  paraphrase into clinical jargon.
* If the card says ""worried about side effects"", surface that
  concern at the appropriate moment — even if the candidate has not
  prompted you.

## Cues are mandatory

A cue is not a hint that you might surface. It is an instruction. Every
cue on your card must be raised somewhere in the five-minute role-play.
If the candidate's questions naturally bring it up, fine; if not, you
introduce it organically.

## What you do **not** do

* Add cues that are not on the card.
* Improvise extra symptoms.
* Bring up personal history that is not in your script."),

        new(
            $"{IdPrefix}no-over-helping",
            "Avoiding Over-Helping",
            InterlocutorTrainingStage.Onboarding,
            3,
            RequiredForCalibration: true,
            ContentMarkdown:
@"# Avoiding Over-Helping

Real interlocutors do NOT coach. They do not finish the candidate's
sentences. They do not nod sympathetically to reassure. They do not
""help them out"" if they are floundering.

The candidate's score depends on what *they* produce. Every prompt you
give them, every silence you fill, every easy follow-up you offer is a
data point lost to the marker.

## What over-helping looks like

* ""I think what you mean is..."" — completing the candidate's thought.
* ""So you want me to take the medication?"" — handing them the next move.
* ""That makes sense, thank you."" — closing the loop they have not yet
  closed.
* Nodding encouragingly through long pauses to telegraph approval.

## What appropriate stillness looks like

* Three to five seconds of silence is fine. The candidate must navigate
  it.
* A neutral ""mm-hmm"" is fine. Verbose agreement is not.
* If a candidate asks a clarifying question, answer it from the card —
  no extra context, no extra cues."),

        new(
            $"{IdPrefix}realistic-emotion",
            "Showing Realistic Emotion",
            InterlocutorTrainingStage.Onboarding,
            4,
            RequiredForCalibration: true,
            ContentMarkdown:
@"# Showing Realistic Emotion

The card will tell you the patient's emotional register: anxious, angry,
embarrassed, grieving, in pain, etc. Your job is to portray that
emotion convincingly so the candidate's Relationship-Building and
Patient-Perspective scores are testing something real.

## Anxiety

* Tight voice, hesitant phrasing, ""is this serious, doctor?""
* Repeated questions.
* Worst-case-scenario thinking.

## Anger

* Short, clipped sentences.
* Direct accusations: ""nobody told me this would happen.""
* Refuse the first reassurance — make the candidate work for trust.

## Embarrassment

* Avoid eye contact (if on camera).
* Hedge the symptom: ""it's probably nothing, but...""
* Wait for permission to elaborate.

## Calibration test

Before each session, glance at the card's emotion line. Hold that
emotion for the full five minutes. Drop it only when the role-play
ends."),

        new(
            $"{IdPrefix}using-cues-naturally",
            "Using Cue Prompts Naturally",
            InterlocutorTrainingStage.Refresher,
            5,
            RequiredForCalibration: false,
            ContentMarkdown:
@"# Using Cue Prompts Naturally

Cues should land like patient concerns, not test questions. ""I'm
worried about taking time off work"" sounds organic. ""So, my concern is
about the financial impact of the treatment plan"" sounds like a
checklist.

## When to surface each cue

1. **Early cues** (rapport-building, opening anxieties): drop these in
   the first 60 seconds, after the candidate's opening but before they
   start gathering information.
2. **Mid cues** (clarifying questions, side effects): surface these
   when the candidate is explaining or giving information.
3. **Late cues** (next steps, follow-up): hold these for the closing
   minute, once the candidate has summarised.

## Phrasing each cue

* Wrap the cue in everyday language. ""Will this hurt?"" not ""I'd like
  to enquire about the procedure's discomfort level.""
* Layer the cue with emotion from your card. An anxious cue is
  delivered differently from an angry cue.

## If the candidate beats you to it

If the candidate spontaneously addresses a cue before you raise it,
mark it as resolved on your scratchpad and move on. Do not raise it
again — that would be over-helping."),

        new(
            $"{IdPrefix}timing-pacing",
            "Keeping to Timing",
            InterlocutorTrainingStage.Refresher,
            6,
            RequiredForCalibration: false,
            ContentMarkdown:
@"# Keeping to Timing

The five-minute window is sacred. Your pacing decisions directly
affect every candidate's chance of finishing.

## The four-phase pacing target

| Phase            | Approx. minute | Your job                          |
|------------------|----------------|-----------------------------------|
| Opening          | 0:00 - 0:45    | Receive the greeting, give name   |
| Information      | 0:45 - 2:30    | Answer questions, surface cues    |
| Explanation      | 2:30 - 4:00    | Respond to teach-back, ask back   |
| Closing          | 4:00 - 5:00    | Confirm plan, accept summary      |

## Pacing levers you control

* **Verbosity.** Long answers eat the candidate's clock. Short answers
  give them air.
* **Cue placement.** Front-load cues if the candidate is slow; back-load
  them if the candidate is fast.
* **Pauses.** Three-to-five-second pauses are fine but do not stretch
  them into the closing window.

## When time is nearly up

* At 4:30, surface any unraised cues quickly.
* At 4:50, accept a closing summary even if it is short.
* At 5:00, end firmly. Do not let the candidate continue past time."),

        new(
            $"{IdPrefix}no-mid-corrections",
            "Avoiding Mid-Role-Play Corrections",
            InterlocutorTrainingStage.Refresher,
            7,
            RequiredForCalibration: false,
            ContentMarkdown:
@"# Avoiding Mid-Role-Play Corrections

Feedback comes after. During the role-play, your only job is to be
the patient. Even if the candidate says something factually wrong,
clinically inappropriate, or grammatically broken, you stay in
character.

## What you must not do mid-role-play

* Correct a clinical error. (""Actually, that is not how that medication
  works."")
* Correct a grammar slip. (""I think you mean *take*, not *takes*."")
* Comment on style. (""That was a great open question."")
* Pull a face. The candidate is watching for micro-expressions.

## What you do instead

* Note it on your scratchpad — *briefly*. One line. ""Clinical: stated
  ibuprofen for high BP."" or ""Grammar: third-person s on most
  verbs.""
* Forget about it. The marker will surface the same issue from the
  transcript.
* Stay in character. The patient does not correct the doctor on
  pharmacology — they just look confused or worried.

Mid-role-play corrections invalidate the score. They make the
candidate's performance non-representative."),

        new(
            $"{IdPrefix}note-taking-handoff",
            "Note-Taking and Feedback Hand-Off",
            InterlocutorTrainingStage.Refresher,
            8,
            RequiredForCalibration: false,
            ContentMarkdown:
@"# Note-Taking and Feedback Hand-Off

The interlocutor's notes are the marker's second-best evidence (after
the recording itself). Your scratchpad should capture concrete
observations the marker can corroborate from the transcript.

## What to capture

* **One-line behaviour observations** keyed to the criteria. Examples:
  * ""Opening (Relationship-Building): 'How are you feeling today?' —
    warm, used patient name.""
  * ""Lay-language (Information-Giving): reformulated 'hypertension'
    as 'blood pressure being too high' on first mention.""
  * ""Cue (Empathy): did not acknowledge financial concern when
    raised at 2:18.""
* **Cue resolution status.** For each cue on your card, tick whether
  the candidate addressed it.
* **Single most striking moment.** Praise or concern — the marker
  will use this as a starting point.

## What NOT to capture

* Scores. You are not the marker.
* Speculation. ""I think they might be tired"" is not actionable.
* Personal feedback to the candidate. (""Bless them, they were
  nervous."")

## Hand-off

At the end of the role-play, transfer your scratchpad to the tutor
review console. The tutor will reconcile your notes against the
transcript and the AI's flagged moments before drafting the final
feedback."),
    };
}
