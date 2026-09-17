using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Senior Assessor Release Audit (16 Sep 2026), group G3 — patient identity
/// regression classes, proved on the canonical fixture letters:
/// - G3a child naming: minor status comes from the DOB at the letter date
///   (0-17 is a child); a child has no title in the Re: line and is never
///   "title + surname" in the body.
/// - G3b adult first-name use: a known adult is never called by the first
///   name alone (audited pack, Erika Stone).
/// - G3c title/sex fidelity: the Re: line title agrees with the letter's own
///   pronouns.
/// - G3d age_dob_inconsistent: a stated age never contradicts the DOB at the
///   letter date (James Greenbaum, Barry Jones).
/// - G3e DOB priority: "was born on" + a date in the notes is a DOB (Mary Clarke).
/// - G3g sign-off: "Yours sincerely," alone, then ONE designation line
///   (Somarni Khaze, Louise Geller).
/// Every new branch is Model Answer only: each class also proves the
/// candidate lane is unchanged.
/// </summary>
public sealed class WritingSeniorAuditG3RegressionTests
{
    private static readonly WritingRuleEngine Engine = new(new RulebookLoader());

    private static List<LintFinding> Lint(string letter, string letterType,
        string? caseNotes = null, bool isModelAnswer = true, ExamProfession profession = ExamProfession.Medicine)
        => Engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: letterType,
            CaseNotesText: caseNotes,
            Profession: profession,
            IsModelAnswer: isModelAnswer)).ToList();

    private static void AssertRuleFires(List<LintFinding> findings, string checkId)
        => Assert.Contains(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static void AssertRuleDoesNotFire(List<LintFinding> findings, string checkId)
        => Assert.DoesNotContain(findings, f => f.RuleId.EndsWith(checkId, StringComparison.Ordinal));

    private static string Inject(string letter, string find, string replace)
    {
        var injected = letter.Replace(find, replace);
        Assert.NotEqual(letter, injected);
        return injected;
    }

    private const string Garcia = WritingRev8RegressionFixtureTests.GarciaUpdateLetter;
    private const string Weston = WritingRev8RegressionFixtureTests.WestonReferralLetter;
    private const string Weir = WritingRev8RegressionFixtureTests.WeirRoutineReferralLetter;
    private const string McDonald = WritingRev8RegressionFixtureTests.McDonaldTransferLetter;
    private const string Taylor = WritingRev8RegressionFixtureTests.TaylorUrgentReferralLetter;

    // Taylor fixture: letter dated 13 June 2020, DOB 1 August 1965 (aged 54).
    private const string TaylorRe = "Re: Mr David Taylor, DOB: 1 August 1965";
    private const string TaylorGoutSentence = "Mr Taylor has had gout since 2000";
    private const string TaylorSignoff = "Yours sincerely,\n\nDoctor";

    private const string MinorNaming = "minor_naming_convention";
    private const string AdultFirstName = "body_uses_last_name_only";
    private const string TitleMismatch = "patient_title_mismatch";
    private const string AgeDob = "age_dob_inconsistent";
    private const string DobPriority = "re_line_dob_priority";
    private const string Signoff = "signoff_designation_present";

    private static readonly string[] G3CheckIds = [MinorNaming, AdultFirstName, TitleMismatch, AgeDob, DobPriority, Signoff];

    private const string TaylorBornOnNotes =
        "Mr David Taylor was born on 1 August 1965 and is a patient in your general practice. " +
        "History of gout since 2000.";

    // ─── G3a — child naming: minor status from the DOB at the letter date ────

    [Theory]
    [InlineData("10 November 2003")] // Sally Webster's DOB: 16 at the letter date
    [InlineData("12 August 2011")]   // Amina Ahmed's DOB: 8 at the letter date
    [InlineData("14 June 2002")]     // one day short of 18: still a child
    public void SA_G3_ChildNaming_Titled_Re_Line_For_A_Dob_Derived_Minor_Fires(string dob)
    {
        var letter = Inject(Taylor, TaylorRe, "Re: Mr David Taylor, DOB: " + dob);
        AssertRuleFires(Lint(letter, "LT-UR"), MinorNaming);
    }

    [Fact]
    public void SA_G3_ChildNaming_Titled_Full_Name_In_The_Body_Of_A_Minor_Fires()
    {
        // The Re: line is the correct untitled child form, but the body still
        // gives the child an adult title (pack: "Ms Webster, a 16-year-old").
        var letter = Inject(Inject(Taylor, TaylorRe, "Re: David Taylor, DOB: 10 November 2003"),
            TaylorGoutSentence, "Mr David Taylor has had gout since 2000");
        AssertRuleFires(Lint(letter, "LT-UR"), MinorNaming);
    }

    [Fact]
    public void SA_G3_ChildNaming_Eighteenth_Birthday_On_The_Letter_Date_Is_An_Adult()
    {
        var letter = Inject(Taylor, TaylorRe, "Re: Mr David Taylor, DOB: 13 June 2002");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), MinorNaming);
    }

    [Fact]
    public void SA_G3_ChildNaming_Untitled_Re_Line_With_First_Name_Body_Passes()
    {
        var letter = Inject(Taylor, TaylorRe, "Re: David Taylor, DOB: 10 November 2003").Replace("Mr Taylor", "David");
        var findings = Lint(letter, "LT-UR");
        AssertRuleDoesNotFire(findings, MinorNaming);
        // A child's first name is correct: the adult first-name check stays silent.
        AssertRuleDoesNotFire(findings, AdultFirstName);
    }

    [Fact]
    public void SA_G3_ChildNaming_Parent_Titled_Surname_In_A_Child_Letter_Passes()
    {
        var child = Inject(Taylor, TaylorRe, "Re: David Taylor, DOB: 10 November 2003").Replace("Mr Taylor", "David");
        var letter = Inject(child,
            "His brother has gout, and his father died of kidney failure.",
            "His brother has gout. His father, Mr Taylor, attends the clinic with him.");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), MinorNaming);
    }

    [Fact]
    public void SA_G3_ChildNaming_Relative_And_Past_Event_Ages_Never_Make_An_Adult_A_Minor()
    {
        // No DOB anywhere: only an age tied to the patient could mark a minor.
        var letter = Inject(Inject(Inject(Taylor, TaylorRe, "Re: Mr David Taylor"),
            "His brother has gout", "His brother, aged 12, has gout"),
            TaylorGoutSentence, "Mr Taylor has had gout since the age of 15");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), MinorNaming);
    }

    [Fact]
    public void SA_G3_ChildNaming_Candidate_Lane_Is_Unchanged()
    {
        var letter = Inject(Taylor, TaylorRe, "Re: Mr David Taylor, DOB: 10 November 2003");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), MinorNaming);
    }

    // ─── G3b — adult first-name use ────

    [Theory]
    [InlineData(TaylorGoutSentence, "David has had gout since 2000")] // pack: "Erika fasting sugars remain ..."
    [InlineData("His brother has gout", "David's brother has gout")]  // pack: "... management of Erika's blood glucose control"
    public void SA_G3_AdultFirstName_Bare_First_Name_For_An_Adult_Fires(string find, string replace)
        => AssertRuleFires(Lint(Inject(Taylor, find, replace), "LT-UR"), AdultFirstName);

    [Fact]
    public void SA_G3_AdultFirstName_Titled_Full_Name_Introduction_Passes()
        // Weston: "... management of Mrs Betty Weston, who has been diagnosed ..."
        => AssertRuleDoesNotFire(Lint(Weston, "LT-NM"), AdultFirstName);

    [Fact]
    public void SA_G3_AdultFirstName_Relative_Sharing_The_First_Name_Passes()
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, "His brother has gout", "His brother David has gout"), "LT-UR"), AdultFirstName);

    [Fact]
    public void SA_G3_AdultFirstName_Is_Silent_When_No_Age_Can_Be_Derived()
    {
        // "Miss Alison Cooper, Year 5 student": without a DOB or an age the
        // patient may be a child, so a first name is never flagged.
        var letter = Inject(Inject(Taylor, TaylorRe, "Re: Mr David Taylor"),
            TaylorGoutSentence, "David has had gout since 2000");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), AdultFirstName);
    }

    [Fact]
    public void SA_G3_AdultFirstName_Calendar_Word_First_Name_Is_Not_Read_As_A_Name()
    {
        var letter = Inject(Inject(Taylor, TaylorRe, "Re: Mr June Taylor, DOB: 1 August 1965"),
            TaylorGoutSentence, "Mr Taylor has had gout since June 2000");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), AdultFirstName);
    }

    [Fact]
    public void SA_G3_AdultFirstName_Candidate_Lane_Is_Unchanged()
    {
        var letter = Inject(Taylor, TaylorGoutSentence, "David has had gout since 2000");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), AdultFirstName);
    }

    // ─── G3c — title/sex fidelity: Re: line title vs the letter's pronouns ────

    [Fact]
    public void SA_G3_TitleSex_Male_Title_With_Only_Female_Pronouns_Fires()
    {
        // "Re: Mr Betty Weston" used consistently, while the body says "Her
        // sleep ...", "She reported ...": the title-switch loop cannot see it.
        var letter = Inject(Weston, "Mrs ", "Mr ");
        AssertRuleFires(Lint(letter, "LT-NM"), TitleMismatch);
    }

    [Fact]
    public void SA_G3_TitleSex_Female_Title_With_Only_Male_Pronouns_Fires()
    {
        var letter = Inject(Taylor, "Mr ", "Ms ");
        AssertRuleFires(Lint(letter, "LT-UR"), TitleMismatch);
    }

    [Fact]
    public void SA_G3_TitleSex_A_Single_Pronoun_Of_The_Titles_Sex_Keeps_The_Check_Silent()
    {
        var letter = Inject(Inject(Weston, "Mrs ", "Mr "),
            "Should there be any queries, kindly do not hesitate to contact me.",
            "His wife can be contacted at home. Should there be any queries, kindly do not hesitate to contact me.");
        AssertRuleDoesNotFire(Lint(letter, "LT-NM"), TitleMismatch);
    }

    [Fact]
    public void SA_G3_TitleSex_Husband_Pronoun_In_A_Female_Patient_Letter_Passes()
    {
        // OET test 10 shape: "Today, Ms Martin attended with her husband. He reported ..."
        var letter = Inject(Weston,
            "Should there be any queries, kindly do not hesitate to contact me.",
            "Mrs Weston attended with her husband. He reported that she has been unable to work. Should there be any queries, kindly do not hesitate to contact me.");
        AssertRuleDoesNotFire(Lint(letter, "LT-NM"), TitleMismatch);
    }

    [Fact]
    public void SA_G3_TitleSex_Doctor_Title_Is_Not_Sexed()
        => AssertRuleDoesNotFire(Lint(Inject(Weston, "Mrs ", "Dr "), "LT-NM"), TitleMismatch);

    [Fact]
    public void SA_G3_TitleSex_Candidate_Lane_Is_Unchanged()
        => AssertRuleDoesNotFire(Lint(Inject(Weston, "Mrs ", "Mr "), "LT-NM", isModelAnswer: false), TitleMismatch);

    // ─── G3d — age_dob_inconsistent ────

    [Theory]
    [InlineData("Mr Taylor, aged 44, has had gout since 2000")]             // Barry Jones: "Mr Jones, aged 44," (DOB-derived 42)
    [InlineData("Mr Taylor, a 23-year-old lawyer, has had gout since 2000")] // James Greenbaum: "a 23-year-old lawyer" (DOB-derived 25)
    [InlineData("Mr David Taylor, aged 55, has had gout since 2000")]       // the notes' own inconsistent "(age 55)" copied
    public void SA_G3_AgeDob_Age_Contradicting_The_Re_Line_Dob_Fires(string sentence)
        => AssertRuleFires(Lint(Inject(Taylor, TaylorGoutSentence, sentence), "LT-UR"), AgeDob);

    [Fact]
    public void SA_G3_AgeDob_Notes_Dob_Wins_Over_The_Notes_Stated_Age()
    {
        const string notes = "Mr David Taylor, DOB 01/08/1965 (age 55). History of gout since 2000.";
        var letter = Inject(Inject(Taylor, TaylorRe, "Re: Mr David Taylor"),
            TaylorGoutSentence, "Mr Taylor, aged 55, has had gout since 2000");
        AssertRuleFires(Lint(letter, "LT-UR", caseNotes: notes), AgeDob);
    }

    [Theory]
    [InlineData("Mr Taylor, aged 54, has had gout since 2000")]
    [InlineData("Mr Taylor, a fifty-four-year-old teacher, has had gout since 2000")]
    [InlineData("Mr Taylor's brother, aged 44, has had gout since 2000")]
    [InlineData("His father, Mr Robert Taylor, aged 80, has had gout since 2000")]
    [InlineData("Mr Taylor has had gout since the age of 35")]
    [InlineData("Mr Taylor was fifteen years old when he started smoking, and he has had gout since 2000")]
    public void SA_G3_AgeDob_Consistent_Relative_And_Past_Event_Ages_Pass(string sentence)
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorGoutSentence, sentence), "LT-UR"), AgeDob);

    [Fact]
    public void SA_G3_AgeDob_Is_Silent_Without_Any_Dob_Or_Recorded_Age()
    {
        var letter = Inject(Inject(Taylor, TaylorRe, "Re: Mr David Taylor"),
            TaylorGoutSentence, "Mr Taylor, aged 44, has had gout since 2000");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR"), AgeDob);
    }

    [Fact]
    public void SA_G3_AgeDob_Candidate_Lane_Is_Silent()
    {
        var letter = Inject(Taylor, TaylorGoutSentence, "Mr Taylor, aged 44, has had gout since 2000");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), AgeDob);
    }

    // ─── G3e — DOB priority: "was born on" in the notes is a DOB ────

    [Theory]
    // Mary Clarke: "Mrs Mary Clarke was born on 17 September 1960 and is a patient in your General Practice." + "Re: Mrs Mary Clarke"
    [InlineData("Re: Mr David Taylor", TaylorBornOnNotes)]
    // Patrick Newton notes form: "Patient was born on 6 July 1989."
    [InlineData("Re: Mr David Taylor, aged 54", "Patient was born on 1 August 1965. History of gout since 2000.")]
    public void SA_G3_DobPriority_Born_On_Dob_Omitted_From_The_Re_Line_Fires(string reLine, string notes)
        => AssertRuleFires(Lint(Inject(Taylor, TaylorRe, reLine), "LT-UR", caseNotes: notes), DobPriority);

    [Theory]
    [InlineData("Mr David Taylor has gout. His daughter was born on 3 May 1990.")]
    [InlineData("Mr David Taylor has gout. In July 2018 his daughter was born after a long labour.")]
    [InlineData("Mr Robert Taylor was born on 2 March 1940. Mr David Taylor has gout.")]
    [InlineData("Mr David Taylor has gout. He lives with 2 children at home, born 2002 and 2004.")]
    public void SA_G3_DobPriority_Relative_Or_Bare_Year_Births_Are_Not_The_Patient_Dob(string notes)
        => AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorRe, "Re: Mr David Taylor"), "LT-UR", caseNotes: notes), DobPriority);

    [Fact]
    public void SA_G3_DobPriority_Re_Line_Carrying_The_Born_On_Dob_Passes()
        => AssertRuleDoesNotFire(Lint(Taylor, "LT-UR", caseNotes: TaylorBornOnNotes), DobPriority);

    [Fact]
    public void SA_G3_DobPriority_Candidate_Lane_Born_On_Branch_Is_Silent()
    {
        var letter = Inject(Taylor, TaylorRe, "Re: Mr David Taylor");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", caseNotes: TaylorBornOnNotes, isModelAnswer: false), DobPriority);
    }

    // ─── G3g — sign-off: closing phrase alone, then ONE designation line ────

    [Theory]
    [InlineData("Yours sincerely, Doctor\n\nDoctor")] // Somarni Khaze / Louise Geller
    [InlineData("Yours sincerely, Doctor")]
    [InlineData("Yours sincerely,\n\nDoctor\n\nDoctor")]
    [InlineData("Yours sincerely,\n\nDoctor\nCity Hospital")]
    public void SA_G3_Signoff_Designation_Duplicated_Or_Not_Alone_Fires(string signoff)
        => AssertRuleFires(Lint(Inject(Taylor, TaylorSignoff, signoff), "LT-UR"), Signoff);

    [Fact]
    public void SA_G3_Signoff_Canonical_And_Valid_Alternative_Blocks_Pass()
    {
        AssertRuleDoesNotFire(Lint(Taylor, "LT-UR"), Signoff);
        // Two blank lines before the designation are allowed.
        AssertRuleDoesNotFire(Lint(Inject(McDonald, "Yours faithfully,\n\nDoctor", "Yours faithfully,\n\n\nDoctor"), "LT-TR"), Signoff);
        // A two-word designation on its own line.
        AssertRuleDoesNotFire(Lint(Inject(Taylor, TaylorSignoff, "Yours sincerely,\n\nRegistered Nurse"), "LT-UR"), Signoff);
        // A single-line text has no closing-phrase line to inspect.
        AssertRuleDoesNotFire(Lint("Dear Dr Smith, I am writing to refer Mr Jones. Yours sincerely, Doctor", "LT-RR"), Signoff);
    }

    [Fact]
    public void SA_G3_Signoff_Candidate_Lane_Is_Unchanged()
    {
        var letter = Inject(Taylor, TaylorSignoff, "Yours sincerely, Doctor\n\nDoctor");
        AssertRuleDoesNotFire(Lint(letter, "LT-UR", isModelAnswer: false), Signoff);
    }

    // ─── Clean fixtures: no G3 finding of any kind ────

    [Fact]
    public void SA_G3_Clean_Fixture_Letters_Produce_No_G3_Findings()
    {
        var fixtures = new (string Name, string Letter, string LetterType, ExamProfession Profession, string? Notes)[]
        {
            ("Garcia", Garcia, "LT-DG", ExamProfession.Medicine, null),
            ("Garcia with notes", Garcia, "LT-DG", ExamProfession.Medicine, "Date of birth 01.01.1995 (age 20). Presented on 23 May 2015 with painful, stiff joints."),
            ("Weston", Weston, "LT-NM", ExamProfession.Medicine, null),
            ("Weir", Weir, "LT-RR", ExamProfession.Medicine, null),
            ("McDonald", McDonald, "LT-TR", ExamProfession.Medicine, null),
            ("Taylor", Taylor, "LT-UR", ExamProfession.Medicine, null),
            ("Taylor with born-on notes", Taylor, "LT-UR", ExamProfession.Medicine, TaylorBornOnNotes),
            ("UltimateFinal Weir", WritingUltimateFinalRegressionFixtureTests.WeirRoutineReferralLetter, "LT-RR", ExamProfession.Medicine, null),
            ("UltimateFinal Garcia", WritingUltimateFinalRegressionFixtureTests.GarciaDischargeLetter, "LT-DG", ExamProfession.Medicine, null),
            ("Ramsey", WritingUltimateFinalRegressionFixtureTests.RamseyPharmacyLetter, "LT-OT", ExamProfession.Pharmacy, null),
            ("Wright", WritingUltimateFinalRegressionFixtureTests.WrightKneeReferralLetter, "LT-RR", ExamProfession.Physiotherapy, null),
        };
        foreach (var fixture in fixtures)
        {
            var g3 = Lint(fixture.Letter, fixture.LetterType, caseNotes: fixture.Notes, profession: fixture.Profession)
                .Where(f => G3CheckIds.Any(id => f.RuleId.EndsWith(id, StringComparison.Ordinal)))
                .ToList();
            Assert.True(g3.Count == 0,
                fixture.Name + " produced G3 findings: " + string.Join(" | ", g3.Select(f => f.RuleId + ": " + f.Message)));
        }
    }
}
