using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;

namespace OetLearner.Api.Services.Speaking;

public sealed record LiveVoiceContentReadiness(
    InterlocutorScript Script,
    bool Generated,
    bool NeedsOwnerInput,
    string Provenance,
    string JevValidationStatus);

public sealed class LiveVoiceContentReadinessService(
    LearnerDbContext db,
    ITypeSafeJudgmentService? jev,
    ILogger<LiveVoiceContentReadinessService> logger)
{
    private const string ProjectionOrigin = "live_voice_projection";
    private const string ProjectionGenerator = "server_visible_projection";
    private const string ProjectionGeneratorModel = "deterministic-visible-card-v1";
    private const string ProjectionProvenance = "role_play_card_visible_fields_projection";

    public async Task<LiveVoiceContentReadiness> PrepareAsync(
        RolePlayCard card,
        InterlocutorScript? script,
        CancellationToken ct)
    {
        if (TryResolveExisting(card, script) is { } existing)
        {
            return existing;
        }

        var sourceDigest = ComputeSourceDigest(card);
        var generated = BuildProjection(card);
        var validationStatus = await ValidateProjectionAsync(card, generated, ct);
        var now = DateTimeOffset.UtcNow;
        var persisted = script ?? new InterlocutorScript
        {
            Id = $"live-generated-{Guid.NewGuid():N}",
            RolePlayCardId = card.Id,
            CreatedAt = now,
        };

        persisted.PatientBackground = generated.PatientBackground;
        persisted.PatientTasks = generated.PatientTasks;
        persisted.OpeningResponse = generated.OpeningResponse;
        persisted.Prompt1 = generated.Prompt1;
        persisted.Prompt2 = generated.Prompt2;
        persisted.Prompt3 = generated.Prompt3;
        persisted.HiddenInformation = generated.HiddenInformation;
        persisted.ResistanceLevel = generated.ResistanceLevel;
        persisted.ClosingCue = generated.ClosingCue;
        persisted.EmotionalState = generated.EmotionalState;
        persisted.ProfessionRoleNotes = generated.ProfessionRoleNotes;
        persisted.LayLanguageTriggersJson = generated.LayLanguageTriggersJson;
        persisted.AllowsSecondVisit = generated.AllowsSecondVisit;
        persisted.SecondVisitIndicator = generated.SecondVisitIndicator;
        persisted.SecondVisitCarryFactsJson = generated.SecondVisitCarryFactsJson;
        persisted.ContentOrigin = ProjectionOrigin;
        persisted.SourceDigest = sourceDigest;
        persisted.Generator = ProjectionGenerator;
        persisted.GeneratorModel = ProjectionGeneratorModel;
        persisted.JevValidationStatus = validationStatus;
        persisted.NeedsOwnerInput = validationStatus != "jev_validated";
        persisted.GeneratedAt ??= now;
        persisted.UpdatedAt = now;

        if (script is null)
        {
            db.InterlocutorScripts.Add(persisted);
        }
        else
        {
            db.InterlocutorScripts.Attach(persisted);
            db.Entry(persisted).State = EntityState.Modified;
        }

        await db.SaveChangesAsync(ct);
        return ProjectGenerated(persisted);
    }

    /// <summary>
    /// Read-only half of <see cref="PrepareAsync"/>: the readiness of an authored
    /// script, or of a still-current validated projection. Null when a projection
    /// would have to be (re)generated, which writes and may call Jev, so the
    /// $0 corpus harness uses this and never generates.
    /// </summary>
    internal static LiveVoiceContentReadiness? TryResolveExisting(RolePlayCard card, InterlocutorScript? script)
    {
        if (script is null)
        {
            return null;
        }

        if (!string.Equals(script.ContentOrigin, ProjectionOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return new LiveVoiceContentReadiness(
                Script: script,
                Generated: false,
                NeedsOwnerInput: script.NeedsOwnerInput,
                Provenance: "authored_interlocutor_script",
                JevValidationStatus: script.JevValidationStatus ?? "not_required");
        }

        return string.Equals(script.SourceDigest, ComputeSourceDigest(card), StringComparison.Ordinal)
            && !script.NeedsOwnerInput
            && string.Equals(script.JevValidationStatus, "jev_validated", StringComparison.Ordinal)
                ? ProjectGenerated(script)
                : null;
    }

    private async Task<string> ValidateProjectionAsync(
        RolePlayCard card,
        InterlocutorScript generated,
        CancellationToken ct)
    {
        var validationStatus = "jev_unavailable";
        if (jev is null)
        {
            return validationStatus;
        }

        using var validationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        validationTimeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            var result = await jev.AskAsync(new JevJudgmentRequest
            {
                StateJson = JsonSerializer.SerializeToElement(new
                {
                    visibleCard = new
                    {
                        card.ScenarioTitle,
                        card.Setting,
                        card.Background,
                        card.ClinicalTopic,
                        card.PatientEmotion,
                        tasks = card.Tasks,
                    },
                    generatedProjection = new
                    {
                        generated.OpeningResponse,
                        generated.Prompt1,
                        generated.Prompt2,
                        generated.Prompt3,
                        generated.HiddenInformation,
                        generated.PatientBackground,
                        patientTasks = generated.PatientTasks,
                    },
                }),
                Questions =
                [
                    new JevQuestion
                    {
                        Id = "jev_live_voice_projection_grounded",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "Does the generatedProjection introduce a specific clinical or patient fact that is absent from visibleCard? Generic conversational prompts are not factual claims.",
                        NoulCriteria = new Dictionary<string, string?>
                        {
                            ["true"] = "The projection contains no unsupported clinical or patient-specific fact.",
                            ["false"] = "The projection asserts a clinical or patient-specific fact not present in the visible card.",
                        },
                    },
                    new JevQuestion
                    {
                        Id = "jev_live_voice_projection_consistent",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "Does the generatedProjection avoid contradicting the visibleCard?",
                        NoulCriteria = new Dictionary<string, string?>
                        {
                            ["true"] = "Nothing in the projection contradicts the visible card.",
                            ["false"] = "The projection contradicts a visible card fact.",
                        },
                    },
                ],
            }, new JevCallMetadata
            {
                FeatureCode = AiFeatureCodes.JevConversationTurn,
                ResourceId = card.Id,
                ResourceType = "live_voice_content_projection",
            }, validationTimeout.Token);

            return result.Status switch
            {
                JevCallStatus.Ok when IsPositive(result, "jev_live_voice_projection_grounded")
                    && IsPositive(result, "jev_live_voice_projection_consistent") => "jev_validated",
                JevCallStatus.Ok => "jev_owner_input_required",
                JevCallStatus.Disabled => "jev_disabled",
                _ => "jev_unavailable",
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "jev_pending";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Jev projection validation failed for Speaking card {CardId}.", card.Id);
            return validationStatus;
        }
    }

    private static InterlocutorScript BuildProjection(RolePlayCard card)
        => new()
        {
            RolePlayCardId = card.Id,
            PatientBackground = card.Background,
            OpeningResponse = "Hello. I would like to discuss the situation today.",
            Prompt1 = "Could you ask me to explain the situation?",
            Prompt2 = "Could you ask me to explain that in a little more detail?",
            Prompt3 = "Could you ask what help I would like?",
            HiddenInformation = string.Empty,
            ResistanceLevel = ResistanceLevel.Low,
            ClosingCue = "Thank you for explaining that.",
            EmotionalState = card.PatientEmotion,
            ProfessionRoleNotes = null,
            PatientTasks = Array.Empty<string>(),
            LayLanguageTriggersJson = "[]",
            AllowsSecondVisit = false,
            SecondVisitIndicator = null,
            SecondVisitCarryFactsJson = "[]",
        };

    private static LiveVoiceContentReadiness ProjectGenerated(InterlocutorScript script)
        => new(
            Script: script,
            Generated: true,
            NeedsOwnerInput: script.NeedsOwnerInput,
            Provenance: ProjectionProvenance,
            JevValidationStatus: script.JevValidationStatus ?? "jev_unavailable");

    private static string ComputeSourceDigest(RolePlayCard card)
    {
        var source = JsonSerializer.SerializeToUtf8Bytes(new
        {
            card.ScenarioTitle,
            card.Setting,
            card.CandidateRole,
            card.InterlocutorRole,
            card.PatientName,
            card.PatientAge,
            card.Background,
            tasks = card.Tasks,
            card.PatientEmotion,
            card.CommunicationGoal,
            card.ClinicalTopic,
        });
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }

    private static bool IsPositive(JevJudgmentResult result, string id)
        => result.Answers is not null
            && result.Answers.TryGetValue(id, out var answer)
            && answer.Noul is not null
            && answer.Noul.Probability >= 0.5;
}
