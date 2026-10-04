using System.Text.Json;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The live patient is a real, stable person (owner spec 4 Oct 2026): a name that is never blank or invented afresh, a gender
/// and age band worked out from the card, a voice from a two-by-two table, and an opening that waits to be invited. All of it
/// is computed from the card alone, so a failover or a reconnect never changes who is speaking.
/// </summary>
public sealed class LiveVoicePatientIdentityTests
{
    private static RolePlayCard Card(
        string id = "rpc-1",
        string? patientName = null,
        string? patientAge = null,
        string interlocutorRole = "Patient",
        string background = "Visible background.",
        string? primaryCategory = null)
        => new()
        {
            Id = id,
            ContentItemId = "ci-1",
            ProfessionId = "medicine",
            ScenarioTitle = "Post-operative pain review",
            Setting = "Hospital ward",
            CandidateRole = "Doctor",
            InterlocutorRole = interlocutorRole,
            PatientName = patientName,
            PatientAge = patientAge,
            Background = background,
            PatientEmotion = "anxious",
            CommunicationGoal = "Reassure and plan analgesia",
            ClinicalTopic = "pain management",
            Difficulty = "core",
            CriteriaFocusJson = "[]",
            Disclaimer = "Practice estimate only.",
            PrimaryCategory = primaryCategory ?? "Other Cards",
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static string Instructions(RolePlayCard card, bool secondVisit = false)
    {
        var script = new InterlocutorScript
        {
            Id = "is-1",
            RolePlayCardId = card.Id,
            OpeningResponse = "Doctor, this pain is much worse than yesterday.",
            HiddenInformation = "Previous bad reaction to morphine",
            ResistanceLevel = ResistanceLevel.Medium,
            ClosingCue = "Accept the plan once reassured",
            EmotionalState = "Worried",
            LayLanguageTriggersJson = "[]",
            AllowsSecondVisit = secondVisit,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var readiness = new LiveVoiceContentReadiness(script, false, false, "authored_interlocutor_script", "not_required");
        return LiveVoiceService.BuildInstructions(card, script, readiness);
    }

    // ── The name ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARealNameOnTheCard_IsTheNameTheSpeakerAnswersTo()
    {
        var identity = LiveVoicePatientIdentityResolver.Resolve(Card(patientName: "  Mrs Hannah Clarke "));

        Assert.Equal("Mrs Hannah Clarke", identity.SpeakerName);
        Assert.Null(identity.PatientName);
        Assert.False(identity.PlaysAThirdParty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankName_GetsAStablePlainOne_NeverTheModelsOwnInvention(string? blank)
    {
        var first = LiveVoicePatientIdentityResolver.Resolve(Card(patientName: blank));
        var second = LiveVoicePatientIdentityResolver.Resolve(Card(patientName: blank));

        Assert.Equal(first, second); // the same person on every call, on every provider, after every reconnect
        Assert.Contains(first.SpeakerName, new[] { "James", "Jack", "Anne", "Sarah" });
        Assert.False(string.IsNullOrWhiteSpace(first.SpeakerName));
    }

    [Fact]
    public void TheFallbackName_FollowsTheGender_AndUsesBothNamesOfEachPoolAcrossCards()
    {
        var males = new HashSet<string>();
        var females = new HashSet<string>();
        for (var i = 0; i < 80; i++)
        {
            var identity = LiveVoicePatientIdentityResolver.Resolve(Card(id: $"card-{i}"));
            (identity.Gender == LiveVoicePatientIdentityResolver.Male ? males : females).Add(identity.SpeakerName);
        }

        Assert.Equal(new[] { "Jack", "James" }, males.Order().ToArray());
        Assert.Equal(new[] { "Anne", "Sarah" }, females.Order().ToArray());
    }

    // ── Gender and age ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Mr John Smith", "male")]
    [InlineData("Mrs Anne Brown", "female")]
    [InlineData("Ms Lee", "female")]
    [InlineData("Miss Patel", "female")]
    public void ATitleOnTheName_DecidesTheGender(string name, string expected)
        => Assert.Equal(expected, LiveVoicePatientIdentityResolver.Resolve(Card(patientName: name)).Gender);

    [Theory]
    [InlineData("She has had a cough for three weeks and her sleep is poor.", "female")]
    [InlineData("He fell at home and his hip hurts; the man is worried.", "male")]
    public void WithoutATitle_ThePronounsInTheBackground_DecideTheGender(string background, string expected)
        => Assert.Equal(expected, LiveVoicePatientIdentityResolver.Resolve(Card(background: background)).Gender);

    [Theory]
    [InlineData("44", "younger")]
    [InlineData("45", "older")]
    [InlineData("62 years", "older")]
    [InlineData("30-year-old", "younger")]
    [InlineData("6 months", "younger")]
    [InlineData("3 weeks", "younger")]
    [InlineData(null, "younger")]
    [InlineData("", "younger")]
    [InlineData("elderly", "younger")]
    public void TheAgeBand_IsUnder45OrOlder(string? age, string expected)
        => Assert.Equal(expected, LiveVoicePatientIdentityResolver.Resolve(Card(patientAge: age)).AgeBand);

    // ── A speaker who is not the patient ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Mother of the patient", "female")]
    [InlineData("Patient's father", "male")]
    [InlineData("Carer", null)]
    public void AParentOrCarer_IsAPersonWithAnOwnName_AndTheCardNameBelongsToThePatient(string role, string? gender)
    {
        var identity = LiveVoicePatientIdentityResolver.Resolve(Card(patientName: "Oliver Reed", patientAge: "6", interlocutorRole: role));

        Assert.True(identity.PlaysAThirdParty);
        Assert.Equal("Oliver Reed", identity.PatientName);
        Assert.NotEqual("Oliver Reed", identity.SpeakerName);
        Assert.Contains(identity.SpeakerName, new[] { "James", "Jack", "Anne", "Sarah" });
        if (gender is not null) Assert.Equal(gender, identity.Gender);
        // A child's age says nothing about the parent's voice.
        Assert.Equal("younger", identity.AgeBand);
    }

    // ── The voice ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheVoiceTable_IsTwoByTwoPerProvider_AndTheSameGenderAndAgeOnBothProviders()
    {
        var options = new LiveVoiceOptions();
        var cells = new[]
        {
            (LiveVoicePatientIdentityResolver.Female, LiveVoicePatientIdentityResolver.Younger),
            (LiveVoicePatientIdentityResolver.Female, LiveVoicePatientIdentityResolver.Older),
            (LiveVoicePatientIdentityResolver.Male, LiveVoicePatientIdentityResolver.Younger),
            (LiveVoicePatientIdentityResolver.Male, LiveVoicePatientIdentityResolver.Older),
        };

        var openAi = cells.Select(c => LiveVoicePatientIdentityResolver.VoiceFor(new LiveVoicePatientIdentity("X", null, c.Item1, c.Item2, false), LiveVoiceProviders.OpenAi, options)).ToArray();
        var gemini = cells.Select(c => LiveVoicePatientIdentityResolver.VoiceFor(new LiveVoicePatientIdentity("X", null, c.Item1, c.Item2, false), LiveVoiceProviders.Gemini, options)).ToArray();

        // Four different voices on each provider: two female, two male.
        Assert.Equal(4, openAi.Distinct().Count());
        Assert.Equal(4, gemini.Distinct().Count());
        Assert.Equal(new[] { "quartz", "willow", "ripple", "vesper" }, openAi);
        Assert.All(openAi.Concat(gemini), voice => Assert.False(string.IsNullOrWhiteSpace(voice)));
    }

    [Fact]
    public void ABlankCell_MeansTheProvidersOwnVoice_NotAnEmptyVoiceName()
    {
        var options = new LiveVoiceOptions { OpenAiVoiceMaleOlder = "  ", GeminiVoiceMaleOlder = "" };
        var identity = new LiveVoicePatientIdentity("X", null, LiveVoicePatientIdentityResolver.Male, LiveVoicePatientIdentityResolver.Older, false);

        Assert.Null(LiveVoicePatientIdentityResolver.VoiceFor(identity, LiveVoiceProviders.OpenAi, options));
        Assert.Null(LiveVoicePatientIdentityResolver.VoiceFor(identity, LiveVoiceProviders.Gemini, options));
    }

    [Fact]
    public void TheSameCard_IsTheSamePersonOnBothProviders_ExceptForTheirVoiceNames()
    {
        var card = Card(id: "rpc-stable", patientName: "Mr Daniel Hughes", patientAge: "58");
        var identity = LiveVoicePatientIdentityResolver.Resolve(card);
        var options = new LiveVoiceOptions();

        Assert.Equal(LiveVoicePatientIdentityResolver.Male, identity.Gender);
        Assert.Equal(LiveVoicePatientIdentityResolver.Older, identity.AgeBand);
        Assert.Equal("vesper", LiveVoicePatientIdentityResolver.VoiceFor(identity, LiveVoiceProviders.OpenAi, options));
        Assert.Equal(options.GeminiVoiceMaleOlder, LiveVoicePatientIdentityResolver.VoiceFor(identity, LiveVoiceProviders.Gemini, options));
    }

    // ── What the patient is told ───────────────────────────────────────────────────────────

    [Fact]
    public void TheInstructions_NameThePatient_AndSayAQuestionAboutANameNeverStartsTheStory()
    {
        var text = Instructions(Card(patientName: "Mrs Hannah Clarke"));

        Assert.Contains("YOUR IDENTITY: your name is Mrs Hannah Clarke and you are a female.", text, StringComparison.Ordinal);
        Assert.Contains("A question about your name never starts your story.", text, StringComparison.Ordinal);
        Assert.Contains("Patient name: Mrs Hannah Clarke", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCardName_NeverReachesThePatientAsNotSuppliedOrEmpty(string? blank)
    {
        var card = Card(patientName: blank);
        var identity = LiveVoicePatientIdentityResolver.Resolve(card);

        var text = Instructions(card);

        Assert.DoesNotContain("Patient name: not supplied", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Patient name: \n", text, StringComparison.Ordinal);
        Assert.Contains($"Patient name: {identity.SpeakerName}", text, StringComparison.Ordinal);
        Assert.Contains($"YOUR IDENTITY: your name is {identity.SpeakerName}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ForAParent_TheInstructionsGiveTheirOwnNameAndTheChildsName()
    {
        var card = Card(patientName: "Oliver Reed", patientAge: "6", interlocutorRole: "Mother of the patient");
        var identity = LiveVoicePatientIdentityResolver.Resolve(card);

        var text = Instructions(card);

        Assert.Contains($"YOUR IDENTITY: your name is {identity.SpeakerName}, you are a female, and you are speaking for the patient (the patient's name is Oliver Reed).", text, StringComparison.Ordinal);
        Assert.Contains("Patient name: Oliver Reed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpeningContract_WaitsForAnExplicitInvitation_AndAGreetingOrANameIsNotOne()
    {
        var text = Instructions(Card());

        Assert.Contains("never speak first", text, StringComparison.Ordinal);
        Assert.Contains("A GREETING, AN INTRODUCTION, A NAME EXCHANGE OR A PAUSE IS NOT AN INVITATION TO TELL YOUR STORY", text, StringComparison.Ordinal);
        Assert.Contains("answer with a brief greeting of a few words and then wait", text, StringComparison.Ordinal);
        Assert.Contains("GIVE YOUR OPENING RESPONSE ONLY AFTER AN EXPLICIT INVITATION", text, StringComparison.Ordinal);
        Assert.Contains("what brings you in today", text, StringComparison.Ordinal);
        Assert.Contains("NARROW QUESTION, NARROW ANSWER", text, StringComparison.Ordinal);
        Assert.Contains("INTERRUPTION POLICY: never talk over the candidate", text, StringComparison.Ordinal);
        Assert.Contains("four to five seconds", text, StringComparison.Ordinal);

        // All of it comes before the card data and before the private story it protects.
        var data = text.IndexOf("[CANDIDATE CARD DATA", StringComparison.Ordinal);
        Assert.True(text.IndexOf("GIVE YOUR OPENING RESPONSE ONLY AFTER", StringComparison.Ordinal) < data);
        Assert.True(text.IndexOf("YOUR IDENTITY:", StringComparison.Ordinal) < data);
    }

    [Fact]
    public void ANewSessionAfterAReconnect_StillCarriesTheSameIdentityAndTheSameOpeningContract()
    {
        var card = Card(patientName: null);

        var first = Instructions(card);
        var second = Instructions(card);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AReturnVisitCard_ConfirmsWhyItCameBack_OnlyAfterTheCandidateHasSaidWhatTheyWantToGoThrough()
    {
        var followUp = Instructions(Card(primaryCategory: "Second Visit / Follow-up"));
        var ordinary = Instructions(Card());
        var allowsSecondVisit = Instructions(Card(), secondVisit: true);

        Assert.Contains("THIS IS A RETURN VISIT", followUp, StringComparison.Ordinal);
        Assert.Contains("THIS IS A RETURN VISIT", allowsSecondVisit, StringComparison.Ordinal);
        Assert.DoesNotContain("THIS IS A RETURN VISIT", ordinary, StringComparison.Ordinal);
    }

    // ── The provider bodies ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheOpenAiSession_PutsTheVoiceUnderAudioOutput_AndOmitsAudioWhenThereIsNone()
    {
        using var withVoice = JsonDocument.Parse(JsonSerializer.Serialize(LiveVoiceService.BuildOpenAiSession("gpt-live-1", "persona", " ripple "), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var without = JsonDocument.Parse(JsonSerializer.Serialize(LiveVoiceService.BuildOpenAiSession("gpt-live-1", "persona", null), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal("ripple", withVoice.RootElement.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
        Assert.Equal("gpt-live-1", withVoice.RootElement.GetProperty("model").GetString());
        Assert.Equal("persona", withVoice.RootElement.GetProperty("instructions").GetString());
        Assert.False(without.RootElement.TryGetProperty("audio", out _));
        Assert.Equal(2, without.RootElement.EnumerateObject().Count()); // model and instructions, exactly as before
    }

    [Fact]
    public void TheGeminiSetup_CarriesTheVoiceAndALongerWaitForTheEndOfTheCandidatesTurn()
    {
        var json = JsonSerializer.Serialize(
            new { bidiGenerateContentSetup = LiveVoiceService.BuildGeminiSetup("models/gemini-3.8-live", "persona", "Charon") },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var setup = doc.RootElement.GetProperty("bidiGenerateContentSetup");

        var generation = setup.GetProperty("generationConfig");
        Assert.Equal("AUDIO", generation.GetProperty("responseModalities")[0].GetString());
        Assert.Equal("Charon", generation.GetProperty("speechConfig").GetProperty("voiceConfig")
            .GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString());

        var detection = setup.GetProperty("realtimeInputConfig").GetProperty("automaticActivityDetection");
        Assert.Equal("END_SENSITIVITY_LOW", detection.GetProperty("endOfSpeechSensitivity").GetString());
        Assert.Equal(1200, detection.GetProperty("silenceDurationMs").GetInt32());
        // The start of speech keeps its default so a real interruption still stops the patient.
        Assert.False(detection.TryGetProperty("startOfSpeechSensitivity", out _));
    }

    [Fact]
    public void TheGeminiSetup_WithoutAVoice_HasNoSpeechConfigAtAll()
    {
        var json = JsonSerializer.Serialize(
            LiveVoiceService.BuildGeminiSetup("models/gemini-3.8-live", "persona"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("generationConfig").TryGetProperty("speechConfig", out _));
    }

    [Fact]
    public void ABlankPatientNameIsNeverStoredAsAnEmptyString_OnTheCreatePath()
    {
        var source = File.ReadAllText(FindSource("Services/AdminService.SpeakingRolePlayCards.cs"));

        Assert.Contains("PatientName = string.IsNullOrWhiteSpace(req.PatientName) ? null : req.PatientName.Trim(),", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PatientName = req.PatientName?.Trim(),", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneratedFallbackScript_NeverOpensWithAGreeting()
    {
        var source = File.ReadAllText(FindSource("Services/Speaking/LiveVoiceContentReadinessService.cs"));

        Assert.DoesNotContain("OpeningResponse = \"Hello.", source, StringComparison.Ordinal);
    }

    private static string FindSource(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "OetLearner.Api", relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}.");
    }
}
