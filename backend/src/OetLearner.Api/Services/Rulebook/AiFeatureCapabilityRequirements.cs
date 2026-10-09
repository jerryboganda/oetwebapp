using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// The modality a feature code actually performs. A chat model's probed capabilities cannot
/// describe an OCR pass or a transcription, so these are separated up front instead of being
/// flattened into one flag set that would make a text model look capable of both.
/// </summary>
public enum AiFeatureModality
{
    /// <summary>Grounded chat completion. Governed by the per-model capability probe.</summary>
    Chat = 0,

    /// <summary>Image / scanned-page text extraction.</summary>
    Ocr = 1,

    /// <summary>Speech-to-text.</summary>
    Stt = 2,

    /// <summary>Text-to-speech.</summary>
    Tts = 3,

    /// <summary>Phoneme-level pronunciation scoring.</summary>
    Phoneme = 4,

    /// <summary>Structured PDF table / form extraction.</summary>
    PdfExtraction = 5,

    /// <summary>Vector embeddings.</summary>
    Embeddings = 6,

    /// <summary>Not gateway-routable at all — driven directly by a job or endpoint, so a feature
    /// route is meaningless for it.</summary>
    Direct = 7,
}

/// <summary>
/// What ONE feature code requires of a provider, declared in code beside
/// <see cref="AiFeatureCodes"/> rather than in the database.
///
/// <para>
/// This map lives in code deliberately. It is a statement about what each feature DOES, and the
/// feature codes themselves are code constants with contractual behaviour — keeping the two in
/// one place means a reviewer reads the requirement and the code it describes together, and a
/// new feature code cannot ship without someone deciding what it needs. An admin-editable map
/// would let someone mark <c>ocr.writing.handwriting</c> as a text feature and turn a routing
/// mistake into a runtime failure.
/// </para>
///
/// <para>
/// Every lookup fails CLOSED: an unlisted code, an unlisted capability, or a provider with no
/// probe result is refused rather than assumed adequate.
/// </para>
/// </summary>
public static class AiFeatureCapabilityRequirements
{
    [Flags]
    public enum Required
    {
        None = 0,

        /// <summary>A plain text chat turn. Every chat model satisfies this.</summary>
        Text = 1 << 0,

        /// <summary>The call site declares <c>tools</c> and parses <c>tool_calls</c>.</summary>
        Tools = 1 << 1,

        /// <summary>The request carries an <c>image_url</c> content part.</summary>
        Vision = 1 << 2,

        /// <summary>The request carries a document/file content part.</summary>
        Documents = 1 << 3,

        /// <summary>The call site sets <c>response_format</c> and parses strict JSON.</summary>
        JsonMode = 1 << 4,

        /// <summary>The call site needs incremental delivery, not one buffered blob.</summary>
        Streaming = 1 << 5,

        /// <summary>The call site posts to a vector-embedding endpoint.</summary>
        Embeddings = 1 << 6,
    }

    /// <summary>One feature code's declared needs.</summary>
    public readonly record struct Requirement(AiFeatureModality Modality, Required Capabilities, string Note);

    /// <summary>Plain grounded text chat. The overwhelming majority of codes.</summary>
    private const Required Chat = Required.Text;

    /// <summary>Chat that must come back as strict JSON for a typed DTO.</summary>
    private const Required ChatJson = Required.Text | Required.JsonMode;

    /// <summary>Chat that reads repository files / queries through declared tools.</summary>
    private const Required ChatTools = Required.Text | Required.Tools;

    /// <summary>
    /// Every routable code. Deliberately explicit rather than a default: a code that appears in
    /// <c>AiFeatureRouteResolver.KnownFeatureCodes</c> but not here is refused by the route gate,
    /// which is the intended "you have not decided what this needs" outcome.
    /// </summary>
    private static readonly Dictionary<string, Requirement> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Writing: grading, review, coaching ────────────────────────────────
        [AiFeatureCodes.WritingGrade] = new(AiFeatureModality.Chat, ChatJson,
            "Letter/assessment grading returns a typed score DTO."),
        [AiFeatureCodes.WritingGradeReview] = new(AiFeatureModality.Chat, ChatJson,
            "Model Answer / reviewer pass returns a typed verdict DTO."),
        [AiFeatureCodes.WritingSampleScore] = new(AiFeatureModality.Chat, ChatJson,
            "Scored exemplar parse into a typed breakdown."),
        [AiFeatureCodes.WritingCoachSuggest] = new(AiFeatureModality.Chat, ChatJson,
            "Coaching suggestions are typed so the UI can render each one."),
        [AiFeatureCodes.WritingCoachExplain] = new(AiFeatureModality.Chat, Chat,
            "Prose explanation; rendered as text."),

        // ── Speaking ──────────────────────────────────────────────────────────
        [AiFeatureCodes.SpeakingGrade] = new(AiFeatureModality.Chat, ChatJson,
            "Speaking grade returns a typed assessment DTO."),
        [AiFeatureCodes.SpeakingGradeReview] = new(AiFeatureModality.Chat, ChatJson,
            "Speaking reviewer pass returns a typed verdict DTO."),
        [SpeakingAiFeatureCodes.SpeakingScoreV2] = new(AiFeatureModality.Chat, ChatJson,
            "Speaking score v2 consumes audio-derived evidence and returns a typed score. Text-only models may still qualify: the audio is transcribed upstream."),
        [SpeakingAiFeatureCodes.SpeakingPatientTurnV1] = new(AiFeatureModality.Chat, Chat,
            "Single simulated patient turn; prose."),
        [SpeakingAiFeatureCodes.CardDraftV1] = new(AiFeatureModality.Chat, ChatJson,
            "Speaking card draft is parsed into a typed card structure."),

        // ── Conversation / pronunciation ──────────────────────────────────────
        [AiFeatureCodes.ConversationOpening] = new(AiFeatureModality.Chat, Chat, "Opening turn; prose."),
        [AiFeatureCodes.ConversationReply] = new(AiFeatureModality.Chat, Chat, "In-session reply; prose."),
        [AiFeatureCodes.ConversationEvaluation] = new(AiFeatureModality.Chat, ChatJson,
            "Post-session evaluation returns a typed, scored breakdown."),
        [AiFeatureCodes.PronunciationTip] = new(AiFeatureModality.Chat, Chat, "Coaching tip; prose."),
        [AiFeatureCodes.PronunciationScore] = new(AiFeatureModality.Direct, Required.None,
            "Scored from phoneme/ASR output, not from a chat model."),
        [AiFeatureCodes.PronunciationLinguisticScore] = new(AiFeatureModality.Direct, Required.None,
            "Computed from a phoneme provider, not a chat model."),
        [AiFeatureCodes.PronunciationFeedback] = new(AiFeatureModality.Chat, Chat,
            "Feedback prose is generated once scores exist."),

        // ── Reading / Listening / vocabulary / recalls ────────────────────────
        [AiFeatureCodes.SummarisePassage] = new(AiFeatureModality.Chat, Chat, "Prose summary."),
        [AiFeatureCodes.VocabularyGloss] = new(AiFeatureModality.Chat, ChatJson,
            "Gloss is a typed {term, definition, ...} object the UI renders as a card."),
        [AiFeatureCodes.RecallsMistakeExplain] = new(AiFeatureModality.Chat, Chat, "Prose explanation."),
        [AiFeatureCodes.RecallsRevisionPlan] = new(AiFeatureModality.Chat, ChatJson,
            "Revision plan is a typed ordered list the UI renders."),
        [AiFeatureCodes.ReadingExplanation] = new(AiFeatureModality.Chat, Chat, "Prose explanation."),
        [AiFeatureCodes.ReadingPassageQna] = new(AiFeatureModality.Chat, Chat, "Grounded Q&A; prose."),
        [AiFeatureCodes.ReadingVocabularyCard] = new(AiFeatureModality.Chat, ChatJson,
            "Card is a typed structure with several fields."),
        [AiFeatureCodes.ListeningExplanation] = new(AiFeatureModality.Chat, Chat, "Prose explanation."),

        // ── Admin authoring ──────────────────────────────────────────────────
        [AiFeatureCodes.AdminContentGeneration] = new(AiFeatureModality.Chat, Chat, "Authoring assist; prose."),
        [AiFeatureCodes.AdminGrammarDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed grammar draft."),
        [AiFeatureCodes.AdminPronunciationDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed pronunciation draft."),
        [AiFeatureCodes.AdminVocabularyDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed vocabulary draft."),
        [AiFeatureCodes.AdminConversationDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed conversation draft."),
        [AiFeatureCodes.AdminListeningDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed listening draft."),
        [AiFeatureCodes.AdminReadingDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed reading draft."),
        [AiFeatureCodes.AdminWritingDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed writing draft."),

        // ── Writing V2 coaching tools ─────────────────────────────────────────
        [AiFeatureCodes.WritingCoachV1] = new(AiFeatureModality.Chat, Chat, "Prose coaching."),
        [AiFeatureCodes.WritingRewriteV1] = new(AiFeatureModality.Chat, Chat, "Rewrite; prose."),
        [AiFeatureCodes.WritingScenarioGenerateV1] = new(AiFeatureModality.Chat, ChatJson,
            "A generated scenario is parsed into a typed, validated scenario object."),
        [AiFeatureCodes.WritingAppealV1] = new(AiFeatureModality.Chat, ChatJson, "Typed appeal outcome."),
        [AiFeatureCodes.WritingCanonDetectV1] = new(AiFeatureModality.Chat, ChatJson, "Typed detection result."),
        [AiFeatureCodes.WritingDrillGradeV1] = new(AiFeatureModality.Chat, ChatJson, "Typed drill grade."),
        [AiFeatureCodes.WritingOutlineV1] = new(AiFeatureModality.Chat, ChatJson, "Typed outline."),
        [AiFeatureCodes.WritingParaphraseV1] = new(AiFeatureModality.Chat, Chat, "Paraphrase; prose."),
        [AiFeatureCodes.WritingAskV1] = new(AiFeatureModality.Chat, Chat, "Grounded Q&A; prose."),
        [AiFeatureCodes.MockRemediationDraft] = new(AiFeatureModality.Chat, ChatJson, "Typed remediation draft."),

        // ── AI Assistant (three roles) ───────────────────────────────────────
        // Tool grants are per feature code and per user, so a tool-free admin deployment does not
        // actually need tool support. Tools are therefore required for the STAFF surfaces (the
        // codebase toolset is the whole point of the admin chatbot) and not for the learner
        // surface, whose grant set is read-only and prose-shaped.
        [AiFeatureCodes.AiAssistantAdmin] = new(AiFeatureModality.Chat, ChatTools,
            "Developer chatbot: declared codebase/repository tools, mutable grants."),
        [AiFeatureCodes.AiAssistantExpert] = new(AiFeatureModality.Chat, ChatTools | Required.Streaming,
            "Staff chatbot: read-only toolset, and expects incremental token delivery."),
        [AiFeatureCodes.AiAssistantLearner] = new(AiFeatureModality.Chat, ChatTools | Required.Streaming,
            "Learner chatbot: read-only toolset, and MUST stream — the UI renders incremental tokens."),

        // ── Live Class Recording pipeline ─────────────────────────────────────
        [AiFeatureCodes.ClassRecordingTranscribe] = new(AiFeatureModality.Stt, Required.None,
            "Audio-to-text: a transcription provider, not a chat model."),
        [AiFeatureCodes.ClassRecordingSummarize] = new(AiFeatureModality.Chat, ChatJson,
            "Transcript to typed {summary, chapters, actionItems, keyTopics}."),
        [AiFeatureCodes.ClassRecordingTranslate] = new(AiFeatureModality.Chat, Chat, "EN to AR; prose."),
        [AiFeatureCodes.ClassAssistantQna] = new(AiFeatureModality.Chat, Chat, "Grounded transcript Q&A; prose."),
        [AiFeatureCodes.TutorRecommendation] = new(AiFeatureModality.Chat, ChatJson, "Typed recommendation."),

        // ── OCR / PDF ─────────────────────────────────────────────────────────
        [AiFeatureCodes.OcrListeningPartA] = new(AiFeatureModality.Ocr, Required.None, "OCR pass."),
        [AiFeatureCodes.OcrListeningPartBC] = new(AiFeatureModality.Ocr, Required.None, "OCR pass."),
        [AiFeatureCodes.OcrContentPdfFallback] = new(AiFeatureModality.Ocr, Required.None, "OCR fallback."),
        [AiFeatureCodes.OcrWritingHandwriting] = new(AiFeatureModality.Ocr, Required.None, "Handwriting OCR."),

        // ── Listening Part A / B-C structuring and marking ────────────────────
        // Audio reaches these as text already transcribed/OCRed upstream, so a text chat model is
        // the right shape; the calls are forced-tool structuring calls that must return typed JSON.
        [AiFeatureCodes.ListeningPartAExtract] = new(AiFeatureModality.Chat, Required.Text | Required.Tools | Required.JsonMode,
            "Part A manifest structuring: a forced-tool call whose result is parsed into a typed manifest."),
        [AiFeatureCodes.ListeningPartAScore] = new(AiFeatureModality.Chat, ChatJson,
            "Per-gap AI marking returning typed scores. Benchmark-gated by AiProviderRouteApprovalService."),
        [AiFeatureCodes.ListeningPartBCExtract] = new(AiFeatureModality.Chat, Required.Text | Required.Tools | Required.JsonMode,
            "Answer-key structuring: a forced-tool call parsed into a typed key."),

        // ── Transcription ─────────────────────────────────────────────────────
        [AiFeatureCodes.SttSpeakingTranscribe] = new(AiFeatureModality.Stt, Required.None, "Whisper-shape STT."),
        [AiFeatureCodes.SttPronunciationTranscribe] = new(AiFeatureModality.Stt, Required.None, "Whisper-shape STT."),
        [AiFeatureCodes.SttConversationTranscribe] = new(AiFeatureModality.Stt, Required.None, "Whisper-shape STT."),

        // ── Embeddings ────────────────────────────────────────────────────────
        [AiFeatureCodes.EmbeddingsGenerate] = new(AiFeatureModality.Embeddings, Required.Embeddings,
            "Vector embeddings. Many chat vendors expose no embedding route; this is probed, not assumed."),
        [AiFeatureCodes.WritingExemplarEmbedV1] = new(AiFeatureModality.Embeddings, Required.Embeddings,
            "Exemplar vectors; probed embedding route required."),
    };

    /// <summary>Every code the map covers, for the admin UI and for coverage checks.</summary>
    public static IReadOnlyCollection<string> MappedFeatureCodes => Map.Keys;

    /// <summary>The requirement for <paramref name="featureCode"/>, or <see langword="null"/> when the
    /// code has no declared requirement. Callers MUST refuse on null rather than assume adequacy.</summary>
    public static Requirement? For(string? featureCode)
        => featureCode is not null && Map.TryGetValue(featureCode, out var requirement) ? requirement : null;

    /// <summary>Provider <see cref="AiProviderCategory"/> a modality demands, or null for
    /// <see cref="AiFeatureModality.Chat"/> and <see cref="AiFeatureModality.Embeddings"/> (which ride
    /// on a text-chat row) and for <see cref="AiFeatureModality.Direct"/>.</summary>
    public static AiProviderCategory? RequiredCategory(AiFeatureModality modality) => modality switch
    {
        AiFeatureModality.Ocr => AiProviderCategory.Ocr,
        AiFeatureModality.Stt => AiProviderCategory.Asr,
        AiFeatureModality.Tts => AiProviderCategory.Tts,
        AiFeatureModality.Phoneme => AiProviderCategory.Phoneme,
        AiFeatureModality.PdfExtraction => AiProviderCategory.PdfExtraction,
        _ => null,
    };

    /// <summary>Human-readable capability names, for a refusal message that names what is missing.</summary>
    public static string Describe(Required capabilities) => capabilities switch
    {
        Required.None => "no specific capability",
        Required.Text => "text chat",
        _ => string.Join(" + ", Names(capabilities)),
    };

    private static IEnumerable<string> Names(Required capabilities)
    {
        if (capabilities.HasFlag(Required.Text)) yield return "text chat";
        if (capabilities.HasFlag(Required.Tools)) yield return "tool calling";
        if (capabilities.HasFlag(Required.Vision)) yield return "image input";
        if (capabilities.HasFlag(Required.Documents)) yield return "document input";
        if (capabilities.HasFlag(Required.JsonMode)) yield return "JSON mode";
        if (capabilities.HasFlag(Required.Streaming)) yield return "streaming";
        if (capabilities.HasFlag(Required.Embeddings)) yield return "embeddings";
    }

    /// <summary>Outcome of checking one (feature, provider, model) triple against the map.</summary>
    public readonly record struct Verdict(bool Allowed, string Reason);

    private static readonly Verdict AllowedVerdict = new(true, "OK");

    /// <summary>
    /// Can <paramref name="provider"/> serve <paramref name="featureCode"/> using
    /// <paramref name="model"/>?
    ///
    /// <para>
    /// This is the gate behind "only route a provider where it is usable". It asks what the feature
    /// NEEDS, then asks what the model was <em>observed</em> to do, and refuses when the two do not
    /// line up. The refusal is specific ("needs JSON mode; glm-4.6v was probed without it") because a
    /// generic refusal teaches an admin nothing.
    /// </para>
    ///
    /// <para>
    /// <b>Fails closed throughout.</b> No declared requirement, a missing probe row, or a probe that
    /// could not reach the endpoint all produce a refusal. The one deliberate exception is a feature
    /// needing nothing but text from a provider with no probe on file: every text chat model does
    /// plain chat, so demanding a probe for the most basic case would block routing to providers
    /// nobody has probed yet for no benefit. Anything beyond plain text still needs a probe.
    /// </para>
    /// </summary>
    public static Verdict Evaluate(
        string? featureCode,
        AiProvider provider,
        AiProviderModelCapability? capability,
        string? model)
    {
        var requirement = For(featureCode);
        if (requirement is null)
        {
            return new Verdict(false,
                $"No capability requirement is declared for '{featureCode}'. Declare it in AiFeatureCapabilityRequirements before routing it.");
        }

        var req = requirement.Value;

        // ── Non-chat modalities are decided by the provider's category ───────
        if (req.Modality != AiFeatureModality.Chat)
        {
            if (req.Modality == AiFeatureModality.Direct)
            {
                return new Verdict(false,
                    $"'{featureCode}' is not a gateway-routable chat feature (it is {req.Modality}), so a feature route does not apply to it.");
            }

            var requiredCategory = RequiredCategory(req.Modality);
            if (requiredCategory is null)
            {
                // Embeddings ride on a text-chat row, so the category matches and the probed
                // flag is what decides.
                return EvaluateChat(provider, capability, model, req.Capabilities, featureCode, req);
            }

            if (provider.Category != requiredCategory.Value)
            {
                return new Verdict(false,
                    $"'{featureCode}' needs a {req.Modality} provider (category {requiredCategory}), but '{provider.Code}' is {provider.Category}. "
                    + $"A route here would be accepted now and fail at call time.");
            }

            return AllowedVerdict;
        }

        return EvaluateChat(provider, capability, model, req.Capabilities, featureCode, req);
    }

    private static Verdict EvaluateChat(
        AiProvider provider,
        AiProviderModelCapability? capability,
        string? model,
        Required needed,
        string? featureCode,
        Requirement requirement)
    {
        if (needed == Required.Text)
            return AllowedVerdict;

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? provider.DefaultModel : model.Trim();

        if (capability is null)
        {
            return new Verdict(false,
                $"'{featureCode}' needs {Describe(needed)}, and '{provider.Code}' has no probe result for "
                + $"'{effectiveModel}'. Run Probe capabilities on /admin/ai-providers first — an unprobed model is treated as incapable, not as capable.");
        }

        var missing = new List<string>();
        if (needed.HasFlag(Required.Tools) && !capability.SupportsTools) missing.Add("tool calling");
        if (needed.HasFlag(Required.Vision) && !capability.SupportsVision) missing.Add("image input");
        if (needed.HasFlag(Required.Documents) && !capability.SupportsDocuments) missing.Add("document input");
        if (needed.HasFlag(Required.JsonMode) && !capability.SupportsJsonMode) missing.Add("JSON mode");
        if (needed.HasFlag(Required.Streaming) && !capability.SupportsStreaming) missing.Add("streaming");
        if (needed.HasFlag(Required.Embeddings) && !capability.SupportsEmbeddings) missing.Add("embeddings");

        if (missing.Count == 0) return AllowedVerdict;

        var because = capability.ProbeStatus is "ok" or "partial"
            ? "was probed without it"
            : $"could not be established (probe status: {capability.ProbeStatus})";

        return new Verdict(false,
            $"'{featureCode}' needs {string.Join(" + ", missing)}, but '{capability.Model}' on '{provider.Code}' {because}."
            + (requirement.Note.Length > 0 ? $" The feature needs this because: {requirement.Note}." : string.Empty));
    }
}