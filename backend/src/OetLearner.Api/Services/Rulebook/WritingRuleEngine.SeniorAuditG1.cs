using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Senior Assessor Release Audit (owner, 16 Sep 2026) — group G1 grammar
/// battery. Every detector here is MODEL ANSWER ONLY (DECISIONS §B): the
/// candidate lane is AI-graded and must never see these deterministic shapes.
/// <list type="bullet">
/// <item><c>sentence_fragment</c> (G1a): verbless / subjectless sentences —
/// "With poorly controlled diabetes, ...", "However, no effusion.",
/// "Lives with her long-term boyfriend, ...", "And prescribed ...",
/// "At your earliest convenience.".</item>
/// <item><c>incomplete_clinical_construction</c> extension (G1b): a gapped
/// passive auxiliary — "..., and family immunisation discussed.",
/// "... and atorvastatin, 20 mg daily added.".</item>
/// <item><c>incomplete_clinical_construction</c> extension (G1c): an
/// adjective coordinated into a "with" noun list — "with a petechial rash
/// ..., bruising on the left arm and unable to touch ...".</item>
/// <item><c>malformed_word_form</c> (G1d): mechanical substitutions —
/// "an ex-smokes", "has been continued smoking", "social drinks alcohol"
/// (doubled words belong to typographic_corruption, G2).</item>
/// </list>
/// Precision over recall: every shape is lexically bounded, and the finite-verb
/// evidence used by the verbless-sentence check is deliberately generous, so a
/// missed fragment is preferred to a false finding on correct clinical English.
/// </summary>
public sealed partial class WritingRuleEngine
{
    // ---------------------------------------------------------------------
    // Shared: sentence segmentation with offsets.
    // ---------------------------------------------------------------------

    // A piece that ends with a dotted abbreviation or a single capital initial
    // ("e.g.", "Dr.", "vitamin D.") is re-joined to the next piece, so an
    // abbreviation never manufactures a "fragment".
    private static readonly Regex SaG1AbbreviationEndRe = new(
        @"(?:\b(?:e\.g|i\.e|etc|approx|vs|Dr|Mr|Mrs|Ms|St|No|Prof)|(?<![A-Za-z])[A-Z])\.$");

    private static List<(string Text, int Index)> SaG1Sentences(string paragraph)
    {
        var result = new List<(string Text, int Index)>();
        var from = 0;
        foreach (var piece in SplitSentences(paragraph))
        {
            var index = paragraph.IndexOf(piece, from, StringComparison.Ordinal);
            if (index < 0) continue;
            from = index + piece.Length;
            if (result.Count > 0 && SaG1AbbreviationEndRe.IsMatch(result[^1].Text))
            {
                var previous = result[^1];
                result[^1] = (paragraph[previous.Index..from], previous.Index);
                continue;
            }
            result.Add((piece, index));
        }
        return result;
    }

    private static string SaG1Clip(string text, int max)
        => text.Length > max ? text[..max] + "…" : text;

    // ---------------------------------------------------------------------
    // G1a — sentence_fragment
    // ---------------------------------------------------------------------

    private static readonly Regex SaG1WordRe = new(@"[A-Za-z]{2,}");

    private static readonly Regex SaG1TokenRe = new(@"[A-Za-z]+");

    // F1 — a linker followed directly by a finite verb: the clause has no
    // subject ("However, was initially resistant ..."). An inverted
    // conditional ("However, should there be ...") is excluded.
    private static readonly Regex SaG1LinkerNoSubjectRe = new(
        @"^(?:However|Therefore|Thus|Consequently|Subsequently|Additionally|In addition|Nevertheless|Nonetheless|Initially|Later|Meanwhile|Furthermore|Moreover|Currently|Previously|Recently|Also),\s+(?:(?:also|still|now|then|initially|subsequently|later|only|never|since|recently|previously|currently)\s+)?(?:am|is|are|was|were|has|have|had|will|would|can|could|should|may|might|must|did|does)\b(?!\s+(?:there|you|he|she|it|they|we|I|his|her|their|its|the|a|an|any|this|these|those|Mr|Mrs|Ms|Miss|Dr)\b)");

    // F2 — a coordinator start with no subject ("And prescribed ...",
    // "And was ..."). The "-ed" branch is confirmed by the absence of any
    // finite evidence in the rest of the sentence.
    private static readonly Regex SaG1CoordinatorStartRe = new(
        @"^(?:And|But|Or)\s+(?:(?:also|then|later|subsequently|initially)\s+)?(?:(?<ed>[a-z]{3,}ed)\b|(?:am|is|are|was|were|has|have|had|will|would|can|could|should|may|might|must)\b(?!\s+(?:there|you|he|she|it|they|we|I|his|her|their|its|the|a|an|any|this|these|those|Mr|Mrs|Ms|Miss|Dr)\b))");

    // F3 — the sentence opens with a finite verb and has no subject. A
    // third-person-singular opener ("Lives with ...") always lacks a subject;
    // a past-tense opener ("Disclosed a ...") fires only when followed by an
    // object/preposition and not by a main clause ("Presented with these
    // results, she agreed ..." is a participle opener).
    private static readonly Regex SaG1BareVerbStartRe = new(
        @"^(?:(?<s>Lives|Works|Comprises|Includes|Reports|Presents|Denies|Takes|Smokes|Drinks|Remains|Requires|Continues|Complains|Attends)|(?<p>Disclosed|Presented|Reported|Denied|Developed|Complained|Attended|Underwent|Noticed|Experienced|Stated|Described))\s+(?<next>[A-Za-z][A-Za-z'’\-]*)");

    private static readonly HashSet<string> SaG1BareVerbStopNext = new(StringComparer.OrdinalIgnoreCase)
    {
        "by", "as", "of", "from", "and", "or", "is", "are", "was", "were", "has", "have", "had",
    };

    private static readonly HashSet<string> SaG1ParticipleOpenerObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "his", "her", "their", "its", "with", "to", "at", "in", "on", "for", "from", "that",
        "no", "several", "some", "any", "multiple", "one", "two", "three", "four", "five", "six", "seven",
        "eight", "nine", "ten", "increasing", "worsening", "recurrent", "ongoing", "persistent", "severe",
        "intermittent", "symptoms", "pain", "shortness", "difficulty", "new", "left", "right", "bilateral",
        "chest", "abdominal", "back",
    };

    private static readonly Regex SaG1OpenerMainClauseRe = new(
        @",\s+(?:(?i:he|she|they|it|we|the|his|her|their)|I|Mr|Mrs|Ms|Miss|Dr)\b");

    // F1b — ", and <aux>" after a pre-clause with no finite verb ("With
    // insomnia, fatigue, joint pain and headaches, and was advised ...").
    private static readonly Regex SaG1AndAuxRe = new(
        @"^(?<pre>(?:[^.;]|\.(?=\d))+?),\s*and\s+(?:was|were|is|are|has|have|had)\b");

    // ---- finite-verb evidence (generous by design: evidence prevents a finding) ----

    private static readonly Regex SaG1AuxRe = new(
        @"\b(?:am|is|are|was|were|has|have|had|do|does|did|will|would|shall|should|can|could|may|might|must|cannot)\b|n['’]t\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1IrregularPastRe = new(
        @"\b(?:began|became|brought|bought|came|caught|chose|drank|drove|ate|fell|felt|fought|found|forgot|gave|got|grew|heard|held|hurt|kept|knew|led|lost|made|meant|met|paid|quit|ran|rose|said|sat|saw|sent|slept|sought|spent|spoke|stood|struck|swam|took|taught|told|thought|threw|understood|underwent|went|woke|won|wore|wrote|bled|broke|withdrew|arose|undertook|overcame|forgave|froze|hid|shook|stuck|tore|wept|swore|sank|rang|sold|dealt|fled|drew|flew|rode|sang|stole|swept|swung|laid|built|burnt|learnt|knelt|leapt|bent|lent|forbade|awoke)\b",
        RegexOptions.IgnoreCase);

    // Irregular past forms that double as nouns count only in a verb frame
    // ("the rash spread to her trunk").
    private static readonly Regex SaG1HomographPastRe = new(
        @"\b[a-z]{3,}\s+(?:spread|cut|put|set|split|shut|hit|let|burst|shed|fed|slid)\s+(?:to|into|over|across|through|from|up|down|off|out|on|in|a|an|the|his|her|their|him|them|its)\b");

    private static readonly Regex SaG1PronounSubjectRe = new(
        @"\b(?:I|we|they|you|he|she|it|who|which)\s+(?:[a-z]+ly\s+|(?:also|still|now|then|only|never|always|often|first|again|already|further|initially|subsequently|later)\s+)?(?!(?:and|or|with|to|at|in|on|of|for|by|from|as)\b)[a-z]{2,}\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1DemonstrativeSubjectRe = new(
        @"^(?:This|That)\s+(?:[a-z]+ly\s+)?[a-z]{2,}(?:ed|s(?<!ss)(?<!us)(?<!is)(?<!as))\b|\bthis\s+(?:[a-z]+ly\s+)?[a-z]{2,}s(?<!ss)(?<!us)(?<!is)(?<!as)\b|^Both\s+[a-z]{2,}\b");

    // "Mr Weir reports ..." — a possessive ("Mr Taylor's gout") cannot match,
    // and an "-ing" word after the name is a participle, not a verb
    // ("Ms Geller existing ...").
    private static readonly Regex SaG1TitledSubjectRe = new(
        @"\b(?:Mr|Mrs|Ms|Miss|Dr)\s+[A-Z][A-Za-z\-]*(?:['’][A-Z][A-Za-z\-]*)?(?:\s+[A-Z][A-Za-z\-]*(?:['’][A-Z][A-Za-z\-]*)?)?\s+(?:[a-z]+ly\s+|(?:also|still|now|then|only|never|first|again|initially|subsequently|later|currently|previously|recently)\s+)?(?![a-z]+ing\b)(?!(?:and|or|with|to|at|in|on|of|for|by|from|as|within|during|after|before|who|which|that|since|until|about|over|under|into|despite|following|including|regarding|via|per)\b)[a-z]{2,}\b");

    // "Crestor blocks ...", "Analgesia comprises ...".
    private static readonly Regex SaG1InitialNounVerbRe = new(
        @"^(?!(?:However|Therefore|Thus|With|Without|At|In|On|For|From|After|Before|Despite|Following|Given|Owing|And|But|Or|The|A|An)\b)[A-Z][a-z\-]+\s+(?:[a-z]+ly\s+)?[a-z]{2,}s(?<!ss)(?<!us)(?<!is)(?<!as)\b");

    // "His family shares most meals.", "Mrs Whitfield's risk factors ...".
    private static readonly Regex SaG1DeterminerSubjectVerbRe = new(
        @"^(?:(?:His|Her|Their|Its|The|This|That|My|Our|Your|A|An)\s+|(?:(?:Mr|Mrs|Ms|Miss|Dr)\s+)?[A-Z][A-Za-z\-]*['’]s\s+)(?:[A-Za-z][A-Za-z\-]*\s+){0,3}?[a-z]{2,}s(?<!ss)(?<!us)(?<!is)(?<!as)\s+(?!(?:of|and|or|in|on|at|with|without|for|to|from|by|as|after|before|during|since|until|over|under|above|below|between|within|including|despite|via|per|than|that|which|who|while|when|where|whereas|because|although|but|nor|so|if)\b)[a-z]");

    private const string SaG1BaseVerbs =
        "live|work|respond|smoke|drink|take|report|remain|require|include|show|reveal|suggest|indicate|confirm|demonstrate|continue|cease|stop|start|begin|end|attend|occur|persist|need|want|use|feel|agree|decline|prefer|accept|visit|help|provide|radiate|worsen|improve|recur|resolve|settle|appear|seem|become|know|understand|deny|complain|describe|experience|find|enjoy|sleep|eat|cause|keep|receive|plan|return|state|worry|await|involve|affect|limit|reduce|increase|contain|allow|lead|see|suffer|follow|fluctuate|deteriorate|develop|produce|vary|exceed|subside|precede|trigger|prevent|reflect|swell|ache|bleed|hurt|place|share|last|spread|tolerate|consist|comprise|arise|shake|tremble|itch|wake|snore|cough|wheeze|sweat|faint|cope|manage|walk|average|range|wax|wane";

    private const string SaG1UnambiguousBaseVerbs =
        "include|require|suggest|indicate|demonstrate|reveal|contain|involve|comprise|persist|occur|appear|seem|become|remain|affect|prevent|deny|understand|agree|prefer|tolerate|radiate|worsen|improve|recur|respond|consist|fluctuate|deteriorate|subside|arise|exacerbate|alleviate|relieve|necessitate|warrant|confirm|allow|receive|describe|accept|continue|cease|resolve|develop|exceed|precede|manage|cope|attend|suffer|enjoy|tremble|wheeze|snore";

    // Plural subject + base verb ("Recent investigations show", "Her fasting
    // sugars remain") and coordinated subject + base verb ("Pyrazinamide and
    // ethambutol then cease").
    private static readonly Regex SaG1PluralSubjectBaseVerbRe = new(
        @"\b(?:[A-Za-z]{3,}s(?<!ss)(?<!us)(?<!is)(?<!as)|children|people|men|women)\s+(?:(?:[a-z]+ly|also|still|now|then|only|never|often|always|both|all)\s+)?(?:" + SaG1BaseVerbs + @")\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1CoordSubjectBaseVerbRe = new(
        @"\band\s+(?!(?:a|an|the|his|her|their|its|no|some|any|mild|severe|occasional|intermittent|recent|new|dry|productive|chronic|acute|low|high|poor|reduced|daily|regular)\b)[A-Za-z][\w\-]*\s+(?:(?:[a-z]+ly|then|also|both|now)\s+)?(?:" + SaG1BaseVerbs + @")\b",
        RegexOptions.IgnoreCase);

    // A base form that is only ever a verb, after any non-infinitive,
    // non-modal word ("risk factors for osteoporosis include ...").
    private static readonly Regex SaG1UnambiguousBaseVerbRe = new(
        @"\b(?![a-z]+ly\b)(?!(?:to|not|and|or|will|would|can|could|may|might|must|should|shall|do|does|did|never|also|please|kindly|further|then|still|now|only|often|always|both|all)\b)[A-Za-z][\w\-]*,?\s+(?:(?:[a-z]+ly|still|now|then|only|often|always|both|all)\s+)?(?:" + SaG1UnambiguousBaseVerbs + @")\b",
        RegexOptions.IgnoreCase);

    // Noun + base verb + object ("... and high cholesterol increase the risk").
    private static readonly Regex SaG1BaseVerbObjectRe = new(
        @"\b(?!(?:an|a|the|to|and|or|of|no|not|any|some|his|her|their|its|this|that|further|slight|small|large|significant|marked|sudden|gradual|rapid|mild|recent|with|for|in|on|at|by|from|will|would|can|could|may|might|must|should|shall|do|does|did|please|kindly|also|then|never)\b)[a-z]{3,}\s+(?:" + SaG1BaseVerbs + @")\s+(?:the|a|an|his|her|their|its|this|these|those|your|my|our|him|them)\b");

    // A word followed by an object pronoun is a verb unless it is a
    // preposition/conjunction/adverb ("... fracture place her at risk").
    private static readonly Regex SaG1ObjectPronounRe = new(
        @"\b(?![a-z]+ing\b)(?![a-z]+ly\b)(?!(?:with|without|to|for|of|from|by|at|in|on|about|after|before|behind|beside|besides|between|towards|toward|near|like|unlike|than|as|and|or|but|nor|around|along|across|into|onto|over|under|through|throughout|during|despite|beyond|within|inside|outside|upon|against|among|per|via|including|regarding|concerning|following|past|since|until|below|above|beneath|underneath|except|all|both|each|either|neither|if|that|when|while|whether|because|so|once|whom|whose|then|also|only|even|just)\b)[a-z]{2,}\s+(?:him|her|them|me|us)\b");

    private static readonly Regex SaG1ImperativeMarkerRe = new(
        @"\b(?:please|kindly)\s+[a-z]{2,}|\bthank\s+you\b",
        RegexOptions.IgnoreCase);

    // Imperatives are complete sentences (patient-instruction letters),
    // optionally after an introductory clause ("If conscious, give ...").
    private const string SaG1ImperativePrefix =
        @"^(?:(?:However|Also|Then|Additionally|Therefore|Instead|Meanwhile|Finally|First|Next|Otherwise|Ideally|Importantly)\s*,\s*|(?:If|When|Before|After|During|In|On|For|Once|Until|While|Unless|Between|From|At|Within|To avoid|To reduce|To prevent|To minimise|To minimize)\b[^,]{0,80},\s*)?(?:(?:also|then|always|never|only|immediately|regularly|please|do not|not)\s+)?";

    private static readonly Regex SaG1ImperativeStartRe = new(
        SaG1ImperativePrefix + @"(?:take|avoid|continue|keep|apply|seek|attend|monitor|store|swallow|ensure|arrange|advise|consider|refer|assess|inform|remember|make|try|eat|reduce|wear|bring|ask|tell|follow|encourage|provide|discuss|carry|give|rotate|discard|telephone|consult|educate|liaise|inject|vacuum|dispose|replace|remove|elevate|obtain|collect|dilute|shake|mix|chew|dissolve|insert|inhale|rinse|protect|allow|skip|notify|escalate|assist|supervise|observe|reassure|explain|teach|demonstrate|confirm|verify|prescribe|dispense|administer|titrate|taper|wean|withhold|cease|resume|restart|adjust|go|let)\b(?![\-'’])",
        RegexOptions.IgnoreCase);

    // Verbs that double as nouns need an object-like follower, so
    // "Exercise tolerance review for referral ..." stays a fragment.
    private static readonly Regex SaG1ImperativeHomographStartRe = new(
        SaG1ImperativePrefix + @"(?:exercise|rest|walk|review|report|record|test|change|place|support|help|visit|plan|note|watch|drink|ice|cover|return|treat|pack|stop|start|use|check|contact|increase|limit|update|call|book|offer|wait|wash|shower|stretch|see|find|read|practise|practice|complete)\s+(?:(?:not|only|always|never|regularly)\s+)?(?:a|an|the|this|these|those|your|his|her|their|it|them|him|me|us|one|two|all|any|some|every|each|up|out|off|on|to|with|for|enough|alcohol|fluids|water|food|medications?|tablets?|capsules?|doses?|insulin|levels|blood|glucose|hands|feet|skin|dressings?|weight|temperature|daily|regularly|immediately|[a-z]{3,}ing)\b",
        RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SaG1ThirdPersonVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "presents", "reports", "denies", "takes", "smokes", "drinks", "lives", "works", "remains", "requires",
        "includes", "comprises", "contains", "consists", "shows", "reveals", "demonstrates", "confirms",
        "suggests", "indicates", "continues", "attends", "complains", "describes", "experiences", "feels",
        "finds", "uses", "wears", "enjoys", "needs", "wants", "walks", "sleeps", "eats", "weighs", "measures",
        "occurs", "persists", "radiates", "responds", "appears", "seems", "becomes", "gets", "goes", "comes",
        "knows", "understands", "agrees", "declines", "refuses", "prefers", "accepts", "attributes", "checks",
        "manages", "mobilises", "mobilizes", "receives", "visits", "relies", "cares", "lacks", "notes",
        "notices", "plans", "returns", "states", "wishes", "worries", "awaits", "involves", "affects", "limits",
        "reduces", "increases", "improves", "worsens", "provides", "supports", "allows", "helps", "leads",
        "results", "causes", "keeps", "starts", "stops", "plays", "runs", "travels", "drives", "resides",
        "believes", "recalls", "admits", "avoids", "follows", "exercises", "practises", "speaks", "stays",
        "spends", "tolerates", "undergoes", "wakes", "lifts", "carries", "struggles", "relieves", "eases",
        "settles", "resolves", "recurs", "extends", "spreads", "slows", "lowers", "raises", "blocks",
        "prevents", "protects", "lasts", "treats", "controls", "produces", "fluctuates", "deteriorates",
        "develops", "detects", "owns", "teaches", "coughs", "vomits", "bleeds", "hurts", "aches", "itches",
        "swells", "progresses", "averages", "varies", "exceeds", "shares", "looks", "mentions", "explains",
        "expresses", "recommends", "excludes", "precludes", "reaches", "sees", "hears", "says", "tells",
        "asks", "thinks", "likes", "loves", "hates", "dies", "falls", "rises", "drops", "sits", "stands",
        "moves", "turns", "brings", "buys", "pays", "sells", "writes", "reads", "studies", "cooks", "cleans",
        "shops", "swims", "cycles", "trains", "volunteers", "feeds", "flies", "rides", "misses", "forgets",
        "remembers", "loses", "gains", "equals", "totals", "represents", "reflects", "warrants",
        "necessitates", "triggers", "exacerbates", "aggravates", "alleviates", "subsides", "disappears",
        "begins", "ends", "happens", "arises", "migrates", "restricts", "interferes", "disturbs", "makes",
        "gives", "rents", "assists", "supervises", "handles", "prepares", "organises", "organizes",
        "completes", "performs", "administers", "injects", "monitors", "records", "tests", "applies",
        "changes", "adjusts", "omits", "suffers", "tends", "hopes", "intends", "expects", "chooses",
        "decides", "consumes", "snores", "urinates", "sweats", "faints", "collapses", "trembles", "shakes",
        "limps", "wheezes", "breathes", "requests", "seeks", "sustains", "obtains", "maintains", "retains",
        "contributes", "correlates", "coincides",
    };

    // Regular "-ed" forms are finite unless they are the first word, a fixed
    // adjective, follow a determiner/preposition/degree word, open an
    // absolute "with ..." phrase, or are a reduced passive (", eased by").
    private static readonly Regex SaG1RegularPastRe = new(@"\b[A-Za-z][A-Za-z\-]+ed\b");

    private static readonly Regex SaG1PrecedingWordRe = new(@"([A-Za-z]+)\s+$");

    private static readonly Regex SaG1WithWithoutStartRe = new(@"^\s*(?:with|without)\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG1ByStartRe = new(@"^\s+by\b");

    private static readonly Regex SaG1SubjectStartRe = new(
        @"^(?:The|A|An|His|Her|Their|Its|This|These|Those|He|She|They|It|I|We|Mr|Mrs|Ms|Miss|Dr)\b");

    private static readonly HashSet<string> SaG1AdjectivalEdWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "aged", "associated", "related", "unrelated", "suspected", "unexplained", "uncontrolled", "undisclosed", "hundred",
    };

    private static readonly HashSet<string> SaG1PastEedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "agreed", "disagreed", "freed", "guaranteed", "refereed",
    };

    private static readonly HashSet<string> SaG1NonFiniteEdPrecursors = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "these", "those", "his", "her", "their", "its", "my", "our", "your", "no",
        "not", "non", "poorly", "well", "newly", "very", "as", "with", "without", "of", "in", "on", "at", "for",
        "from", "to", "by", "about", "over", "under", "into", "after", "before", "during", "despite", "via",
        "than", "between", "within", "through", "throughout", "against", "alongside", "including", "per",
    };

    private static bool SaG1HasFiniteEvidence(string x)
    {
        if (SaG1AuxRe.IsMatch(x) || SaG1IrregularPastRe.IsMatch(x) || SaG1HomographPastRe.IsMatch(x)
            || SaG1PronounSubjectRe.IsMatch(x) || SaG1DemonstrativeSubjectRe.IsMatch(x) || SaG1TitledSubjectRe.IsMatch(x)
            || SaG1InitialNounVerbRe.IsMatch(x) || SaG1DeterminerSubjectVerbRe.IsMatch(x)
            || SaG1PluralSubjectBaseVerbRe.IsMatch(x) || SaG1CoordSubjectBaseVerbRe.IsMatch(x)
            || SaG1UnambiguousBaseVerbRe.IsMatch(x) || SaG1BaseVerbObjectRe.IsMatch(x) || SaG1ObjectPronounRe.IsMatch(x)
            || SaG1ImperativeMarkerRe.IsMatch(x) || SaG1ImperativeStartRe.IsMatch(x) || SaG1ImperativeHomographStartRe.IsMatch(x))
            return true;

        foreach (Match token in SaG1TokenRe.Matches(x))
            if (SaG1ThirdPersonVerbs.Contains(token.Value)) return true;

        var firstWord = SaG1TokenRe.Match(x);
        foreach (Match m in SaG1RegularPastRe.Matches(x))
        {
            if (firstWord.Success && m.Index == firstWord.Index) continue;
            var word = m.Value;
            if (SaG1AdjectivalEdWords.Contains(word)) continue;
            if (word.EndsWith("eed", StringComparison.OrdinalIgnoreCase) && !SaG1PastEedWords.Contains(word)) continue;
            var before = x[..m.Index];
            var previous = SaG1PrecedingWordRe.Match(before);
            if (previous.Success && SaG1NonFiniteEdPrecursors.Contains(previous.Groups[1].Value)) continue;
            var comma = before.LastIndexOf(',');
            if (comma >= 0)
            {
                var segment = before[(comma + 1)..];
                if (SaG1WithWithoutStartRe.IsMatch(segment)) continue;
                if (segment.Trim().Length == 0
                    && SaG1ByStartRe.IsMatch(x[(m.Index + m.Length)..])
                    && !SaG1SubjectStartRe.IsMatch(x))
                    continue;
            }
            return true;
        }
        return false;
    }

    // Returns null for a complete sentence, otherwise the reason it is a
    // fragment.
    private static string? SaG1FragmentReason(string sentence)
    {
        if (SaG1WordRe.Matches(sentence).Count < 3) return null;
        // "Medications: ..." style lists are a different (layout) problem.
        if (Regex.IsMatch(sentence, @":\s")) return null;

        if (SaG1LinkerNoSubjectRe.IsMatch(sentence))
            return "the linker is followed directly by a verb, so the clause has no subject";

        var coordinator = SaG1CoordinatorStartRe.Match(sentence);
        if (coordinator.Success
            && (!coordinator.Groups["ed"].Success || !SaG1HasFiniteEvidence(sentence[(coordinator.Index + coordinator.Length)..])))
            return "it opens with a coordinator and has no subject";

        var bare = SaG1BareVerbStartRe.Match(sentence);
        if (bare.Success)
        {
            var next = bare.Groups["next"].Value;
            if (bare.Groups["s"].Success)
            {
                if (!SaG1BareVerbStopNext.Contains(next) && !next.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
                    return "it opens with a verb and has no subject";
            }
            else if (SaG1ParticipleOpenerObjects.Contains(next) && !SaG1OpenerMainClauseRe.IsMatch(sentence))
            {
                return "it opens with a verb and has no subject";
            }
        }

        var andAux = SaG1AndAuxRe.Match(sentence);
        if (andAux.Success)
        {
            var pre = andAux.Groups["pre"].Value;
            if (SaG1WordRe.Matches(pre).Count >= 2 && !SaG1HasFiniteEvidence(pre))
                return "the clause after \", and\" has no subject";
        }

        return SaG1HasFiniteEvidence(sentence) ? null : "it has no subject and finite verb";
    }

    // G1a — "sentence_fragment" (Model Answer only). Audit §2/§3.1: "With
    // poorly controlled diabetes, ...", "However, no effusion.", "However, a
    // HbA1c level at 10% ...", "Lives with her long-term boyfriend ...",
    // "However, was initially resistant ...", "Loss of appetite, ...",
    // "And prescribed eflornithine cream ...", "At your earliest convenience.",
    // "Oxycodone, 5-10 mg as required, ...", "Exercise tolerance review ...".
    private static IEnumerable<LintFinding> DetectSaG1SentenceFragment(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.BodyParagraphs.Count == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        var paragraphSearchFrom = 0;
        foreach (var paragraph in s.BodyParagraphs)
        {
            var paragraphIndex = s.Body.IndexOf(paragraph, paragraphSearchFrom, StringComparison.Ordinal);
            if (paragraphIndex >= 0) paragraphSearchFrom = paragraphIndex + paragraph.Length;
            foreach (var (sentence, sentenceIndex) in SaG1Sentences(paragraph))
            {
                var reason = SaG1FragmentReason(sentence);
                if (reason is null) continue;
                int? start = paragraphIndex >= 0 ? bodyOffset + paragraphIndex + sentenceIndex : null;
                int? end = start + sentence.Length;
                var fix = reason == "it has no subject and finite verb"
                    ? "Attach the phrase to the neighbouring sentence (\"... on the medial aspect, with no effusion.\" / \"I would be grateful if you could assess Miss Jones at your earliest convenience.\") or add a subject and a finite verb (\"He has poorly controlled diabetes, ...\")."
                    : "Add the patient reference as the subject (e.g. \"However, Ms Hoffmann was initially resistant ...\").";
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    "\"" + SaG1Clip(sentence, 120) + "\" is not a complete sentence: " + reason
                    + ". Join it to the neighbouring sentence or give it its own subject and finite verb.",
                    Quote: sentence, Start: start, End: end, FixSuggestion: fix);
                if (++count >= 5) yield break;
            }
        }
    }

    // ---------------------------------------------------------------------
    // G1b — incomplete_clinical_construction extension: gapped passive
    // auxiliary ("..., and family immunisation discussed.").
    // The noun phrase may not start with an adverb ("-ly") or a past form
    // ("-ed"), but nouns that merely end in "ly" ("family", "supply") are
    // noun-phrase heads: the first CI run showed the audited Garcia sentence
    // never matched because "family" was read as an adverb.
    // ---------------------------------------------------------------------

    private static readonly Regex SaG1ElidedPassiveRe = new(
        @"(?<coord>,\s*and\s+|\band\s+|,\s+)(?<np>(?!(?:he|she|they|it|we|you|i|who|which|that|this|these|those|there|his|her|their|its|both|all|each|either|neither|no|not|with|without|as|by|to|for|in|on|at|of|from|after|before|following|then|also|first|later|since|once|twice|so|but|or|and)\b)(?!(?:[a-z]+ed|(?!(?:family|supply|assembly|belly|anomaly|rally|reply|ally|jelly|lily|italy)\b)[a-z]+ly)\b)(?:(?!(?:was|were|is|are|has|have|had|been|be)\b)[a-z][a-z'’\-]*\s+){0,3}?(?!(?:was|were|is|are|has|have|had|been|be)\b)(?<head>[a-z][a-z'’\-]*))(?<dose>,\s*\d(?:(?!\b(?:was|were|is|are|has|have|had|been|be)\b)(?:[^,.;\n]|\.(?=\d))){0,30}?,?)?\s+(?<pp>added|discussed|arranged|prescribed|given|advised|ordered|requested|performed|organised|organized|booked|scheduled|notified|informed|recommended|administered|sent|explained|offered|provided|obtained|withheld|trialled|trialed|inserted|applied|counselled|counseled|initiated|instituted|discontinued|commenced|started|ceased|stopped|continued|increased|reduced|decreased|changed|switched)\b(?=(?<follow>\s*[.;,]|\s*$|\s+(?:to|for|at|on|in|with|by|from|after|before|until|twice|once|daily|weekly|nightly|each|every|as)\b))",
        RegexOptions.IgnoreCase);

    // Gapping needs an earlier passive to gap FROM ("was notified, and ...").
    private static readonly Regex SaG1PassiveLicensorRe = new(
        @"\b(?:was|were|been)\s+(?:(?:[a-z]+ly|then|also|further)\s+)?[a-z]+(?:ed|en|wn)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1TailPastRe = new(@"\b[a-z]{3,}ed\b", RegexOptions.IgnoreCase);

    private static readonly Regex SaG1FollowPrepositionRe = new(
        @"^\s+(?:to|at|on|in|with|by|from|after|before)\b", RegexOptions.IgnoreCase);

    private static readonly char[] SaG1ClauseEnd = { ',', '.', ';' };

    private static readonly HashSet<string> SaG1IntransitiveCapableParticiples = new(StringComparer.OrdinalIgnoreCase)
    {
        "commenced", "started", "ceased", "stopped", "continued", "increased", "reduced", "decreased", "changed", "switched",
    };

    private static readonly HashSet<string> SaG1ExtraPersonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "physiotherapist", "physiotherapists", "dietitian", "dietician", "pharmacist", "psychologist", "psychiatrist",
        "podiatrist", "optometrist", "educator", "midwife", "paramedic", "paramedics", "registrar", "resident",
        "carer", "carers", "worker", "workers", "manager", "officer", "partner", "neighbour", "neighbours",
        "mum", "dad", "sibling", "siblings", "gps", "therapist", "therapists", "clinician", "clinicians",
    };

    // Departments act as agents ("and cardiology advised on management");
    // they are skipped only when a preposition follows the participle.
    private static readonly HashSet<string> SaG1DepartmentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cardiology", "neurology", "oncology", "radiology", "pathology", "haematology", "hematology", "nephrology",
        "urology", "gastroenterology", "dermatology", "rheumatology", "endocrinology", "psychiatry", "paediatrics",
        "pediatrics", "orthopaedics", "orthopedics", "surgery", "surgeons", "medicine", "emergency", "pharmacy",
        "physiotherapy", "dietetics", "nursing", "palliative", "anaesthetics", "obstetrics", "gynaecology",
        "ophthalmology", "respiratory", "department", "unit", "practice", "laboratory", "service", "services",
    };

    private static readonly HashSet<string> SaG1PersonTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "mr", "mrs", "ms", "miss", "mx", "prof", "professor",
    };

    private static bool SaG1IsPersonWord(string word)
        => PersonSubjects.Contains(word) || SaG1ExtraPersonWords.Contains(word)
           || word.EndsWith("ist", StringComparison.OrdinalIgnoreCase)
           || word.EndsWith("ician", StringComparison.OrdinalIgnoreCase);

    // The existing medication_passive_grammar detector already reports a
    // drug name directly followed by a bare participle; never report it twice.
    private static bool SaG1MedicationPassiveCovers(string text)
    {
        foreach (Match m in MedicationPassiveRe.Matches(text))
        {
            if (m.Groups["aux"].Success || m.Groups["det"].Success) continue;
            var drug = m.Groups["drug"].Value;
            if (MedicationStopWords.Contains(drug) || IntransitiveParticipleSubjects.Contains(drug) || PersonSubjects.Contains(drug)) continue;
            if (drug.Equals("your", StringComparison.OrdinalIgnoreCase)
                || drug.Equals("my", StringComparison.OrdinalIgnoreCase)
                || drug.Equals("our", StringComparison.OrdinalIgnoreCase)
                || (drug.Length > 3 && drug.EndsWith("ly", StringComparison.OrdinalIgnoreCase)))
                continue;
            return true;
        }
        return false;
    }

    private static IEnumerable<LintFinding> DetectSaG1ElidedPassiveAuxiliary(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.BodyParagraphs.Count == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;
        var paragraphSearchFrom = 0;
        foreach (var paragraph in s.BodyParagraphs)
        {
            var paragraphIndex = s.Body.IndexOf(paragraph, paragraphSearchFrom, StringComparison.Ordinal);
            if (paragraphIndex >= 0) paragraphSearchFrom = paragraphIndex + paragraph.Length;
            foreach (var (sentence, sentenceIndex) in SaG1Sentences(paragraph))
            {
                foreach (Match m in SaG1ElidedPassiveRe.Matches(sentence))
                {
                    var before = sentence[..m.Index];
                    if (!SaG1PassiveLicensorRe.IsMatch(before)) continue;

                    var head = m.Groups["head"].Value;
                    var pp = m.Groups["pp"].Value;
                    var np = m.Groups["np"].Value;
                    var dose = m.Groups["dose"];
                    var npWords = np.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    // A person agent makes it active voice ("and Dr Smith advised on
                    // ...", "and the ward nurse informed"). Only the head (or a title)
                    // decides: "family immunisation" is not a person.
                    if (SaG1IsPersonWord(head) || npWords.Any(w => SaG1PersonTitles.Contains(w))) continue;
                    if (SaG1FollowPrepositionRe.IsMatch(m.Groups["follow"].Value) && npWords.Any(w => SaG1DepartmentWords.Contains(w))) continue;
                    if (SaG1IntransitiveCapableParticiples.Contains(pp)
                        && (!dose.Success || IntransitiveParticipleSubjects.Contains(head))) continue;

                    var comma = before.LastIndexOf(',');
                    if (SaG1WithWithoutStartRe.IsMatch(comma >= 0 ? before[(comma + 1)..] : before)) continue;

                    // Reduced relative followed by the real verb ("a core
                    // biopsy performed today confirmed ...").
                    var matchEnd = m.Groups["pp"].Index + m.Groups["pp"].Length;
                    var tail = sentence[matchEnd..];
                    var clauseEnd = tail.IndexOfAny(SaG1ClauseEnd);
                    var clause = clauseEnd >= 0 ? tail[..clauseEnd] : tail;
                    if (SaG1AuxRe.IsMatch(clause) || SaG1IrregularPastRe.IsMatch(clause) || SaG1TailPastRe.IsMatch(clause)) continue;
                    if (SaG1TokenRe.Matches(clause).Any(t => SaG1ThirdPersonVerbs.Contains(t.Value))) continue;

                    var quoteStart = m.Groups["np"].Index;
                    var quote = sentence[quoteStart..matchEnd];
                    if (SaG1MedicationPassiveCovers(sentence[m.Index..matchEnd])) continue;

                    var plural = Regex.IsMatch(head, @"[a-z]s$", RegexOptions.IgnoreCase)
                                 && !Regex.IsMatch(head, @"(?:ss|is|us)$", RegexOptions.IgnoreCase);
                    var doseText = dose.Success ? dose.Value.TrimEnd().TrimEnd(',') + "," : "";
                    var fix = np + doseText + " " + (plural ? "were" : "was") + " " + pp;
                    int? start = paragraphIndex >= 0 ? bodyOffset + paragraphIndex + sentenceIndex + quoteStart : null;
                    int? end = start + quote.Length;
                    yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                        "\"" + quote + "\" omits the passive auxiliary. In a Model Answer every coordinated passive clause carries its own auxiliary: \"" + fix + "\".",
                        Quote: quote, Start: start, End: end, FixSuggestion: fix);
                    if (++count >= 5) yield break;
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // G1c — incomplete_clinical_construction extension: an adjective
    // coordinated into a "with" noun list ("with a petechial rash on the
    // abdomen and legs, bruising on the left arm and unable to touch ...").
    // ---------------------------------------------------------------------

    private static readonly Regex SaG1WithListAdjectiveRe = new(
        @"\bwith\b(?<span>(?:(?!\b(?:am|is|are|was|were|has|have|had|will|would|can|could|should|may|might|must)\b)(?:[^.;\n]|\.(?=\d))){1,160}?)\band\s+(?<adj>unable|able|afebrile|febrile|apyrexial|alert|orientated|oriented|disoriented|disorientated|drowsy|confused|breathless|tachycardic|bradycardic|hypotensive|jaundiced|dehydrated|lethargic|distressed|unwell|unresponsive|incontinent|immobile|reluctant|unwilling|willing)\b(?=\s+to\b|\s*[,.;]|\s*$|\s+(?:when|while|on|at|in|and|despite|throughout)\b)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG1FaultyWithListParallelism(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.BodyParagraphs.Count == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var paragraphSearchFrom = 0;
        foreach (var paragraph in s.BodyParagraphs)
        {
            var paragraphIndex = s.Body.IndexOf(paragraph, paragraphSearchFrom, StringComparison.Ordinal);
            if (paragraphIndex >= 0) paragraphSearchFrom = paragraphIndex + paragraph.Length;
            foreach (Match m in SaG1WithListAdjectiveRe.Matches(paragraph))
            {
                var span = m.Groups["span"].Value.TrimEnd();
                // Only a real list ("with A, B and unable ...") is non-parallel.
                // "comfortable with minimal pain and able to ..." (no list) and
                // "stable, with good saturation, and alert" (parenthetical) are
                // correct coordination with the earlier predicate.
                if (span.EndsWith(',') || !span.Contains(',')) continue;
                var adj = m.Groups["adj"].Value;
                var quote = m.Value.Length > 120 ? "…" + m.Value[^90..] : m.Value;
                int? start = paragraphIndex >= 0 ? bodyOffset + paragraphIndex + m.Index : null;
                int? end = start + m.Length;
                var fix = adj.Equals("unable", StringComparison.OrdinalIgnoreCase) || adj.Equals("able", StringComparison.OrdinalIgnoreCase)
                    ? "..., and was " + adj + " to ... / ... and inability to ..."
                    : "..., and was " + adj;
                yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                    "Faulty parallelism: the \"with ...\" list joins noun phrases to the adjective \"" + adj
                    + "\". Keep the list parallel (\"..., and was " + adj + " ...\" or a noun form such as \"inability to ...\").",
                    Quote: quote, Start: start, End: end, FixSuggestion: fix);
            }
        }
    }

    // ---------------------------------------------------------------------
    // G1d — malformed_word_form: blind find/replace damage.
    // ---------------------------------------------------------------------

    // "an ex-smokes", "a former smokes", and the article-less "non-drinks
    // alcohol" (Nursing, Mr Anthony Nutt).
    private static readonly Regex SaG1AgentVerbFormRe = new(
        @"\b(?:(?:a|an)\s+(?:ex|non|former|current|heavy|light|social|lifelong)[\s\-]*|(?:ex|non)-)(?:smokes|drinks)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1PassivePerfectGerundRe = new(
        @"\b(?<aux>has|have|had)\s+been\s+(?<v>continued|started|stopped|commenced|ceased|quit)\s+(?<g>smoking|drinking|working|taking|using|exercising|attending|walking|driving|eating|playing|injecting)\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex SaG1AdjectiveDrinksVerbRe = new(
        @"\b(?:social|heavy|binge)\s+drinks\s+(?:alcohol|wine|beer|spirits)\b",
        RegexOptions.IgnoreCase);

    private static IEnumerable<LintFinding> DetectSaG1MalformedWordForm(OetRule rule, WritingLintInput input, LetterStructure s)
    {
        if (!input.IsModelAnswer) yield break;
        if (s.Body.Length == 0) yield break;
        var bodyOffset = BodyOffset(s);
        var count = 0;

        foreach (Match m in SaG1AgentVerbFormRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" is not a valid word form. Describe the behaviour instead (\"smoked for thirty-five years and quit seven years ago\", \"drinks alcohol socially\"); a Model Answer never uses the \"ex-smoker\" label.",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "smoked for ... years and quit ... years ago");
            if (++count >= 5) yield break;
        }

        foreach (Match m in SaG1PassivePerfectGerundRe.Matches(s.Body))
        {
            var fix = m.Groups["aux"].Value + " " + m.Groups["v"].Value + " " + m.Groups["g"].Value;
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" is not a valid verb form; write \"" + fix + "\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: fix);
            if (++count >= 5) yield break;
        }

        foreach (Match m in SaG1AdjectiveDrinksVerbRe.Matches(s.Body))
        {
            yield return new LintFinding(rule.Id, ModeSeverity(input, RuleSeverity.Major),
                "\"" + m.Value + "\" uses \"drinks\" as a verb after an adjective; write the behaviour with its frequency, e.g. \"drinks alcohol socially\".",
                Quote: m.Value, Start: bodyOffset + m.Index, End: bodyOffset + m.Index + m.Length,
                FixSuggestion: "drinks alcohol socially");
            if (++count >= 5) yield break;
        }
        // Doubled words ("mellitus mellitus") are reported once, by
        // typographic_corruption (WritingRuleEngine.SeniorAuditG2.cs).
    }
}
