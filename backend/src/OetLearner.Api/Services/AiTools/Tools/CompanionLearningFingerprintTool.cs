using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Services.AiTools.Tools;

/// <summary>
/// F-045 / F-070 — the Learning Fingerprint: how this learner actually answers
/// Reading questions, derived from their own graded answers and nothing else.
///
/// <para>
/// Five signals, each from a column that exists: confidence vs accuracy
/// (<c>ReadingAnswer.Confidence</c> joined to <c>IsCorrect</c>), answer
/// changing (<c>ReadingAnswerRevision</c> counted per question), pace
/// (<c>ReadingAnswer.ElapsedMs</c> against the learner's OWN median for the
/// part), distractor pattern (<c>SelectedDistractorCategory</c> on misses) and
/// miss classification (<c>MissReason</c>). Every comparison is within-learner:
/// their own average, their own median, their own quartiles. No external norm,
/// benchmark, "normal" change rate or pass mark exists anywhere in this file,
/// and none is invented at runtime.
/// </para>
///
/// <para>
/// SAMI §5.3 requires a knowledge gap to be separated from a confidence,
/// attention or strategy problem. That separation is reported per part as a
/// fixed vocabulary (<c>separation.*.reading</c>) with the numbers it came from
/// and a plain-English <c>meaning</c>, plus the causes the data cannot support
/// (<c>cannot_conclude</c>). Where the data is thin the tool says so instead of
/// printing a zero that would read as a measurement: every block carries its own
/// <c>can_report</c>/<c>reason</c>, the reporting floors are stated in the
/// payload, and <c>not_measured</c> names what this tool does not cover
/// (Listening/Writing/Speaking equivalents, the direction of each individual
/// change, and any claim about content).
/// </para>
/// </summary>
public sealed class CompanionLearningFingerprintTool(
    ICompanionContextResolver contexts,
    LearnerDbContext db) : IAiToolExecutor
{
    public string Code => "companion_learning_fingerprint";

    public string Description =>
        "Explain how this learner answers Reading questions from their own graded answers: accuracy per " +
        "self-rated confidence, whether changing answers helped or hurt, pace against their own median for the " +
        "part, recurring distractor types, and whether the pattern reads as a knowledge gap, a " +
        "confidence/calibration problem or a pace problem. States plainly when the data cannot support a signal.";

    public AiToolCategory Category => AiToolCategory.Read;

    public string JsonSchemaArgs => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";

    // ── Bounds and reporting conventions ─────────────────────────────────────
    //
    // The attempt cap keeps the query bounded (≈25 × 42 answer rows) and is
    // reported in the payload, because every figure describes that window and
    // not the learner's lifetime.
    private const int MaxAttemptsConsidered = 25;

    // Minimum answer counts before a STATEMENT is made. These are reporting
    // floors, not benchmarks: they exist so a "pattern" is never asserted from a
    // handful of answers, and they are echoed in the payload so the model can
    // say what would make the signal measurable. None of them is a standard of
    // good performance, and no external norm is used anywhere in this tool.
    private const int MinRatedForConfidenceStatement = 10;
    private const int MinAnswersPerGroup = 5;
    private const int MinTimedForPaceStatement = 8;

    /// <summary>
    /// How far apart two percentages must be before this tool calls them
    /// different. A within-learner reporting band: it prevents a 3-point
    /// difference being narrated as a finding. It is not a benchmark.
    /// </summary>
    private const double SeparationBandPp = 10.0;

    /// <summary>
    /// The ONE absolute reference this tool uses, and only for one question:
    /// are the answers the learner felt confident about right more often than
    /// wrong? 50 % is where a binary outcome tips — a property of the measure,
    /// not an OET pass mark, a target or a norm. It is needed because
    /// "confident and mostly wrong" is exactly the calibration case SAMI §5.3
    /// asks to be separated from a knowledge gap, and a purely relative
    /// comparison cannot see it (a learner can be uniformly low and still show
    /// no gap between their confident and unsure answers). It is stated in the
    /// payload so the model can say where the line came from.
    /// </summary>
    private const double TiePercent = 50.0;

    private const string TieBasis =
        "50 % is where a learner's answers are right more often than wrong — a property of a binary measure, not an " +
        "OET pass mark, a target or a cohort norm. It is the only absolute reference in this tool; every other " +
        "comparison is the learner against themselves.";

    private const string TimingBasis =
        "ReadingAnswer.ElapsedMs — the time between focus and save on the visit that last wrote this answer. " +
        "It is the last visit, not the sum of all visits (ReadingAnswer.TotalElapsedMs holds that sum and is not used here).";

    private const string GradedAnswersBasis =
        "ReadingAnswer rows the Reading grader set IsCorrect on. Answers held for integrity or multiple-selection " +
        "review have IsCorrect = null and are excluded, as are questions the learner never answered.";

    private static readonly (ReadingConfidence Level, string Label)[] ConfidenceLevels =
    [
        (ReadingConfidence.Guessed, "guessed"),
        (ReadingConfidence.Unsure, "unsure"),
        (ReadingConfidence.FairlySure, "fairly_sure"),
        (ReadingConfidence.Certain, "certain"),
    ];

    private static readonly (ReadingDistractorCategory Category, string Label)[] DistractorLabels =
    [
        (ReadingDistractorCategory.Opposite, "opposite"),
        (ReadingDistractorCategory.TooBroad, "too_broad"),
        (ReadingDistractorCategory.TooSpecific, "too_specific"),
        (ReadingDistractorCategory.WrongSpeaker, "wrong_speaker"),
        (ReadingDistractorCategory.NotInText, "not_in_text"),
        (ReadingDistractorCategory.DistortedDetail, "distorted_detail"),
        (ReadingDistractorCategory.OutOfScope, "out_of_scope"),
    ];

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return CompanionToolGuards.NoUser();
        var context = await contexts.ResolveAsync(ctx.UserId!, new CompanionContextEnvelope(), ct);
        if (!context.ActionsEnabled) return CompanionToolGuards.ActionsOff();
        return await BuildAsync(ctx.UserId!, ct);
    }

    private async Task<AiToolExecutionResult> BuildAsync(string userId, CancellationToken ct)
    {
        // Bounded window: the learner's most recent graded Reading attempts.
        var window = await db.ReadingAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Answers.Any(x => x.IsCorrect != null))
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new { a.Id, a.StartedAt })
            .Take(MaxAttemptsConsidered)
            .ToListAsync(ct);

        if (window.Count == 0)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_report = false,
                reason = "no_graded_reading_attempts",
                subtest_coverage = new[] { "reading" },
                how_it_gets_measured =
                    "This needs submitted, graded Reading answers. Confidence, answer-changing, pace and distractor " +
                    "patterns are all read from graded answer rows, so nothing can be said until at least one Reading " +
                    "attempt has been submitted and graded.",
                instruction =
                    "The learner has no graded Reading answers yet, so there is no fingerprint to describe. Say that " +
                    "plainly and offer a short Reading practice set or a diagnostic instead — never describe study " +
                    "behaviour that was never recorded.",
            }));
        }

        var scopedAttemptIds = window.Select(a => a.Id).ToList();

        var answerRows = await db.ReadingAnswers.AsNoTracking()
            .Where(x => scopedAttemptIds.Contains(x.ReadingAttemptId) && x.IsCorrect != null)
            .Select(x => new
            {
                x.ReadingAttemptId,
                x.ReadingQuestionId,
                x.IsCorrect,
                x.Confidence,
                x.ElapsedMs,
                x.SelectedDistractorCategory,
                x.MissReason,
                x.FlaggedForReview,
                PartCode = x.Question!.Part!.PartCode,
            })
            .ToListAsync(ct);

        if (answerRows.Count == 0)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
            {
                can_report = false,
                reason = "no_graded_reading_answers",
                subtest_coverage = new[] { "reading" },
                instruction =
                    "No graded Reading answers are in scope, so there is nothing to analyse. Say that plainly rather " +
                    "than reporting zeroes.",
            }));
        }

        // ReadingAnswerRevision has no navigation property, so the counts are
        // grouped by (attempt, question) and joined in memory.
        var revisionCounts = await db.ReadingAnswerRevisions.AsNoTracking()
            .Where(r => scopedAttemptIds.Contains(r.ReadingAttemptId))
            .GroupBy(r => new { r.ReadingAttemptId, r.ReadingQuestionId })
            .Select(g => new { g.Key.ReadingAttemptId, g.Key.ReadingQuestionId, Count = g.Count() })
            .ToListAsync(ct);

        var revisionLookup = revisionCounts.ToDictionary(
            r => (r.ReadingAttemptId, r.ReadingQuestionId),
            r => r.Count);

        // F-070 — a revision row is appended to the FIRST save as well (the
        // initial value is written through the same code path that records
        // changes), so REVISIONS = changes + 1 and "changed the answer at least
        // once" is revisionCount >= 2, not >= 1. Counting >= 1 here would have
        // reported every answered question as changed.
        var rows = answerRows
            .Select(x => new AnswerFact(
                x.ReadingAttemptId,
                x.ReadingQuestionId,
                x.PartCode,
                x.IsCorrect ?? false,
                x.Confidence,
                x.ElapsedMs,
                x.SelectedDistractorCategory,
                x.MissReason,
                x.FlaggedForReview,
                revisionLookup.TryGetValue((x.ReadingAttemptId, x.ReadingQuestionId), out var revisionCount)
                    ? revisionCount
                    : 0))
            .ToList();

        var overall = Accuracy(rows);
        var confidence = ConfidenceSplit(rows);
        var pace = PaceSplit(rows);
        var change = ChangeSplit(rows);
        var parts = rows.Select(r => r.Part).Distinct().OrderBy(p => (int)p).ToList();

        var ratedAnswers = rows.Count(r => r.Confidence is not null);
        var flaggedAnswers = rows.Where(r => r.FlaggedForReview).ToList();
        var incorrect = rows.Where(r => !r.IsCorrect).ToList();
        var taggedMisses = incorrect.Where(r => r.Distractor is not null).ToList();

        var confidenceBuckets = ConfidenceLevels
            .Select(level =>
            {
                var subset = rows.Where(r => r.Confidence == level.Level).ToList();
                var acc = Accuracy(subset);
                return new
                {
                    confidence = level.Label,
                    answers = acc.Answers,
                    correct = acc.Correct,
                    accuracy_percent = acc.AccuracyPercent,
                };
            })
            .Where(b => b.answers > 0)
            .ToList();

        var distractorCounts = DistractorLabels
            .Select(label => new
            {
                category = label.Label,
                misses = taggedMisses.Count(r => r.Distractor == label.Category),
            })
            .Where(x => x.misses > 0)
            .OrderByDescending(x => x.misses)
            .Select(x => new
            {
                x.category,
                x.misses,
                share_of_tagged_misses_percent = Share(x.misses, taggedMisses.Count),
            })
            .ToList();

        var missReasons = incorrect
            .GroupBy(r => string.IsNullOrWhiteSpace(r.MissReason) ? "unclassified" : r.MissReason!)
            .Select(g => new { reason = g.Key, misses = g.Count() })
            .OrderByDescending(x => x.misses)
            .ThenBy(x => x.reason, StringComparer.Ordinal)
            .ToList();

        var firstAttempt = window.Min(a => a.StartedAt);
        var lastAttempt = window.Max(a => a.StartedAt);

        var notMeasured = new List<object>
        {
            new
            {
                signal = "confidence_vs_accuracy (Listening, Writing, Speaking)",
                reason =
                    "Per-question confidence is captured on Reading answers only. Nothing else is instrumented, so " +
                    "nothing is reported for the other sub-tests.",
            },
            new
            {
                signal = "the direction of each individual answer change",
                reason =
                    "Revision rows store the value written, not whether that value was better: intermediate answers " +
                    "are never graded. Only the changed-vs-unchanged accuracy comparison is possible, and it is an " +
                    "association, not proof that changing caused the outcome.",
            },
            new
            {
                signal = "which topic, rule or question type a weakness is in",
                reason =
                    "This tool measures patterns across parts and answers, not content. Naming a topic would be a " +
                    "cause the data does not support; the learner's evidenced errors are the place for that " +
                    "(companion_train_mistakes).",
            },
            new
            {
                signal = "score trend over time",
                reason =
                    "Not computed here. Score movement is answered from confirmed results by " +
                    "companion_why_score_change.",
            },
        };

        if (ratedAnswers == 0)
        {
            notMeasured.Insert(0, new
            {
                signal = "confidence vs accuracy",
                reason =
                    "No graded answer in scope carries a confidence rating. The column exists and the Reading answer " +
                    "save accepts it, but no answer has been rated yet, so the comparison has no data — it is not a " +
                    "zero, it is unmeasured.",
            });
        }

        object ConfidenceBlock() => new
        {
            can_report = ratedAnswers > 0,
            reason = ratedAnswers > 0 ? null : "no_confidence_recorded",
            statement_reliable = ratedAnswers >= MinRatedForConfidenceStatement,
            rated_answers = ratedAnswers,
            unrated_answers = rows.Count - ratedAnswers,
            unrated_basis =
                "Answers with no stored rating. They are excluded from every percentage here — \"not rated\" is never " +
                "treated as a level.",
            buckets = confidenceBuckets,
            low_confidence = new
            {
                levels = new[] { "guessed", "unsure" },
                answers = confidence.LowAnswers,
                accuracy_percent = confidence.LowAccuracyPercent,
            },
            high_confidence = new
            {
                levels = new[] { "fairly_sure", "certain" },
                answers = confidence.HighAnswers,
                accuracy_percent = confidence.HighAccuracyPercent,
            },
            gap_pp = confidence.GapPp,
            gap_basis =
                "accuracy(high confidence) − accuracy(low confidence), within this learner only. A positive gap means " +
                "their self-rating separates their right answers from their wrong ones; a negative gap means it does not.",
            how_it_gets_measured = ratedAnswers > 0
                ? null
                : "It becomes measurable as soon as the learner rates confidence on the Reading answers they submit; " +
                  "historical answers stay unrated by design and cannot be reconstructed.",
            instruction = ratedAnswers == 0
                ? "No answer carries a confidence rating yet. Say exactly that — this cannot be calculated from the " +
                  "answers on file, and inventing how sure they felt would be a fabrication. Offer to have them rate " +
                  "their confidence on the next practice set."
                : "Report the bucket counts and the accuracy inside each bucket, and say how many answers had no " +
                  "rating. Do not describe an unrated answer as confident or unsure.",
        };

        object AnswerChangeBlock() => new
        {
            can_report = change.ChangedAnswers + change.UnchangedAnswers > 0,
            reason = change.ChangedAnswers + change.UnchangedAnswers > 0 ? null : "no_revision_history",
            changed_answers = change.ChangedAnswers,
            unchanged_answers = change.UnchangedAnswers,
            answers_without_revision_rows = change.AnswersWithoutRevisionRows,
            accuracy_changed_percent = change.ChangedPercent,
            accuracy_unchanged_percent = change.UnchangedPercent,
            effect_pp = change.ChangeEffectPp,
            comparison_reliable = change.StatementAllowed,
            revision_basis =
                "ReadingAnswerRevision counts every version of the answer INCLUDING the first save, so a question the " +
                "learner changed at least once has 2 or more rows and a question they left alone has 1. Answers stored " +
                "before the revision table existed have no revision rows at all: they are counted separately in " +
                "`answers_without_revision_rows` and are never treated as \"never changed\", because silence there is " +
                "missing history rather than evidence.",
            effect_basis =
                "accuracy(changed ≥ 1 time) − accuracy(never changed), within this learner only. Positive means changing " +
                "was associated with better outcomes, negative with worse. Association, not proof of cause: a hard " +
                "question attracts changes.",
            instruction = change.ChangedAnswers + change.UnchangedAnswers == 0
                ? "There is no stored answer history to compare, so say that rather than reporting zero."
                : "Report both accuracies and the difference once `comparison_reliable` is true; while it is false, say " +
                  "there are not yet enough answers on one side of the comparison to judge it. Never present the " +
                  "difference as proof that changing answers caused the outcome.",
        };

        object PaceBlock() => new
        {
            can_report = pace.TimedAnswers > 0,
            reason = pace.TimedAnswers > 0 ? null : "no_timing_captured",
            timing_basis = TimingBasis,
            median_basis =
                "The learner's OWN median time for that part. There is no absolute fast/slow threshold anywhere in this " +
                "tool: the comparison is their fastest answers against their slowest ones.",
            overall = new
            {
                timed_answers = pace.TimedAnswers,
                median_ms = pace.MedianMs,
                quartile_bucket_size = pace.BucketSize,
                fastest_quartile_accuracy_percent = pace.FastestPercent,
                fastest_quartile_answers = pace.FastestAnswers,
                slowest_quartile_accuracy_percent = pace.SlowestPercent,
                slowest_quartile_answers = pace.SlowestAnswers,
                pace_gap_pp = pace.PaceGapPp,
                statement_reliable = pace.StatementAllowed,
            },
            by_part = parts.Select(p =>
            {
                var partPace = PaceSplit(rows.Where(r => r.Part == p).ToList());
                return new
                {
                    part = p.ToString(),
                    timed_answers = partPace.TimedAnswers,
                    median_ms = partPace.MedianMs,
                    quartile_bucket_size = partPace.BucketSize,
                    fastest_quartile_accuracy_percent = partPace.FastestPercent,
                    fastest_quartile_answers = partPace.FastestAnswers,
                    slowest_quartile_accuracy_percent = partPace.SlowestPercent,
                    slowest_quartile_answers = partPace.SlowestAnswers,
                    pace_gap_pp = partPace.PaceGapPp,
                    statement_reliable = partPace.StatementAllowed,
                };
            }).ToList(),
            gap_basis =
                "accuracy(slowest quartile) − accuracy(fastest quartile), within this learner only. Quartiles are taken " +
                "from that learner's own timed answers for the part, so the buckets are equal-sized counts, not clock " +
                "thresholds.",
            instruction = pace.TimedAnswers == 0
                ? "No timing was captured for these answers, so pace cannot be described. Say that plainly."
                : "State the median you used and the accuracy in the fastest and slowest quartiles. Only draw a pace " +
                  "conclusion where `statement_reliable` is true; otherwise say the sample is too small to tell.",
        };

        object DistractorBlock() => new
        {
            can_report = incorrect.Count > 0,
            reason = incorrect.Count > 0 ? null : "no_incorrect_answers",
            incorrect_answers = incorrect.Count,
            tagged_misses = taggedMisses.Count,
            untagged_misses = incorrect.Count - taggedMisses.Count,
            by_category = distractorCounts,
            by_miss_reason = missReasons,
            basis =
                "A distractor category is recorded only when the wrong option the learner picked carried authoring " +
                "metadata, so untagged misses are counted but not attributed. Miss reasons are the grader's own " +
                "classification of the same misses (blank / spelling / number_form / distractor / wrong_text / " +
                "incomplete / wrong).",
            instruction = taggedMisses.Count == 0
                ? "None of the missed options carried distractor metadata, so no distractor pattern can be named. " +
                  "Report the miss reasons instead, and do not invent a distractor type."
                : "Name the distractor types that recur, with how many misses each accounts for. Say what share of " +
                  "the TAGGED misses that is, never of all misses, and do not treat the untagged ones as a pattern.",
        };

        object ReviewFlagBlock() => new
        {
            can_report = flaggedAnswers.Count > 0,
            reason = flaggedAnswers.Count > 0 ? null : "no_flagged_answers",
            flagged_answers = flaggedAnswers.Count,
            accuracy_among_flagged_percent = Share(flaggedAnswers.Count(r => r.IsCorrect), flaggedAnswers.Count),
            basis = "Answers the learner themselves marked to revisit on review (ReadingAnswer.FlaggedForReview).",
        };

        return new AiToolExecutionResult(AiToolOutcome.Success, CompanionToolGuards.Json(new
        {
            can_report = true,
            reason = (string?)null,
            scope = new
            {
                subtest = "reading",
                attempts_considered = window.Count,
                attempts_window = $"the learner's most recent {MaxAttemptsConsidered} graded Reading attempts",
                window_from = firstAttempt.ToString("yyyy-MM-dd"),
                window_to = lastAttempt.ToString("yyyy-MM-dd"),
                graded_answers = rows.Count,
                graded_answers_basis = GradedAnswersBasis,
                learner_accuracy_percent = overall.AccuracyPercent,
                rated_answers = ratedAnswers,
            },
            confidence_vs_accuracy = ConfidenceBlock(),
            answer_changing = AnswerChangeBlock(),
            pace = PaceBlock(),
            distractor_pattern = DistractorBlock(),
            review_flags = ReviewFlagBlock(),
            separation = new
            {
                requirement =
                    "SAMI §5.3 — separate a knowledge gap from speed, confidence, attention and strategy problems. The " +
                    "verdict below is a within-learner comparison, never a comparison with other learners or a norm.",
                band_pp = SeparationBandPp,
                band_basis =
                    $"Two percentages closer than {SeparationBandPp:0} points are not called a difference anywhere in " +
                    "this tool. The band is a reporting convention, not a benchmark.",
                tie_reference_percent = TiePercent,
                tie_reference_basis = TieBasis,
                reference_accuracy_percent = overall.AccuracyPercent,
                reference_basis =
                    "The learner's own accuracy across every graded answer in scope. A part is only called weak " +
                    "relative to THEM, never against a pass mark or a cohort.",
                overall = SeparationScope("overall", rows, overall.AccuracyPercent),
                by_part = parts
                    .Select(p => SeparationScope(p.ToString(), rows.Where(r => r.Part == p).ToList(), overall.AccuracyPercent))
                    .ToList(),
            },
            reporting_floors = new
            {
                max_attempts_considered = MaxAttemptsConsidered,
                min_rated_answers_for_confidence_statement = MinRatedForConfidenceStatement,
                min_answers_per_group_for_comparison = MinAnswersPerGroup,
                min_timed_answers_for_pace_statement = MinTimedForPaceStatement,
                separation_band_pp = SeparationBandPp,
                basis =
                    "Floors exist so a pattern is never asserted from a handful of answers, and they are reported so " +
                    "the learner can be told what would make the signal measurable. They are not benchmarks of good " +
                    "performance and no external norm is used anywhere in this tool.",
            },
            not_measured = notMeasured,
            instruction = BuildInstruction(ratedAnswers, change.ChangedAnswers + change.UnchangedAnswers, pace.TimedAnswers),
        }));
    }

    private static object SeparationScope(string scope, IReadOnlyList<AnswerFact> rows, double? referenceAccuracyPercent)
    {
        var acc = Accuracy(rows);
        var confidence = ConfidenceSplit(rows);
        var pace = PaceSplit(rows);
        var change = ChangeSplit(rows);

        var flags = ClassifyFlags(
            acc.AccuracyPercent,
            referenceAccuracyPercent,
            confidence,
            pace.PaceGapPp,
            pace.StatementAllowed,
            change.ChangeEffectPp,
            change.ChangedAnswers,
            change.UnchangedAnswers);

        var reading = ReadPattern(flags, acc.Answers);

        return new
        {
            scope,
            graded_answers = acc.Answers,
            accuracy_percent = acc.AccuracyPercent,
            vs_learner_average_pp = acc.AccuracyPercent is double ap && referenceAccuracyPercent is double rp
                ? Math.Round(ap - rp, 1)
                : (double?)null,
            confidence_gap_pp = confidence.GapPp,
            pace_gap_pp = pace.StatementAllowed ? pace.PaceGapPp : null,
            change_effect_pp = change.StatementAllowed ? change.ChangeEffectPp : null,
            flags = new
            {
                overconfident = flags.Overconfident,
                knowledge_gap_consistent = flags.KnowledgeGapConsistent,
                rushed_or_pace_problem = flags.Rushed,
                confidence_tracks_accuracy = flags.ConfidenceTracksAccuracy,
                confidence_not_costly = flags.ConfidenceNotCostly,
                answer_changes_cost_marks = flags.ChangesCostMarks,
                confidence_comparison_judgeable = flags.ConfidenceComparisonJudgeable,
            },
            reading,
            meaning = MeaningOf(reading),
            cannot_conclude = CannotConclude(flags),
        };
    }

    /// <summary>
    /// Plain-English meaning of each verdict, emitted with the verdict itself so
    /// the model cannot re-interpret it into a stronger claim.
    /// </summary>
    private static string MeaningOf(string reading) => reading switch
    {
        "overconfident" =>
            "Their confident answers are not more accurate than their unsure ones — or they are confident while being " +
            "right no more often than wrong — so their confidence is not tracking whether they are right. That is the " +
            "§5.3 calibration case, not evidence of a knowledge gap. Do not tell them they know the material, and do " +
            "not read their 'certain' answers as evidence of mastery.",
        "knowledge_gap_consistent" =>
            "This part sits at least the band below their own average AND their confidence tracks their accuracy, so " +
            "the misses line up with where they feel unsure — consistent with a knowledge or practice gap here. Name " +
            "the PART only: this tool did not measure content, so never name a topic, rule or question type.",
        "rushed_or_pace_problem" =>
            "Their slowest-quartile answers are at least the band more accurate than their fastest-quartile ones, so " +
            "errors concentrate in the answers they made fastest — a pace or strategy signal. It does not prove the " +
            "knowledge is there; report only what the numbers show.",
        "confidence_tracks_accuracy" =>
            "Their confidence tracks their accuracy, so the self-ratings carry real information. No confidence or " +
            "pace signal crossed the reporting band in this part.",
        "confidence_not_costly" =>
            "Their low-confidence answers are about as accurate as their confident ones, and the confident ones are " +
            "right more often than wrong, so the uncertainty is not yet costing marks: that reads as a confidence " +
            "problem rather than a knowledge gap. You cannot tell how they felt — only how they scored — so do not " +
            "claim they lack knowledge, and do not claim they are anxious.",
        "no_signal_yet" =>
            "No signal crossed the reporting band: accuracy, confidence, pace and answer-changing are all within this " +
            "learner's own typical range here. Say there is nothing to flag yet rather than inventing a weakness.",
        _ =>
            "There is not enough graded and rated data in this part to separate a knowledge gap from a confidence or " +
            "pace problem. Say that plainly, say what would make it measurable (rating confidence on each answer, and " +
            "answering more questions), and do not guess at a cause.",
    };

    private static List<string> CannotConclude(FlagSet flags)
    {
        var cannot = new List<string>
        {
            "whether a miss was a gap in a specific topic, rule or skill — this tool reads no content",
            "why the learner answered the way they did (attention, fatigue, time pressure or teaching are not recorded)",
        };

        if (!flags.Overconfident && !flags.ConfidenceNotCostly)
        {
            // The confidence comparison only speaks when it produced a verdict of
            // its own; otherwise what it could NOT settle is stated here.
            cannot.Add(flags.ConfidenceComparisonJudgeable
                ? "whether their uncertainty is real: their confidence ratings do not separate their right answers " +
                  "from their wrong ones in this data"
                : $"whether their confidence is calibrated at all: fewer than {MinAnswersPerGroup} answers carry a " +
                  $"low confidence rating, or fewer than {MinAnswersPerGroup} carry a high one, so the two cannot be " +
                  "compared");
        }

        if (!flags.Rushed)
        {
            cannot.Add("whether pace is a factor — no quartile difference crossed the band here");
        }

        return cannot;
    }

    private static string ReadPattern(FlagSet flags, int answers)
    {
        if (answers == 0) return "insufficient_data";
        if (flags.Overconfident) return "overconfident";
        if (flags.KnowledgeGapConsistent) return "knowledge_gap_consistent";
        if (flags.Rushed) return "rushed_or_pace_problem";
        if (flags.ConfidenceTracksAccuracy) return "confidence_tracks_accuracy";
        if (flags.ConfidenceNotCostly) return "confidence_not_costly";
        if (!flags.ConfidenceComparisonJudgeable) return "insufficient_data";
        return "no_signal_yet";
    }

    private static FlagSet ClassifyFlags(
        double? accuracyPercent,
        double? referenceAccuracyPercent,
        ConfidenceSplitResult confidence,
        double? paceGapPp,
        bool paceAllowed,
        double? changeEffectPp,
        int changedAnswers,
        int unchangedAnswers)
    {
        // The confidence comparison needs a real group on each side; below that
        // it is reported as unmeasured rather than as a small difference.
        var judgeable = confidence.LowAnswers >= MinAnswersPerGroup
                        && confidence.HighAnswers >= MinAnswersPerGroup;

        var gap = confidence.GapPp;
        var highAccuracy = confidence.HighAccuracyPercent;

        // Confident and not more right: either their confident answers do worse
        // than their unsure ones, or they are confident while being right no more
        // often than wrong (the §5.3 "accuracy low and confidence high" case,
        // which a relative comparison alone cannot see).
        var overconfident = judgeable
                            && ((gap is double g && g <= -SeparationBandPp)
                                || (highAccuracy is double ha && ha <= TiePercent));

        var tracks = judgeable && gap is double g2 && g2 >= SeparationBandPp;

        // Uncertainty that is not costing marks: their confident and unsure
        // answers are equally accurate, and the confident ones are right more
        // often than wrong. This is the §5.3 "accuracy fine but confidence low"
        // case, stated relatively.
        var confidenceNotCostly = judgeable
                                  && gap is double g3
                                  && Math.Abs(g3) < SeparationBandPp
                                  && highAccuracy is double ha2
                                  && ha2 > TiePercent;

        var relativeBehind = accuracyPercent is double ap && referenceAccuracyPercent is double rp
            ? rp - ap
            : (double?)null;
        var knowledgeGap = tracks && relativeBehind is double behind && behind >= SeparationBandPp;
        var rushed = paceAllowed && paceGapPp is double pg && pg >= SeparationBandPp;
        var changesCost = changedAnswers >= MinAnswersPerGroup
                          && unchangedAnswers >= MinAnswersPerGroup
                          && changeEffectPp is double ce
                          && ce <= -SeparationBandPp;

        return new FlagSet(
            overconfident,
            tracks,
            knowledgeGap,
            rushed,
            confidenceNotCostly,
            changesCost,
            judgeable);
    }

    private static string BuildInstruction(int ratedAnswers, int comparableAnswers, int timedAnswers)
    {
        var parts = new List<string>
        {
            "Describe the learner's OWN pattern from these numbers and nothing else. Every comparison here is " +
            "within-learner — their median, their buckets, their average. Never compare them with another learner, a " +
            "cohort, a pass mark or a norm, and never state a threshold this payload does not contain.",
            "Lead with `separation`: its `reading` and `meaning` say which of a knowledge gap, a confidence/calibration " +
            "problem or a pace/strategy problem their own data supports, and `cannot_conclude` lists what it does not.",
        };

        parts.Add(ratedAnswers == 0
            ? "No answer carries a confidence rating, so say the confidence comparison is unmeasured rather than zero, " +
              "and tell them it becomes measurable when they rate their confidence on the next set."
            : "Report the confidence buckets with their counts, and say how many answers were unrated.");

        parts.Add(comparableAnswers == 0
            ? "There is no answer-change history to compare — say so."
            : "For answer changing, report the association and say plainly that it is not proof of cause.");

        parts.Add(timedAnswers == 0
            ? "No timing was captured, so do not describe pace at all."
            : "State the median you used before saying anything about pace, and respect `statement_reliable`.");

        parts.Add(
            "If the learner asks about a sub-test other than Reading, or about a topic this payload does not contain, " +
            "say it is not measured here and point at the tool that can answer it (companion_train_mistakes for " +
            "evidenced errors, companion_why_score_change for score movement).");

        parts.Add(
            "The only absolute reference in this payload is `separation.tie_reference_percent` (50 % — right more often " +
            "than wrong). Never present it, the reporting band, or any reporting floor as a pass mark, a target or an " +
            "official requirement.");

        return string.Join(" ", parts);
    }

    private static (int Answers, int Correct, double? AccuracyPercent) Accuracy(IReadOnlyList<AnswerFact> rows)
    {
        var correct = rows.Count(r => r.IsCorrect);
        return (rows.Count, correct, Share(correct, rows.Count));
    }

    private static ConfidenceSplitResult ConfidenceSplit(IReadOnlyList<AnswerFact> rows)
    {
        var rated = rows.Where(r => r.Confidence is not null).ToList();
        var low = rated.Where(r => r.Confidence is ReadingConfidence.Guessed or ReadingConfidence.Unsure).ToList();
        var high = rated.Where(r => r.Confidence is ReadingConfidence.FairlySure or ReadingConfidence.Certain).ToList();
        var lowAccuracy = Accuracy(low).AccuracyPercent;
        var highAccuracy = Accuracy(high).AccuracyPercent;

        return new ConfidenceSplitResult(
            rated.Count,
            low.Count,
            lowAccuracy,
            high.Count,
            highAccuracy,
            lowAccuracy is double lp && highAccuracy is double hp ? Math.Round(hp - lp, 1) : null);
    }

    private static PaceSplitResult PaceSplit(IReadOnlyList<AnswerFact> rows)
    {
        var timed = rows
            .Where(r => r.ElapsedMs is int ms && ms > 0)
            .OrderBy(r => r.ElapsedMs!.Value)
            .ToList();

        if (timed.Count == 0)
        {
            return new PaceSplitResult(0, null, 0, 0, null, 0, null, null, false);
        }

        // Equal-sized buckets taken from the learner's own ordering, so the
        // "fastest quartile" is a count of their fastest answers rather than a
        // threshold someone chose. Bucket size is reported because a small
        // sample makes the buckets bigger than a true quartile.
        var bucketSize = Math.Max(1, timed.Count / 4);
        var fastest = timed.Take(bucketSize).ToList();
        var slowest = timed.Skip(timed.Count - bucketSize).ToList();
        var fastestAccuracy = Accuracy(fastest).AccuracyPercent;
        var slowestAccuracy = Accuracy(slowest).AccuracyPercent;
        var median = Median(timed.Select(r => r.ElapsedMs!.Value).ToList());

        return new PaceSplitResult(
            timed.Count,
            Math.Round(median, 0, MidpointRounding.AwayFromZero),
            bucketSize,
            fastest.Count,
            fastestAccuracy,
            slowest.Count,
            slowestAccuracy,
            fastestAccuracy is double fp && slowestAccuracy is double sp ? Math.Round(sp - fp, 1) : null,
            timed.Count >= MinTimedForPaceStatement);
    }

    private static ChangeSplitResult ChangeSplit(IReadOnlyList<AnswerFact> rows)
    {
        // See the note in BuildAsync: revision rows include the first save.
        var changed = rows.Where(r => r.RevisionCount >= 2).ToList();
        var unchanged = rows.Where(r => r.RevisionCount == 1).ToList();
        var withoutRevisions = rows.Count(r => r.RevisionCount == 0);
        var changedAccuracy = Accuracy(changed).AccuracyPercent;
        var unchangedAccuracy = Accuracy(unchanged).AccuracyPercent;

        return new ChangeSplitResult(
            changed.Count,
            changedAccuracy,
            unchanged.Count,
            unchangedAccuracy,
            withoutRevisions,
            changedAccuracy is double cp && unchangedAccuracy is double up ? Math.Round(cp - up, 1) : null,
            changed.Count >= MinAnswersPerGroup && unchanged.Count >= MinAnswersPerGroup);
    }

    private static double Median(IReadOnlyList<int> orderedAscending)
    {
        var mid = orderedAscending.Count / 2;
        return orderedAscending.Count % 2 == 1
            ? orderedAscending[mid]
            : (orderedAscending[mid - 1] + orderedAscending[mid]) / 2.0;
    }

    /// <summary>
    /// part / whole as a percentage, or null when there is no whole — so an
    /// empty group reports "no data" rather than an accuracy of 0 %.
    /// </summary>
    private static double? Share(int part, int whole) =>
        whole > 0 ? Math.Round(100.0 * part / whole, 1, MidpointRounding.AwayFromZero) : null;

    private sealed record AnswerFact(
        string AttemptId,
        string QuestionId,
        ReadingPartCode Part,
        bool IsCorrect,
        ReadingConfidence? Confidence,
        int? ElapsedMs,
        ReadingDistractorCategory? Distractor,
        string? MissReason,
        bool FlaggedForReview,
        int RevisionCount);

    private sealed record ConfidenceSplitResult(
        int RatedAnswers,
        int LowAnswers,
        double? LowAccuracyPercent,
        int HighAnswers,
        double? HighAccuracyPercent,
        double? GapPp);

    private sealed record PaceSplitResult(
        int TimedAnswers,
        double? MedianMs,
        int BucketSize,
        int FastestAnswers,
        double? FastestPercent,
        int SlowestAnswers,
        double? SlowestPercent,
        double? PaceGapPp,
        bool StatementAllowed);

    private sealed record ChangeSplitResult(
        int ChangedAnswers,
        double? ChangedPercent,
        int UnchangedAnswers,
        double? UnchangedPercent,
        int AnswersWithoutRevisionRows,
        double? ChangeEffectPp,
        bool StatementAllowed);

    private sealed record FlagSet(
        bool Overconfident,
        bool ConfidenceTracksAccuracy,
        bool KnowledgeGapConsistent,
        bool Rushed,
        bool ConfidenceNotCostly,
        bool ChangesCostMarks,
        bool ConfidenceComparisonJudgeable);
}
