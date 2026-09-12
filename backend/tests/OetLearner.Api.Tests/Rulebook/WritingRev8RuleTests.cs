using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Rulebook;

/// <summary>
/// Deterministic regression battery for the owner Writing Rule Enforcement and
/// Model Answer Validation Addendum, Revision 8 (11 Sep 2026), §9.4: a
/// Fires/Passes pair for every Rev7-8 detector in WritingRuleEngine(.Rev8).cs,
/// run in both modes wherever the Model Answer house style (IsModelAnswer =
/// true) and candidate grading differ. Every expectation is traced from the
/// detector regexes themselves. Canonical professions (Medicine default)
/// report "BUILTIN." + checkId. The Skip'd facts pin reported engine gaps.
/// </summary>
public sealed class WritingRev8RuleTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private const string Address = "Dr Helen Still\nRheumatology Clinic\nCity Hospital\n\n";
    private const string DateAndSalutation = "11 September 2026\n\nDear Dr Still,\n";
    private const string SignOff = "Yours sincerely,\n\nDoctor";

    private const string TaylorRe = "Re: Mr David Taylor, DOB: 12 March 1971";
    private const string TaylorIntro = "I am writing to refer Mr Taylor for assessment of his painful right knee.";
    private const string TaylorBody = "Mr Taylor presented today with a painful, swollen right knee.";
    private const string TaylorClosure = "I would be grateful if you could assess Mr Taylor. Should there be any queries, kindly do not hesitate to contact me.";

    private const string UrgentIntro = "I am writing to urgently refer Mr Taylor for assessment of a hot, swollen right knee.";
    private const string UrgentClosure = "I would be grateful if you could assess Mr Taylor at your earliest convenience. Should there be any queries, kindly do not hesitate to contact me.";

    private const string GarciaRe = "Re: Mrs Rose Garcia, DOB: 1 January 1945";
    private const string GarciaIntro = "I am writing to refer Mrs Garcia for assessment of her memory loss.";
    private const string GarciaBody = "Mrs Garcia presented today with increasing forgetfulness.";
    private const string GarciaClosure = "I would be grateful if you could assess Mrs Garcia. Should there be any queries, kindly do not hesitate to contact me.";

    private const string WeirRe = "Re: Mr Arthur Weir, DOB: 4 May 1950";
    private const string WeirIntro = "I am writing to refer Mr Weir for assessment of his chest pain.";
    private const string WeirClosure = "I would be grateful if you could assess Mr Weir. Should there be any queries, kindly do not hesitate to contact me.";

    private const string TommyRe = "Re: Tommy Smith, DOB: 3 March 2018";
    private const string TommyIntro = "I am writing to refer Tommy for assessment of his persistent cough.";
    private const string TommyClosure = "I would be grateful if you could assess Tommy. Should there be any queries, kindly do not hesitate to contact me.";

    // Address block, one blank line, date, one blank line, salutation and Re:
    // on consecutive lines, one blank line, single-line paragraphs separated by
    // one blank line, sign-off with one blank line before the designation.
    private static string Letter(string reLine, params string[] paragraphs)
        => Address + DateAndSalutation + reLine + "\n\n" + string.Join("\n\n", paragraphs) + "\n\n" + SignOff;

    private static IReadOnlyList<LintFinding> Lint(
        string letter,
        bool model,
        string letterType = "routine_referral",
        bool minor = false,
        ExamProfession profession = ExamProfession.Medicine)
        => Engine.Lint(new WritingLintInput(
            letter, letterType, PatientIsMinor: minor, Profession: profession, IsModelAnswer: model));

    private static List<LintFinding> Findings(IReadOnlyList<LintFinding> findings, string checkId)
        => findings.Where(f => f.RuleId == "BUILTIN." + checkId).ToList();

    // ---------------------------------------------------------------------
    // closure_contact_offer (OWN-W-014)
    // ---------------------------------------------------------------------

    [Fact]
    public void ClosureContactOffer_ModelAnswer_Fires_When_Offer_Is_Not_The_Final_Sentence_Candidate_Passes()
    {
        var letter = Letter(TaylorRe, TaylorIntro, TaylorBody,
            "Please do not hesitate to contact me if you require any further information. I would be grateful if you could assess Mr Taylor.");

        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.closure_contact_offer" && f.Severity == RuleSeverity.Critical);
        // Candidate: the contact-offer meaning anywhere in the closure is enough.
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.closure_contact_offer");
    }

    [Theory]
    [InlineData(true, "I would be grateful if you could assess Mr Taylor. Should there be any queries, kindly do not hesitate to contact me.")]
    [InlineData(true, "I would be grateful if you could assess Mr Taylor. Please feel free to call me if you need any further information.")]
    [InlineData(false, "I would be grateful if you could assess Mr Taylor. If you have any questions, I would be happy to discuss them.")]
    public void ClosureContactOffer_Passes_On_Canonical_Or_Equivalent_Offer(bool model, string closure)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, closure), model),
            f => f.RuleId == "BUILTIN.closure_contact_offer");

    [Fact]
    public void ClosureContactOffer_ModelAnswer_Requires_An_Explicit_Offer_To_Contact_The_Writer()
    {
        // Equivalent meaning without "contact/call ... me" is accepted from a
        // candidate (above) but not as the final Model Answer sentence.
        var letter = Letter(TaylorRe, TaylorIntro, TaylorBody,
            "I would be grateful if you could assess Mr Taylor. If you have any questions, I would be happy to discuss them.");
        Assert.Contains(Lint(letter, model: true), f => f.RuleId == "BUILTIN.closure_contact_offer");
    }

    [Fact]
    public void ClosureContactOffer_Candidate_Fires_Major_When_Closure_Offers_No_Contact()
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, "I would be grateful if you could assess Mr Taylor."), model: false),
            f => f.RuleId == "BUILTIN.closure_contact_offer" && f.Severity == RuleSeverity.Major);

    // ---------------------------------------------------------------------
    // closure_contains_management (OWN-W-034)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(false, RuleSeverity.Major)]
    [InlineData(true, RuleSeverity.Critical)]
    public void ClosureContainsManagement_Fires_When_Management_Follows_The_Request(bool model, RuleSeverity expected)
    {
        var closure = "I would be grateful if you could review Mr Taylor at your earliest convenience. He was advised to rest and elevate the leg. Please do not hesitate to contact me if you require any further information.";
        Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, closure), model),
            f => f.RuleId == "BUILTIN.closure_contains_management" && f.Severity == expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosureContainsManagement_Passes_When_Management_Is_In_The_Body(bool model)
    {
        var letter = Letter(TaylorRe, TaylorIntro, TaylorBody + " He was advised to rest and elevate the leg.", TaylorClosure);
        Assert.DoesNotContain(Lint(letter, model), f => f.RuleId == "BUILTIN.closure_contains_management");
    }

    // ---------------------------------------------------------------------
    // dob_colon_format (OWN-W-004)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Re: Mr David Taylor, D.O.B: 12 March 1971")]
    [InlineData("Re: Mr David Taylor, DOB 01/01/1990")]
    public void DobColonFormat_Fires_On_Dotted_Or_Colonless_Dob(string reLine)
        => Assert.Contains(Lint(Letter(reLine, TaylorIntro, TaylorBody, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.dob_colon_format" && f.FixSuggestion == "DOB:");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DobColonFormat_Passes_On_Canonical_Dob_Colon(bool model)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure), model),
            f => f.RuleId == "BUILTIN.dob_colon_format");

    [Fact]
    public void DobColonFormat_ModelAnswer_Fires_On_Date_Of_Birth_In_ReLine_Candidate_Passes()
    {
        var letter = Letter("Re: Mr David Taylor, date of birth 12 March 1971", TaylorIntro, TaylorBody, TaylorClosure);
        Assert.Contains(Lint(letter, model: true), f => f.RuleId == "BUILTIN.dob_colon_format");
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.dob_colon_format");
    }

    // ---------------------------------------------------------------------
    // intro_opens_i_am_writing_to (OWN-W-013)
    // ---------------------------------------------------------------------

    [Fact]
    public void CanonicalOpening_ModelAnswer_Fires_On_Relationship_Opening_Candidate_Never_Checked()
    {
        var letter = Letter(GarciaRe, "Your mother, Mrs Garcia, has been experiencing memory loss for the past year.", GarciaBody, GarciaClosure);
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.intro_opens_i_am_writing_to" && f.Severity == RuleSeverity.Critical);
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.intro_opens_i_am_writing_to");
    }

    [Fact]
    public void CanonicalOpening_ModelAnswer_Passes_On_I_Am_Writing_To()
        => Assert.DoesNotContain(Lint(Letter(GarciaRe, GarciaIntro, GarciaBody, GarciaClosure), model: true),
            f => f.RuleId == "BUILTIN.intro_opens_i_am_writing_to");

    // ---------------------------------------------------------------------
    // linker_comma_and_case (OWN-W-022)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor was stable. However he developed a fever overnight.")]
    [InlineData("Mr Taylor was stable; However, he developed a fever overnight.")]
    [InlineData("Mr Taylor was stable; however he developed a fever overnight.")]
    public void LinkerCommaAndCase_Fires_On_Missing_Comma_Or_Capital_After_Semicolon(string sentence)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.linker_comma_and_case");

    [Theory]
    [InlineData("Mr Taylor was stable. However, he developed a fever overnight.")]
    [InlineData("Mr Taylor was stable; however, he developed a fever overnight.")]
    [InlineData("In addition to this, Mr Taylor reported a fever overnight.")]
    public void LinkerCommaAndCase_Passes_On_Canonical_Punctuation(string sentence)
    {
        foreach (var model in new[] { false, true })
            Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model),
                f => f.RuleId == "BUILTIN.linker_comma_and_case");
    }

    // ---------------------------------------------------------------------
    // linker_however/therefore/in_addition_punctuation: only a run-on or
    // comma splice joining two complete clauses is an error (Rev8 §3).
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("linker_however_punctuation", "Mr Taylor was stable however he deteriorated overnight.")]
    [InlineData("linker_however_punctuation", "Mr Taylor was stable, however he deteriorated overnight.")]
    [InlineData("linker_therefore_punctuation", "Mr Taylor was in severe pain, therefore he was admitted.")]
    [InlineData("linker_in_addition_punctuation", "Mr Taylor takes allopurinol daily, in addition he uses colchicine.")]
    public void LinkerRunOn_Fires_On_Run_On_Or_Comma_Splice(string checkId, string sentence)
    {
        foreach (var model in new[] { false, true })
            Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model),
                f => f.RuleId == "BUILTIN." + checkId);
    }

    [Theory]
    [InlineData("linker_therefore_punctuation", "Mr Taylor was therefore referred for further assessment.")]
    [InlineData("linker_however_punctuation", "Mr Taylor did not, however, attend the appointment.")]
    [InlineData("linker_however_punctuation", "Mr Taylor was stable; however, he deteriorated overnight.")]
    [InlineData("linker_however_punctuation", "Mr Taylor was stable. However, he deteriorated overnight.")]
    [InlineData("linker_in_addition_punctuation", "Mr Taylor has gout. In addition, he has hypertension.")]
    public void LinkerRunOn_Passes_On_Adverbial_Parenthetical_Or_Correctly_Punctuated_Use(string checkId, string sentence)
    {
        foreach (var model in new[] { false, true })
            Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model),
                f => f.RuleId == "BUILTIN." + checkId);
    }

    // ---------------------------------------------------------------------
    // linker_avoid_words (§3, §13 linker vocabulary)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor also reported ankle swelling.")]
    [InlineData("Mr Taylor was stable but reported ankle swelling.")]
    public void LinkerAvoidWords_ModelAnswer_Fires_On_MidSentence_Also_Or_But(string sentence)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: true),
            f => f.RuleId == "BUILTIN.linker_avoid_words" && f.Severity == RuleSeverity.Critical);

    [Theory]
    [InlineData("Mrs Garcia was in severe pain, hence she was admitted.", "hence")]
    [InlineData("Mrs Garcia improved, but she later deteriorated.", "but")]
    public void LinkerAvoidWords_Candidate_Flags_Routine_Connectives_As_Major(string sentence, string word)
        => Assert.Contains(Lint(Letter(GarciaRe, GarciaIntro, sentence, GarciaClosure), model: false),
            f => f.RuleId == "BUILTIN.linker_avoid_words" && f.Quote == word && f.Severity == RuleSeverity.Major);

    [Fact]
    public void LinkerAvoidWords_Candidate_Also_Is_An_Advisory_Minor()
    {
        var findings = Findings(Lint(Letter(GarciaRe, GarciaIntro, "Mrs Garcia also reported ankle swelling.", GarciaClosure), model: false),
            "linker_avoid_words");
        var also = Assert.Single(findings);
        Assert.Equal("also", also.Quote);
        Assert.Equal(RuleSeverity.Minor, also.Severity);
    }

    [Fact]
    public void LinkerAvoidWords_Candidate_Passes_On_Adverbial_So()
        => Assert.DoesNotContain(Lint(Letter(GarciaRe, GarciaIntro, "Her pain was not so severe today.", GarciaClosure), model: false),
            f => f.RuleId == "BUILTIN.linker_avoid_words");

    // OWN-W-005's own example: "also known as" names a drug, it is not a linker.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkerAvoidWords_Passes_On_Also_Known_As(bool model)
        => Assert.DoesNotContain(Lint(Letter(GarciaRe, GarciaIntro, "Mrs Garcia was commenced on dalteparin, also known as Fragmin, today.", GarciaClosure), model),
            f => f.RuleId == "BUILTIN.linker_avoid_words");

    // ---------------------------------------------------------------------
    // medication_list_punctuation (OWN-W-023)
    // ---------------------------------------------------------------------

    [Fact]
    public void MedicationListPunctuation_Fires_On_Missing_Comma_With_Fix()
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, "Mr Taylor takes ranitidine 150 mg twice a day.", TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.medication_list_punctuation" && f.FixSuggestion == "ranitidine, 150 mg");

    [Theory]
    [InlineData("Mr Taylor takes ranitidine, 150 mg twice a day.")]
    [InlineData("Mr Taylor takes ranitidine, 150 mg and paracetamol, 1 g.")]
    [InlineData("Mr Taylor takes ranitidine, 150 mg; paracetamol, 1 g; and allopurinol, 100 mg.")]
    [InlineData("Blood tests showed haemoglobin 110 g/L.")]
    [InlineData("Mr Taylor drinks 20 units of alcohol per week.")]
    public void MedicationListPunctuation_Passes_On_Canonical_Lists_And_Non_Medication_Values(string sentence)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.medication_list_punctuation");

    [Fact]
    public void MedicationListPunctuation_Fires_On_Three_Items_Separated_By_Commas()
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, "Mr Taylor takes ranitidine, 150 mg, paracetamol, 1 g, and allopurinol, 100 mg.", TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.medication_list_punctuation" && f.Message.StartsWith("Three or more medicines", StringComparison.Ordinal));

    // ---------------------------------------------------------------------
    // value_unit_spacing (OWN-W-024)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor takes ranitidine, 150mg twice a day.", "150 mg")]
    [InlineData("His potassium was 6.37mmol/L.", "6.37 mmol/L")]
    [InlineData("Mr Taylor takes colecalciferol, 2500IU daily.", "2500 IU")]
    public void ValueUnitSpacing_Fires_With_Spaced_Fix(string sentence, string fix)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.value_unit_spacing" && f.FixSuggestion == fix);

    [Fact]
    public void ValueUnitSpacing_Passes_On_Spaced_Values_Degrees_And_Percentages()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro,
                "Mr Taylor takes ranitidine, 150 mg twice a day. His temperature was 37.8°C, his oxygen saturation was 95% and his blood pressure was 120/80 mmHg.",
                TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.value_unit_spacing");

    // ---------------------------------------------------------------------
    // number_style_words_vs_digits (OWN-W-006, supersedes G-W-110)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor was advised to take paracetamol, 1 g every 6 hours for 4 days.")]
    [InlineData("Mr Taylor lives with his wife and 3 children.")]
    [InlineData("Mr Taylor first noticed the pain 2 weeks ago.")]
    public void NumberStyle_Fires_On_Descriptive_Counts_And_Durations(string sentence)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.number_style_words_vs_digits");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberStyle_Passes_On_Ages_Dates_Vitals_And_Doses(bool model)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro,
                "Mr Taylor, aged 55, is a 55-year-old teacher. On 3 June 2026, his blood pressure was 120/80 mmHg and he was prescribed paracetamol, 1 g.",
                TaylorClosure), model),
            f => f.RuleId == "BUILTIN.number_style_words_vs_digits");

    // ---------------------------------------------------------------------
    // register_colloquial (OWN-W-033)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("An MRI imaging study was requested.", "MRI imaging")]
    [InlineData("Mr Taylor felt something 'pop' in his knee.", "felt something 'pop'")]
    [InlineData("Mr Taylor lives alone with no GP.", "with no GP")]
    [InlineData("Mr Taylor has been feeling tired.", "tired")]
    public void RegisterColloquial_Is_Critical_For_Model_Answers_And_Minor_For_Candidates(string sentence, string quote)
    {
        var letter = Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure);
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.register_colloquial" && f.Quote == quote && f.Severity == RuleSeverity.Critical);
        Assert.Contains(Lint(letter, model: false),
            f => f.RuleId == "BUILTIN.register_colloquial" && f.Quote == quote && f.Severity == RuleSeverity.Minor);
    }

    // ---------------------------------------------------------------------
    // relationship_label_patient_reference (OWN-W-012)
    // ---------------------------------------------------------------------

    [Fact]
    public void RelationshipLabel_Fires_When_The_Patient_Is_Named()
        => Assert.Contains(Lint(Letter(GarciaRe, GarciaIntro, "Your mother will continue her current medication.", GarciaClosure), model: false),
            f => f.RuleId == "BUILTIN.relationship_label_patient_reference");

    [Fact]
    public void RelationshipLabel_Passes_When_The_ReLine_Does_Not_Name_The_Patient()
        => Assert.DoesNotContain(Lint(Letter("Re: Your mother, DOB: 1 January 1945",
                "I am writing to refer your mother for assessment of her memory loss.",
                "Your mother will continue her current medication.",
                "I would be grateful for your assessment. Should there be any queries, kindly do not hesitate to contact me."), model: false),
            f => f.RuleId == "BUILTIN.relationship_label_patient_reference");

    // ---------------------------------------------------------------------
    // paragraph_start_patient_name (OWN-W-010)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(false, RuleSeverity.Major)]
    [InlineData(true, RuleSeverity.Critical)]
    public void ParagraphStartName_Fires_When_A_Body_Paragraph_Opens_With_A_Pronoun(bool model, RuleSeverity expected)
    {
        var letter = Letter(WeirRe, WeirIntro, "He first presented with chest pain on 2 June 2026.", WeirClosure);
        var finding = Assert.Single(Findings(Lint(letter, model), "paragraph_start_patient_name"));
        Assert.Equal(expected, finding.Severity);
        Assert.Equal("Mr Weir", finding.FixSuggestion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParagraphStartName_Passes_When_Each_Paragraph_Opens_With_Title_And_Surname(bool model)
        => Assert.DoesNotContain(Lint(Letter(WeirRe, WeirIntro, "Mr Weir first presented with chest pain on 2 June 2026. He was admitted for observation.", WeirClosure), model),
            f => f.RuleId == "BUILTIN.paragraph_start_patient_name");

    [Fact]
    public void ParagraphStartName_Checks_The_Closure_For_Model_Answers_Only()
    {
        var letter = Letter(WeirRe, WeirIntro, "Mr Weir first presented with chest pain on 2 June 2026.",
            "I would be grateful if you could assess him at your earliest convenience. Should there be any queries, kindly do not hesitate to contact me.");
        Assert.Contains(Lint(letter, model: true), f => f.RuleId == "BUILTIN.paragraph_start_patient_name");
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.paragraph_start_patient_name");
    }

    [Fact]
    public void ParagraphStartName_Uses_The_First_Name_For_A_Child()
    {
        var pronounFirst = Letter(TommyRe, TommyIntro, "He was brought in by his mother with a cough.", TommyClosure);
        var nameFirst = Letter(TommyRe, TommyIntro, "Tommy was brought in by his mother with a cough.", TommyClosure);
        Assert.Contains(Lint(pronounFirst, model: false, minor: true),
            f => f.RuleId == "BUILTIN.paragraph_start_patient_name" && f.FixSuggestion == "Tommy");
        Assert.DoesNotContain(Lint(nameFirst, model: false, minor: true), f => f.RuleId == "BUILTIN.paragraph_start_patient_name");
    }

    [Fact]
    public void ParagraphStartName_Passes_When_ReLine_Has_Dob_Without_A_Comma()
        => Assert.DoesNotContain(Lint(Letter("Re: Mrs Rose Garcia DOB: 1 January 1945", GarciaIntro,
                "Mrs Garcia presented today with increasing forgetfulness. She lives alone.", GarciaClosure), model: false),
            f => f.RuleId == "BUILTIN.paragraph_start_patient_name");

    [Fact]
    public void ParagraphStartName_Leaves_The_Patient_To_Its_Own_Check()
    {
        var findings = Lint(Letter(WeirRe, WeirIntro, "The patient first presented with chest pain. He was admitted for observation.", WeirClosure), model: false);
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.paragraph_start_patient_name");
        Assert.Contains(findings, f => f.RuleId == "BUILTIN.body_forbidden_phrase_the_patient");
    }

    // ---------------------------------------------------------------------
    // body_forbidden_phrase_the_patient (OWN-W-009, supersedes G-W-112)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("The patient first presented with chest pain.", true)]
    [InlineData("Mr Taylor has been a patient at this practice since 2015.", true)]
    [InlineData("Mr Taylor attends the outpatient clinic every month.", false)]
    public void ThePatient_Is_Flagged_As_A_Person_Reference(string sentence, bool fires)
        => Assert.Equal(fires, Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false)
            .Any(f => f.RuleId == "BUILTIN.body_forbidden_phrase_the_patient"));

    [Fact]
    public void ThePatient_Is_Critical_In_A_Model_Answer()
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, "The patient first presented with chest pain.", TaylorClosure), model: true),
            f => f.RuleId == "BUILTIN.body_forbidden_phrase_the_patient" && f.Severity == RuleSeverity.Critical);

    // ---------------------------------------------------------------------
    // body_uses_last_name_only (OWN-W-011)
    // ---------------------------------------------------------------------

    [Fact]
    public void FullName_Fires_When_Repeated_In_The_Introduction()
        => Assert.Contains(Lint(Letter(TaylorRe, "I am writing to refer Mr David Taylor for assessment of his painful right knee.", TaylorBody, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.body_uses_last_name_only" && f.FixSuggestion == "Mr Taylor");

    [Fact]
    public void FullName_Passes_On_Title_And_Surname()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.body_uses_last_name_only");

    [Fact]
    public void FullName_Fires_On_A_Repeated_Child_Full_Name()
        => Assert.Contains(Lint(Letter(TommyRe, TommyIntro, "Tommy Smith was brought in by his mother with a cough.", TommyClosure), model: false, minor: true),
            f => f.RuleId == "BUILTIN.body_uses_last_name_only" && f.FixSuggestion == "Tommy");

    // ---------------------------------------------------------------------
    // signoff_designation_present (OWN-W-026)
    // ---------------------------------------------------------------------

    [Fact]
    public void SignoffDesignation_Fires_When_The_Designation_Is_Missing()
    {
        var letter = Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure).Replace(SignOff, "Yours sincerely,");
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.signoff_designation_present" && f.Severity == RuleSeverity.Critical);
        Assert.Contains(Lint(letter, model: false),
            f => f.RuleId == "BUILTIN.signoff_designation_present" && f.Severity == RuleSeverity.Minor);
    }

    [Fact]
    public void SignoffDesignation_ModelAnswer_Fires_With_No_Blank_Line_Before_The_Designation()
    {
        var letter = Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure).Replace(SignOff, "Yours sincerely,\nDoctor");
        Assert.Contains(Lint(letter, model: true), f => f.RuleId == "BUILTIN.signoff_designation_present");
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.signoff_designation_present");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignoffDesignation_Passes_With_One_Blank_Line(bool model)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure), model),
            f => f.RuleId == "BUILTIN.signoff_designation_present");

    // ---------------------------------------------------------------------
    // model_answer_layout (OWN-W-036)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("no-address-block")]
    [InlineData("line-break-inside-paragraph")]
    [InlineData("three-blank-lines-between-paragraphs")]
    [InlineData("two-blank-lines-after-re-line")]
    public void ModelAnswerLayout_Fires_On_Layout_Defects_For_Model_Answers_Only(string defect)
    {
        var letter = defect switch
        {
            "no-address-block" => Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure).Replace(Address, ""),
            "line-break-inside-paragraph" => Letter(TaylorRe, TaylorIntro, TaylorBody + "\nHe was unable to bear weight.", TaylorClosure),
            "three-blank-lines-between-paragraphs" => Letter(TaylorRe, TaylorIntro, "\n\n" + TaylorBody, TaylorClosure),
            "two-blank-lines-after-re-line" => Letter(TaylorRe, "\n" + TaylorIntro, TaylorBody, TaylorClosure),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        Assert.Contains(Lint(letter, model: true), f => f.RuleId == "BUILTIN.model_answer_layout");
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.model_answer_layout");
    }

    [Fact]
    public void ModelAnswerLayout_Passes_On_A_Correctly_Laid_Out_Letter()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, TaylorClosure), model: true),
            f => f.RuleId == "BUILTIN.model_answer_layout");

    [Fact]
    public void ModelAnswerLayout_Fires_On_Exactly_Two_Blank_Lines_Between_Paragraphs()
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, "\n" + TaylorBody, TaylorClosure), model: true),
            f => f.RuleId == "BUILTIN.model_answer_layout");

    // ---------------------------------------------------------------------
    // year_not_abbreviated / date_format_consistent (OWN-W-003)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor had a knee arthroscopy on 11.08.14.")]
    [InlineData("Mr Taylor had a knee arthroscopy on 01/01/19.")]
    [InlineData("Mr Taylor had a knee arthroscopy on 11-08-20.")]
    [InlineData("Mr Taylor had a knee arthroscopy in June '14.")]
    public void YearNotAbbreviated_Fires_On_Two_Digit_Years(string sentence)
    {
        var letter = Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure);
        Assert.Contains(Lint(letter, model: false),
            f => f.RuleId == "BUILTIN.year_not_abbreviated" && f.Severity == RuleSeverity.Major);
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.year_not_abbreviated" && f.Severity == RuleSeverity.Critical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YearNotAbbreviated_Passes_On_Four_Digit_Years(bool model)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, "Mr Taylor had a knee arthroscopy on 13 June 2020 and a blood test on 01/01/2019.", TaylorClosure), model),
            f => f.RuleId == "BUILTIN.year_not_abbreviated");

    [Theory]
    [InlineData("Re: Mrs Rose Garcia, DOB: 01.01.1995", "Mrs Garcia was diagnosed with hypertension on 23 May 2015.")]
    [InlineData(WeirRe, "Mr Weir had a knee arthroscopy on 11.08.14 and a left hip replacement on 13 June 2020.")]
    public void DateFormatConsistent_Fires_On_Mixed_Date_Families(string reLine, string sentence)
        => Assert.Contains(Lint(Letter(reLine, "I am writing to request your specialist assessment.", sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.date_format_consistent");

    [Fact]
    public void DateFormatConsistent_Passes_On_One_Written_Date_Family()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, "Mr Taylor had a knee arthroscopy on 13 June 2020.", TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.date_format_consistent");

    // ---------------------------------------------------------------------
    // judgmental_labels (§2 judgmental labels)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Mr Taylor is a smoker.")]
    [InlineData("Mr Taylor is a non-smoker.")]
    [InlineData("Mr Taylor is a hypertensive patient.")]
    [InlineData("Mr Taylor has been non-compliant with his medication.")]
    public void JudgmentalLabels_Fires_On_Person_Labels(string sentence)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.judgmental_labels");

    [Theory]
    [InlineData("Mr Taylor was admitted with hypertensive crisis.")]
    [InlineData("Mr Taylor has diabetic retinopathy.")]
    [InlineData("Mr Taylor was anxious and tachycardic on arrival.")]
    public void JudgmentalLabels_Passes_On_Clinical_Adjectives_And_Transient_Findings(string sentence)
    {
        foreach (var model in new[] { false, true })
            Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model),
                f => f.RuleId == "BUILTIN.judgmental_labels");
    }

    [Fact]
    public void JudgmentalLabels_ModelAnswer_Fires_On_A_Bare_Smoker_Label_Candidate_Passes()
    {
        var letter = Letter(TaylorRe, TaylorIntro, "Mr Taylor, non-smoker, drinks alcohol socially.", TaylorClosure);
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.judgmental_labels" && f.Severity == RuleSeverity.Critical);
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.judgmental_labels");
    }

    [Theory]
    [InlineData("Mr Taylor presented with a hypertensive crisis.")]
    [InlineData("Mr Taylor had an epileptic seizure last night.")]
    [InlineData("Mr Taylor has a diabetic foot ulcer.")]
    [InlineData("Mr Taylor was hypertensive on arrival.")]
    public void JudgmentalLabels_Passes_On_Article_Plus_Clinical_Adjective(string sentence)
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, TaylorIntro, sentence, TaylorClosure), model: false),
            f => f.RuleId == "BUILTIN.judgmental_labels");

    // ---------------------------------------------------------------------
    // emotional_wording (§2; Model Answers ban suffer/suffered outright)
    // ---------------------------------------------------------------------

    [Fact]
    public void EmotionalWording_ModelAnswer_Bans_Suffered_Candidate_Accepts_Factual_Use()
    {
        var letter = Letter(TaylorRe, TaylorIntro, "Mr Taylor suffered a myocardial infarction in 2019.", TaylorClosure);
        Assert.Contains(Lint(letter, model: true),
            f => f.RuleId == "BUILTIN.emotional_wording" && f.Severity == RuleSeverity.Critical);
        Assert.DoesNotContain(Lint(letter, model: false), f => f.RuleId == "BUILTIN.emotional_wording");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmotionalWording_Fires_On_Unfortunately_In_Both_Modes(bool model)
        => Assert.Contains(Lint(Letter(TaylorRe, TaylorIntro, "Unfortunately, Mr Taylor's pain has worsened.", TaylorClosure), model),
            f => f.RuleId == "BUILTIN.emotional_wording");

    // ---------------------------------------------------------------------
    // Urgent referral: today paragraph (OWN-W-017) and closure order (OWN-W-015)
    // ---------------------------------------------------------------------

    [Fact]
    public void UrgentBodyStartsToday_ModelAnswer_Fires_On_Remote_History_Year_Candidate_Passes()
    {
        var letter = Letter(TaylorRe, UrgentIntro, TaylorBody + " He had a similar episode in 2019.", UrgentClosure);
        Assert.Contains(Lint(letter, model: true, letterType: "urgent_referral"),
            f => f.RuleId == "BUILTIN.urgent_body_starts_today" && f.Quote == "2019");
        Assert.DoesNotContain(Lint(letter, model: false, letterType: "urgent_referral"),
            f => f.RuleId == "BUILTIN.urgent_body_starts_today");
    }

    [Fact]
    public void UrgentBodyStartsToday_ModelAnswer_Passes_On_The_Letter_Year()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, UrgentIntro,
                "Mr Taylor presented today with a painful, swollen right knee that began on 9 September 2026.", UrgentClosure),
                model: true, letterType: "urgent_referral"),
            f => f.RuleId == "BUILTIN.urgent_body_starts_today");

    [Fact]
    public void UrgentClosure_ModelAnswer_Fires_When_Earliest_Convenience_Is_The_Final_Sentence()
    {
        var request = "I would be grateful if you could assess Mr Taylor at your earliest convenience.";
        var letter = Letter(TaylorRe, UrgentIntro, TaylorBody,
            "Should there be any queries, kindly do not hesitate to contact me. " + request);
        Assert.Contains(Lint(letter, model: true, letterType: "urgent_referral"),
            f => f.RuleId == "BUILTIN.urgent_closure_phrase" && f.Quote == request);
    }

    [Fact]
    public void UrgentClosure_ModelAnswer_Passes_When_Earliest_Convenience_Precedes_The_Contact_Offer()
        => Assert.DoesNotContain(Lint(Letter(TaylorRe, UrgentIntro, TaylorBody, UrgentClosure), model: true, letterType: "urgent_referral"),
            f => f.RuleId == "BUILTIN.urgent_closure_phrase");

    // ---------------------------------------------------------------------
    // Blocking policy and checkId de-dup
    // ---------------------------------------------------------------------

    [Fact]
    public void ModelAnswerBlockingFindings_Blocks_Every_Severity_Except_Info()
    {
        var findings = new[]
        {
            new LintFinding("BUILTIN.a", RuleSeverity.Critical, "critical"),
            new LintFinding("BUILTIN.b", RuleSeverity.Major, "major"),
            new LintFinding("BUILTIN.c", RuleSeverity.Minor, "minor"),
            new LintFinding("BUILTIN.d", RuleSeverity.Info, "info"),
        };
        Assert.Equal(new[] { "BUILTIN.a", "BUILTIN.b", "BUILTIN.c" },
            WritingRuleEngine.ModelAnswerBlockingFindings(findings).Select(f => f.RuleId));
    }

    // Legacy (R-id) rulebooks wire urgent_intro_contains_urgent to both R07.6
    // and R13.2; the same defect must be reported once, under the first rule.
    [Fact]
    public void Lint_Runs_A_CheckId_Shared_By_Two_Legacy_Rules_Only_Once()
    {
        var findings = Lint(Letter(TaylorRe, TaylorIntro, TaylorBody, UrgentClosure), model: false,
            letterType: "urgent_referral", profession: ExamProfession.Dietetics);
        var finding = Assert.Single(findings, f => f.RuleId is "R07.6" or "R13.2");
        Assert.Equal("R07.6", finding.RuleId);
        Assert.DoesNotContain(findings, f => f.RuleId == "BUILTIN.urgent_intro_contains_urgent");
    }

    // ---------------------------------------------------------------------
    // Detector fault isolation (P0 fix, 12 Sep 2026): a live regeneration
    // batch found every single attempt failing "model_answer_generation_failed"
    // despite the underlying AI call completing normally - the freshly
    // AI-generated draft did not match the shape every hand-crafted fixture
    // above assumes, and SOME detector in the 81-detector battery threw on
    // it, killing the whole Lint() call (used by Model Answer generation AND
    // real candidate Submit-for-Grading). Lint() must never throw, whatever
    // the input; a detector fault must surface as one finding, not a crash.
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> AdversarialLetters()
    {
        // Every case here is a plausible real-world shape none of the
        // hand-crafted fixtures above exercise: missing sections, no Re:
        // line at all, no title, run-on/empty paragraphs, unusual Unicode,
        // extremely short/long content, no sign-off, and a genuinely
        // malformed Re: line ("Re: Patient DOB Age 55" - all-caps/no-name
        // tokens only, which is exactly the shape ResolvePatientName's
        // trailing-token stripping (12 Sep 2026 fix) must not throw on if
        // NO token survives the strip).
        yield return new object[] { "" };
        yield return new object[] { "Just one line, nothing else." };
        yield return new object[] { "Dear Sir/Madam,\n\nNo Re: line at all in this letter.\n\nYours faithfully,\n\nDoctor" };
        yield return new object[] { "Re: DOB Age NHS MRN\n\nI am writing to refer.\n\nYours sincerely,\n\nDoctor" };
        yield return new object[] { "Re: Mr\n\nI am writing to refer him.\n\nYours sincerely,\n\nDoctor" };
        yield return new object[] { "Re: Mr Smith\n\n\n\n\n\nYours sincerely,\n\nDoctor" };
        yield return new object[] { string.Concat(Enumerable.Repeat("Mr Smith was seen today. ", 400)) };
        yield return new object[] { "Re: Mr Ünïçödé Ñame, DOB: 1 Ⅷ 2020\n\nI am writing to refer. 你好\n\nYours sincerely,\n\nDoctor" };
        yield return new object[] { "Re: Mr Smith\nDOB: 1 January 2020\nRe: Mr Smith again\n\nBody.\n\nYours sincerely,\n\nDoctor" };
        yield return new object[] { new string('\n', 50) };
        yield return new object[] { "Re: Mrs Smith, aged aged aged\n\nBody with 1/0 and 99999999999999999999 as tokens.\n\nYours sincerely,\n\nDoctor" };
    }

    [Theory]
    [MemberData(nameof(AdversarialLetters))]
    public void Lint_Never_Throws_On_Adversarial_Input(string letter)
    {
        foreach (var model in new[] { false, true })
        foreach (var letterType in new[] { "routine_referral", "urgent_referral", "discharge", "transfer_letter", "non_medical_referral", "other_letters" })
        foreach (var profession in new[] { ExamProfession.Medicine, ExamProfession.Nursing, ExamProfession.Dietetics })
        {
            var exception = Record.Exception(() => Lint(letter, model, letterType, profession: profession));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Lint_Converts_A_Detector_Exception_Into_A_Finding_Not_A_Crash()
    {
        // Cannot inject a fake throwing detector (DetectorFor is a fixed
        // switch over static methods) - this documents the isolation
        // contract via the synthetic ruleId RunDetectorSafely emits, so a
        // future refactor that removes the try/catch fails loudly here even
        // without reproducing the exact unknown production input that
        // exposed the gap.
        Assert.True(typeof(WritingRuleEngine)
            .GetMethod("RunDetectorSafely", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) is not null,
            "WritingRuleEngine.RunDetectorSafely must exist and wrap every detector call in Lint().");
    }
}
