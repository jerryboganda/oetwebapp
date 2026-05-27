using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

// Phase 6 (16-stage pathway, plan section 16) of the OET Speaking module
// roadmap.
//
// SpeakingCoursePathwayService surfaces the 16-stage learning journey
// that takes a learner from "what does the speaking sub-test look like?"
// through to "two-role-play exam simulation".
//
// TODO(LearningPath): the canonical plan in B.6 references
// `LearningPath` + `LearningPathStage` entities. Those rows do not yet
// exist as DB tables in this codebase — `EnsurePathwaySeededAsync` is a
// no-op today and the 16 stages are returned from an in-memory canonical
// catalogue. Once the LearningPath schema lands, this service should be
// migrated to persist its rows.
public sealed class SpeakingCoursePathwayService(LearnerDbContext db)
{
    public const string PathwayCode = "speaking-foundations-16";

    public static readonly IReadOnlyList<SpeakingPathwayStage> CanonicalStages = new SpeakingPathwayStage[]
    {
        new("intro-to-speaking-format", 1, "Intro to the Speaking Format",
            "Walk through the structure of the OET Speaking sub-test — the warm-up, the two role-plays, and the time you have to prep.",
            SpeakingPathwayActivityKind.OrientationVideo),
        new("understanding-roleplay-card", 2, "Understanding the Role-Play Card",
            "Decode each section of a role-play card: setting, task, patient profile, and the four to five cues you must surface.",
            SpeakingPathwayActivityKind.GuidedReading),
        new("using-3min-prep", 3, "Using the 3-Minute Prep",
            "A repeatable prep routine: read twice, annotate, plan an opener, and rehearse one transition.",
            SpeakingPathwayActivityKind.Drill),
        new("opening-naturally", 4, "Opening Naturally",
            "Practise warm, profession-appropriate openings that establish rapport without sounding rehearsed.",
            SpeakingPathwayActivityKind.Drill),
        new("building-rapport", 5, "Building Rapport",
            "Acknowledge concerns, mirror language, and use small empathetic moves that lift Relationship-Building band scores.",
            SpeakingPathwayActivityKind.Drill),
        new("open-and-closed-questions", 6, "Open and Closed Questions",
            "Choose the right question shape for the right moment — funnel from open to closed when gathering information.",
            SpeakingPathwayActivityKind.Drill),
        new("exploring-ice", 7, "Exploring ICE (Ideas, Concerns, Expectations)",
            "Probe ICE explicitly so Information-Gathering and Patient-Perspective both land in band-3 territory.",
            SpeakingPathwayActivityKind.Drill),
        new("explaining-medical-info-simply", 8, "Explaining Medical Info Simply",
            "Lay-language reformulation drills: turn jargon into something a worried family member would actually understand.",
            SpeakingPathwayActivityKind.Drill),
        new("signposting-and-organising", 9, "Signposting and Organising",
            "Use connectors and transition phrases so the role-play has a clear arc — Structure scores reward this directly.",
            SpeakingPathwayActivityKind.Drill),
        new("checking-understanding", 10, "Checking Understanding",
            "Teach-back, summary checks, and confirmation moves that improve Information-Giving and reduce miscommunication risk.",
            SpeakingPathwayActivityKind.Drill),
        new("handling-angry-anxious-patients", 11, "Handling Angry / Anxious Patients",
            "Recovery moves for high-resistance role-plays — name the emotion, validate, then move forward together.",
            SpeakingPathwayActivityKind.RolePlay),
        new("managing-time-in-5-min", 12, "Managing Time in 5 Minutes",
            "Pace the four phases (open, gather, explain, close) so you finish without rushing in the final 30 seconds.",
            SpeakingPathwayActivityKind.RolePlay),
        new("profession-specific-roleplay", 13, "Profession-Specific Role-Play",
            "A scenario drawn from your profession (Nursing, Medicine, etc.) at a realistic exam difficulty.",
            SpeakingPathwayActivityKind.RolePlay),
        new("recorded-mock-test-1", 14, "Recorded Mock Test #1",
            "Sit a full two-role-play mock with recording. AI scores all nine criteria and flags rulebook violations.",
            SpeakingPathwayActivityKind.Mock),
        new("feedback-and-drills", 15, "Feedback and Drills",
            "Targeted drills derived from your mock — typically Empathy + Lay-Language + Signposting for most learners.",
            SpeakingPathwayActivityKind.Drill),
        new("final-two-roleplay-sim", 16, "Final Two-Role-Play Simulation",
            "Final exam-style simulation. If you hit Borderline-or-better here, you are ready to book a real OET attempt.",
            SpeakingPathwayActivityKind.Mock),
    };

    /// <summary>
    /// Idempotent seed entrypoint. Today this is a no-op because the
    /// `LearningPath` schema is not yet present; the canonical 16-stage
    /// catalogue is held in-memory. Once the schema lands the
    /// implementation should:
    ///   1. Look for `LearningPath { Code = "speaking-foundations-16" }`.
    ///   2. Create the parent row + 16 child stages if missing.
    /// </summary>
    public Task EnsurePathwaySeededAsync(CancellationToken ct)
    {
        // TODO(LearningPath): persist `LearningPath` + `LearningPathStage`
        // rows once the schema is wired through `LearnerDbContext`.
        return Task.CompletedTask;
    }

    public async Task<object> GetForLearnerAsync(string userId, CancellationToken ct)
    {
        // Seed is a no-op today, but we still call it so the contract is
        // future-proof.
        await EnsurePathwaySeededAsync(ct);

        // Pull learner's recent activity so we can derive stage state.
        var sessions = await db.SpeakingSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var drills = await db.SpeakingDrillAttempts
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.StartedAt)
            .ToListAsync(ct);

        var completedSessionCount = sessions.Count(s => s.State == SpeakingSessionState.Finished);
        var completedDrillCount = drills.Count(d => d.CompletedAt.HasValue);
        var mockSessionCount = sessions.Count(s =>
            s.State == SpeakingSessionState.Finished
            && (!string.IsNullOrEmpty(s.MockSetId) || !string.IsNullOrEmpty(s.MockSessionId)));

        // State derivation strategy:
        //   - Orientation / Reading stages (1, 2): completed as soon as the
        //     learner has at least one session.
        //   - Drill stages (3..10, 15): each completed drill unlocks the
        //     next stage in sequence.
        //   - Role-play stages (11..13): completed by N finished sessions.
        //   - Mock stages (14, 16): completed by finished mock sessions.
        //
        // A stage is "in_progress" when it is the first un-completed stage;
        // every later stage is "locked" until the previous one completes.

        var states = new List<string>(new string[CanonicalStages.Count]);
        for (var i = 0; i < CanonicalStages.Count; i++)
        {
            var stage = CanonicalStages[i];
            var isComplete = stage.OrderIndex switch
            {
                1 or 2 => sessions.Count > 0,
                3 => completedDrillCount >= 1,
                4 => completedDrillCount >= 2,
                5 => completedDrillCount >= 3,
                6 => completedDrillCount >= 4,
                7 => completedDrillCount >= 5,
                8 => completedDrillCount >= 6,
                9 => completedDrillCount >= 7,
                10 => completedDrillCount >= 8,
                11 => completedSessionCount >= 2,
                12 => completedSessionCount >= 3,
                13 => completedSessionCount >= 4,
                14 => mockSessionCount >= 1,
                15 => completedDrillCount >= 10,
                16 => mockSessionCount >= 2,
                _ => false,
            };
            states[i] = isComplete ? "completed" : "locked";
        }

        // Flip the first non-completed stage to in_progress (if any).
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i] == "locked")
            {
                states[i] = "in_progress";
                break;
            }
        }

        var enrichedStages = CanonicalStages.Select((s, idx) => new
        {
            code = s.Code,
            orderIndex = s.OrderIndex,
            title = s.Title,
            description = s.Description,
            activityKind = s.ActivityKind.ToString(),
            state = states[idx],
            actionHref = StageActionHref(s),
            actionLabel = StageActionLabel(s.ActivityKind),
        }).ToArray();

        var nextStage = enrichedStages.FirstOrDefault(s => s.state == "in_progress");
        var completedStageCount = states.Count(s => s == "completed");

        return new
        {
            pathwayCode = PathwayCode,
            title = "OET Speaking Foundations",
            totalStages = CanonicalStages.Count,
            completedStageCount,
            progressPercent = Math.Round(completedStageCount * 100.0 / CanonicalStages.Count, 1),
            stages = enrichedStages,
            nextStage,
        };
    }

    public sealed record SpeakingPathwayStage(
        string Code,
        int OrderIndex,
        string Title,
        string Description,
        SpeakingPathwayActivityKind ActivityKind);

    public enum SpeakingPathwayActivityKind
    {
        OrientationVideo = 0,
        GuidedReading = 1,
        Drill = 2,
        RolePlay = 3,
        Mock = 4,
    }

    private static string StageActionHref(SpeakingPathwayStage stage) => stage.ActivityKind switch
    {
        SpeakingPathwayActivityKind.OrientationVideo => "/speaking/rulebook",
        SpeakingPathwayActivityKind.GuidedReading => "/speaking/rulebook",
        SpeakingPathwayActivityKind.Drill => "/speaking/drills",
        SpeakingPathwayActivityKind.RolePlay => "/speaking/roleplay",
        SpeakingPathwayActivityKind.Mock => "/speaking/mocks",
        _ => "/speaking",
    };

    private static string StageActionLabel(SpeakingPathwayActivityKind kind) => kind switch
    {
        SpeakingPathwayActivityKind.OrientationVideo => "Open guide",
        SpeakingPathwayActivityKind.GuidedReading => "Open role-play guide",
        SpeakingPathwayActivityKind.Drill => "Practise drills",
        SpeakingPathwayActivityKind.RolePlay => "Start role-play",
        SpeakingPathwayActivityKind.Mock => "Open mock sets",
        _ => "Continue",
    };
}
