using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingCardClassifierTests
{
    private static SpeakingCardClassification Card(
        string? scenarioTitle = null,
        string? setting = null,
        string? background = null,
        IEnumerable<string>? tasks = null,
        string? clinicalTopic = null,
        string? patientEmotion = null,
        string? patientName = null,
        string? communicationGoal = null)
    {
        return SpeakingCardClassifier.Classify(new SpeakingCardClassifiable(
            ScenarioTitle: scenarioTitle,
            Setting: setting,
            Background: background,
            Tasks: tasks?.ToArray(),
            ClinicalTopic: clinicalTopic,
            PatientEmotion: patientEmotion,
            PatientName: patientName,
            CommunicationGoal: communicationGoal));
    }

    [Fact]
    public void FirstVisit_AngryPatient_IsFirstVisitWithAngrySecondaryTag()
    {
        var result = Card(
            scenarioTitle: "First visit with an angry patient",
            background: "Mrs Allen is attending the clinic for the first time with headaches. She is angry about the waiting time.",
            tasks: new[] { "Take a history", "Acknowledge her concern about the wait" });

        Assert.Equal("First Visit", result.Primary);
        Assert.Contains("Angry", result.SecondaryTags);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void FollowUp_ReluctantPatient_IsSecondVisitWithReluctantSecondaryTag()
    {
        var result = Card(
            scenarioTitle: "Follow-up hypertension review",
            background: "Mr Baker is returning for a follow-up of his hypertension. He refuses to take the prescribed tablets.",
            tasks: new[] { "Check progress since the last visit", "Address his refusal" });

        Assert.Equal("Second Visit / Follow-up", result.Primary);
        Assert.Contains("Reluctant", result.SecondaryTags);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void FirstVisit_BreakingBadNews_IsFirstVisitWithBadNewsSecondaryTag()
    {
        var result = Card(
            scenarioTitle: "First visit with bad news",
            background: "Ms Clark is attending for the first time. The biopsy confirms cancer and you must break the bad news today.",
            tasks: new[] { "Give the diagnosis sensitively" });

        Assert.Equal("First Visit", result.Primary);
        Assert.Contains("Breaking Bad News", result.SecondaryTags);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void FollowUpResult_ConfirmsCancer_IsSecondVisitWithBreakingBadNewsTag()
    {
        var result = Card(
            scenarioTitle: "Follow-up test results",
            background: "Mr Davis is coming back for his test results. The results confirm cancer and you must explain the next steps.",
            tasks: new[] { "Communicate the results", "Discuss referral" });

        Assert.Equal("Second Visit / Follow-up", result.Primary);
        Assert.Contains("Breaking Bad News", result.SecondaryTags);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void AfterExaminationYouFind_Alone_DoesNotForceExaminationCard()
    {
        var result = Card(
            scenarioTitle: "First visit headache",
            background: "Mrs Evans is attending for the first time with headaches. After examination you find nothing abnormal.",
            tasks: new[] { "Take a history", "Explain the findings" });

        Assert.Equal("First Visit", result.Primary);
        Assert.Empty(result.SecondaryTags);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void YouHaveJustExaminedPatient_IsExaminationCard()
    {
        var result = Card(
            scenarioTitle: "Post-examination discussion",
            background: "You have just examined the patient, who presented with abdominal pain. Discuss your findings and plan.",
            tasks: new[] { "Begin with 'Thank you for letting me examine you'" });

        Assert.Equal("Examination Card", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void JustArrivedInEd_IsEmergencyDepartment()
    {
        var result = Card(
            scenarioTitle: "Emergency chest pain",
            setting: "Emergency Department",
            background: "Mr Farah has just arrived in the Emergency Department with chest pain. Take a focused history.",
            tasks: new[] { "Assess the emergency" });

        Assert.Equal("Emergency / Emergency Department", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void PatientInEdWardForHours_IsAlreadyKnownPatient()
    {
        var result = Card(
            scenarioTitle: "Ward review",
            setting: "Emergency Department observation ward",
            background: "Mrs Green has been in the ED under care for hours and is now admitted to the ward. Review her progress.",
            tasks: new[] { "Check how she is responding to treatment" });

        Assert.Equal("Already Known Patient", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void DischargeAfterAdmission_IsAlreadyKnownPatient()
    {
        var result = Card(
            scenarioTitle: "Discharge advice after appendectomy",
            setting: "Surgical ward",
            background: "Mr Hill was admitted three days ago and had an appendectomy. You are discharging him today. Give discharge advice.",
            tasks: new[] { "Explain wound care", "Explain red flags" });

        Assert.Equal("Already Known Patient", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void NursingHomeMedicationReview_WithoutEncounterMatch_IsOtherCardsAndReviewFlag()
    {
        var result = Card(
            scenarioTitle: "Nursing home medication review visit",
            setting: "Nursing home",
            background: "You are visiting a nursing home resident to review the medication chart with the senior carer.",
            tasks: new[] { "Go through the chart" });

        Assert.Equal("Other Cards", result.Primary);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void PureBreakingBadNews_IsBreakingBadNews()
    {
        var result = Card(
            scenarioTitle: "Breaking bad news consultation",
            background: "You must tell the patient the diagnosis is terminal cancer.",
            tasks: new[] { "Break the bad news sensitively" });

        Assert.Equal("Breaking Bad News", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void PureAngryComplaint_IsAngryPatient()
    {
        var result = Card(
            scenarioTitle: "Complaint about a bill",
            background: "The patient is furious about an incorrect bill and demands an explanation.",
            tasks: new[] { "Acknowledge the complaint" });

        Assert.Equal("Angry Patient", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void PureRefusal_IsReluctantPatient()
    {
        var result = Card(
            scenarioTitle: "Vaccination refusal",
            background: "The mother refuses vaccination for her child despite your advice.",
            tasks: new[] { "Explore her concerns" });

        Assert.Equal("Reluctant Patient", result.Primary);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public void PatientSurnameWard_DoesNotReadAsHospitalWard()
    {
        var result = Card(
            scenarioTitle: "Sharing a new diagnosis of colorectal cancer",
            patientName: "Mrs Hannah Ward",
            background: "Mrs Ward underwent a colonoscopy two weeks ago. Biopsy results have confirmed adenocarcinoma.");

        Assert.Equal("Second Visit / Follow-up", result.Primary);
        Assert.Contains("Breaking Bad News", result.SecondaryTags);
    }

    [Fact]
    public void EmptyOrAmbiguousCard_IsOtherCardsAndReviewFlag()
    {
        var result = Card(
            scenarioTitle: "General consultation",
            background: "Talk to the patient about their health.");

        Assert.Equal("Other Cards", result.Primary);
        Assert.True(result.NeedsReview);
    }

    // ── 2026-09-09 classifier repair — regressions named in the spec ────────

    [Fact]
    public void HistoricalSurgeryMention_DuringFirstVisit_IsNotAlreadyKnownPatient()
    {
        // Bare "surgery"/"operation" must no longer trigger Already Known
        // Patient — only an active peri-operative/inpatient setting does.
        var result = Card(
            scenarioTitle: "First visit for hernia assessment",
            background: "Mr Okafor attends the clinic for the first time to discuss a possible hernia repair. "
                + "He had abdominal surgery 3 years ago for an unrelated condition.",
            tasks: new[] { "Take a history", "Discuss options for surgery" });

        Assert.Equal("First Visit", result.Primary);
        Assert.Equal("Q4-first-visit", result.RuleCode);
    }

    [Fact]
    public void SymptomDurationPhrase_DoesNotFalsePositiveAsKnownCare()
    {
        // "cough for 3 days" is ordinary first-visit history-taking, not an
        // ED/ward "known for hours/days" signal.
        var result = Card(
            scenarioTitle: "First visit with a persistent cough",
            background: "Ms Ibrahim attends for the first time with a cough for 3 days and a mild fever.",
            tasks: new[] { "Take a history", "Examine the chest" });

        Assert.Equal("First Visit", result.Primary);
    }

    [Fact]
    public void NewEdArrival_VersusManagedEd_ClassifiesCorrectly()
    {
        var justArrived = Card(
            scenarioTitle: "Chest pain",
            setting: "Emergency Department",
            background: "Mr Petrov has just arrived in the Emergency Department with sudden chest pain.",
            tasks: new[] { "Take a focused history" });
        Assert.Equal("Emergency / Emergency Department", justArrived.Primary);
        Assert.Equal("Q2-ed-arrival", justArrived.RuleCode);

        var alreadyManaged = Card(
            scenarioTitle: "Chest pain follow-up",
            setting: "Emergency Department",
            background: "Mr Petrov has already been managed in the Emergency Department for several hours "
                + "and is now stable. Review his progress.",
            tasks: new[] { "Review progress" });
        Assert.Equal("Already Known Patient", alreadyManaged.Primary);
        Assert.Equal("Q3-known-care", alreadyManaged.RuleCode);
    }

    [Fact]
    public void NegatedAngryMention_DoesNotTagAngry()
    {
        var result = Card(
            scenarioTitle: "First visit review",
            background: "Ms Duval attends the clinic for the first time. She is not angry, just seeking reassurance about her results.",
            tasks: new[] { "Take a history", "Reassure the patient" });

        Assert.DoesNotContain("Angry", result.SecondaryTags);
    }

    [Fact]
    public void InitialDietaryConsultation_IsFirstVisit()
    {
        var result = Card(
            scenarioTitle: "Initial dietary consultation",
            background: "This is Mrs Farrow's initial dietary consultation following a referral for weight management advice.",
            tasks: new[] { "Take a diet history", "Discuss goals" });

        Assert.Equal("First Visit", result.Primary);
        Assert.Equal("Q4-first-visit", result.RuleCode);
    }

    [Fact]
    public void FirstPhysiotherapyAppointment_IsFirstVisit()
    {
        var result = Card(
            scenarioTitle: "First physiotherapy appointment",
            background: "Mr Sato attends his first physiotherapy appointment after a knee injury.",
            tasks: new[] { "Assess the knee", "Explain the treatment plan" });

        Assert.Equal("First Visit", result.Primary);
    }

    [Fact]
    public void Classification_ReturnsRuleCodeAndEvidenceForAuditing()
    {
        var result = Card(
            scenarioTitle: "Follow-up review",
            background: "Mr Lee is returning for a follow-up of his asthma control.",
            tasks: new[] { "Review inhaler technique" });

        Assert.Equal("Second Visit / Follow-up", result.Primary);
        Assert.Equal("Q5-follow-up", result.RuleCode);
        Assert.NotNull(result.Evidence);
    }

    [Fact]
    public void ApplyIfUnclassified_DoesNotOverwriteConfirmedFirstVisitCategory()
    {
        var card = new RolePlayCard
        {
            Id = "rpc-test-1",
            ContentItemId = "ci-test-1",
            ProfessionId = "nursing",
            ScenarioTitle = "First visit with an angry patient",
            Setting = "Clinic",
            Background = "Mrs Allen is attending the clinic for the first time with headaches. She is angry about the waiting time.",
            Tasks = new[] { "Take a history", "Acknowledge her concern about the wait" },
            PatientEmotion = "angry",
            CommunicationGoal = "Inform",
            ClinicalTopic = "headache",
            PrimaryCategory = "First Visit",
            SecondaryTagsJson = "[]",
            CategoryNeedsReview = false,
        };

        var changed = SpeakingCardClassifier.ApplyIfUnclassified(card);

        Assert.False(changed);
        Assert.Equal("First Visit", card.PrimaryCategory);
        Assert.False(card.CategoryNeedsReview);
    }

    [Fact]
    public void ApplyIfUnclassified_UsesClassificationForOtherCardsNeedsReview()
    {
        var card = new RolePlayCard
        {
            Id = "rpc-test-2",
            ContentItemId = "ci-test-2",
            ProfessionId = "nursing",
            ScenarioTitle = "First visit with an angry patient",
            Setting = "Clinic",
            Background = "Mrs Allen is attending the clinic for the first time with headaches. She is angry about the waiting time.",
            Tasks = new[] { "Take a history", "Acknowledge her concern about the wait" },
            PatientEmotion = "angry",
            CommunicationGoal = "Inform",
            ClinicalTopic = "headache",
            PrimaryCategory = "Other Cards",
            SecondaryTagsJson = "[]",
            CategoryNeedsReview = true,
        };

        var changed = SpeakingCardClassifier.ApplyIfUnclassified(card);

        Assert.True(changed);
        Assert.Equal("First Visit", card.PrimaryCategory);
        Assert.Contains("Angry", System.Text.Json.JsonSerializer.Deserialize<string[]>(card.SecondaryTagsJson)!);
        Assert.False(card.CategoryNeedsReview);
    }
}
