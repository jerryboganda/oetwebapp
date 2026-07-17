# Inline Part B/C Question Text — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task's implementer MUST open and read the exact file section named under **Files** before editing — code blocks below show the intended change, but the surrounding code must be matched from the live file.

**Goal:** Replace the `See PDF` / `Option A/B/C` placeholders on Listening & Reading Part B/C question cards with real, authored (and AI-extractable) inline stem + option text, drop the question-paper PDF for a section once it is fully inline, and guarantee grading is unaffected by option display text.

**Architecture:** The stem/options data path already exists end-to-end; the placeholders come only from the fast Answer-Sheet Builders. We (0) make MCQ grading key on the option **letter/index** so display text is score-neutral, (1) let admins type stem+options in the fast builder, (2) add a per-extract context line + hide the PDF when a section is fully inline, (3) widen the existing Part B/C AI-OCR extractor to also return stem+options, and (4) mirror it all in Reading (4-option Part C).

**Tech Stack:** .NET 8 / EF Core (backend, `LearnerDbContext`), Next.js 15 / React 19 / TypeScript (frontend), Mistral OCR + Claude forced-tool (AI extraction), xUnit (backend tests), Vitest + Testing Library (frontend tests).

## Global Constraints

- Stage explicit paths only — **never `git add -A`**. Never commit secrets/`.env*`. No `Co-Authored-By` trailer (repo `.claude/settings.json` does not set `attribution.commit`).
- Heavy compute (dotnet build/test, EF migrations) runs on **CI**, not locally — push the branch and watch `gh run`. Local: frontend `npm run build` / typecheck + targeted Vitest.
- Branch: `feat/inline-bc-question-text` (already created; spec committed).
- Raw→scaled scoring MUST route only through `OetScoring.OetRawToScaled` — no inline math (CI source-scan `ListeningScoringPathAuditTest` enforces this). This plan does not touch scoring math.
- MCQ option counts are validated at write time: Listening MCQ3 = exactly 3; Reading MCQ3 = 3, MCQ4 = 4. Never change these counts.
- `OptionKey` is the positional letter `A/B/C` (Listening) — the stable grading key. Option **order** is load-bearing; never reorder options without remapping the correct key.
- Placeholder sentinels to treat as "not authored": stem == `See PDF` (case-insensitive); options == `Option A`/`Option B`/`Option C` (+`Option D` for Reading MCQ4).

---

## File map

**Phase 0 (grading safety, Listening)**
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningGradingService.cs` (Evaluate MC3, l.362-402)
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningLearnerService.cs` (`LearnerQuestionDto` l.2488-2502, `MapRelationalQuestion` l.1951-1991, JSON grader path)
- Modify: `lib/listening-api.ts` (`ListeningSessionQuestionDto` ~l.126-134)
- Modify: `components/domain/listening/BCQuestionRenderer.tsx`
- Modify: `app/listening/player/[id]/page.tsx` (BCQuestionRenderer usage l.1758-1772)
- Test: `backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningGradingServiceTests.cs`, `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx` (create if absent)

**Phase 1 (manual inline text, Listening builder)**
- Modify: `app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.tsx`
- Test: `app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.test.tsx`

**Phase 2 (context field + PDF gate, Listening)**
- Modify: `backend/src/OetWithDrHesham.Api/Domain/ListeningEntities.cs` (ListeningExtract ~l.260)
- Create: `backend/src/OetWithDrHesham.Api/Data/Migrations/<timestamp>_AddListeningExtractContextIntro.cs`
- Modify: `backend/src/OetWithDrHesham.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningAuthoringService.cs` (records l.148-220, normalize l.1093-1136, apply patch l.1662-1677)
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningBackfillService.cs` (extract projection ~l.223-243)
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningLearnerService.cs` (`ListeningExtractMetaDto` l.3350, `MapRelationalExtract` l.2002-2021, JSON extract path; inlineTextReady computation)
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningStructureService.cs` (`IsPdfBackedItem` reuse for inlineTextReady, l.949-953)
- Modify: `lib/listening-authoring-api.ts` (extract contract l.135-160, patch l.281-297), `lib/listening-api.ts` (extract meta DTO l.157-194)
- Modify: `app/listening/player/[id]/page.tsx` (PDF gate l.1673-1685, section map l.1688-1689)

**Phase 3 (AI widening, Listening B/C)**
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningPartBCExtractionService.cs` (SystemPrompt l.329-356, ToolSchemaJson l.358-377, `BcToolAnswer` l.178, `ListeningPartBCAnswer` l.34, ValidateAndProject l.143, max_tokens l.218)
- Modify: `lib/listening-authoring-api.ts` (`ListeningPartBCAnswer` l.442-458)
- Modify: `app/admin/content/listening/[paperId]/questions/ListeningPartAiExtraction.tsx` (Row l.65, review list l.248-274, onSaveAll l.136-154)
- Test: `backend/tests/OetWithDrHesham.Api.Tests/Listening/` (new extraction test)

**Phase 4 (Reading parity)**
- Modify: `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.tsx` (placeholders l.67-68, l.235-238, parseExisting l.119-133)
- Modify: `backend/src/OetWithDrHesham.Api/Domain/ReadingEntities.cs` (+ migration + snapshot) for the Reading per-part/section context field
- Modify: `backend/src/OetWithDrHesham.Api/Services/Reading/ReadingStructureService.cs` (publish gate l.1021-1045)
- Modify: `backend/src/OetWithDrHesham.Api/Endpoints/ReadingLearnerEndpoints.cs` (questionPaperAssets l.72-94; inlineReady flag)
- Modify: `app/reading/paper/[paperId]/page.tsx` (PartBody PDF viewer l.1474, grid l.1469)
- Test: `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.test.tsx`, reading publish-gate + 4-option round-trip backend tests

---

## Phase 0 — Grading safety (Listening B/C by key/index)

Goal: option **letter/index** is the grading key; display text is score-neutral; legacy text/index answers still resolve.

### Task 0.1: Grade-time legacy-answer resolver in the V2 grader

**Files:**
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningGradingService.cs` (Evaluate, MC3 branch l.370-378)
- Test: `backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningGradingServiceTests.cs`

**Interfaces:**
- Consumes: `ListeningOptionIdHelper.ResolveLegacyAnswer(string? legacyAnswer, string questionId, IReadOnlyList<string> currentOptions)` → returns `"lo-{qid}-{index}"` or null; and by index we can map to `OptionKey`.
- Produces: MC3 grading that is correct when the saved answer is the option **letter (`A`)**, the option **text**, a bare **index**, or a stable **option id** — all resolving to the same option.

- [ ] **Step 1: Write the failing test** — same correct option graded correctly whether the saved answer is the letter, the option text, or the index.

```csharp
[Fact]
public void Evaluate_Mc3_matches_by_letter_text_or_index()
{
    var q = new ListeningQuestion
    {
        Id = "q1",
        QuestionType = ListeningQuestionType.MultipleChoice3,
        Points = 1,
        Options = new List<ListeningQuestionOption>
        {
            new() { OptionKey = "A", DisplayOrder = 0, Text = "The patient is stable", IsCorrect = false },
            new() { OptionKey = "B", DisplayOrder = 1, Text = "The dosage must be reduced", IsCorrect = true },
            new() { OptionKey = "C", DisplayOrder = 2, Text = "Refer to a specialist", IsCorrect = false },
        },
    };

    (bool, ListeningDistractorCategory?, ListeningMissReason?) Grade(string saved)
        => ListeningGradingService.Evaluate(
            q,
            new ListeningAnswer { UserAnswerJson = JsonSerializer.Serialize(saved) });

    Assert.True(Grade("B").Item1);                          // letter
    Assert.True(Grade("The dosage must be reduced").Item1); // real option text
    Assert.True(Grade("1").Item1);                          // zero-based index
    Assert.False(Grade("A").Item1);                         // wrong option by letter
    Assert.False(Grade("Refer to a specialist").Item1);     // wrong option by text
}
```

- [ ] **Step 2: Run it and confirm it fails** — `dotnet test` on CI (push branch); expect the text/index cases to fail (current code only matches `OptionKey`).

- [ ] **Step 3: Implement the resolver in the MC3 branch.** Replace the direct `OptionKey` match with a resolve-then-match that tries key, then text, then index.

```csharp
case ListeningQuestionType.MultipleChoice3:
{
    var selected = TryReadString(ans.UserAnswerJson);
    if (string.IsNullOrEmpty(selected)) return (false, null, null);

    var ordered = q.Options.OrderBy(o => o.DisplayOrder).ToList();

    // 1) direct OptionKey (the canonical, forward path — letter A/B/C)
    var opt = ordered.FirstOrDefault(o =>
        string.Equals(o.OptionKey, selected, StringComparison.OrdinalIgnoreCase));

    // 2) legacy: saved value is the option TEXT or a numeric INDEX or a stable id
    if (opt is null)
    {
        var resolvedId = ListeningOptionIdHelper.ResolveLegacyAnswer(
            selected, q.Id, ordered.Select(o => o.Text).ToList());
        var idx = resolvedId is null ? null : ListeningOptionIdHelper.ExtractOptionIndex(resolvedId);
        if (idx is int i && i >= 0 && i < ordered.Count) opt = ordered[i];
    }

    if (opt is null) return (false, null, null);
    return (opt.IsCorrect, opt.IsCorrect ? null : opt.DistractorCategory, null);
}
```

- [ ] **Step 4: Run tests and confirm pass** (CI). Also run the existing `ListeningGradingServiceTests` suite to confirm no regression.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetWithDrHesham.Api/Services/Listening/ListeningGradingService.cs \
        backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningGradingServiceTests.cs
git commit -m "fix(listening): grade Part B/C MCQ by option key/text/index so real option text is score-neutral"
```

### Task 0.2: JSON-store grader parity

**Files:**
- Modify: the JSON-attempt grading path (the `la-`/`Attempt.AnswersJson` grader — locate the method that grades non-relational attempts in `ListeningLearnerService.cs` / whichever grader `SubmitAttemptAsync` calls for JSON papers).
- Test: extend `ListeningGradingServiceTests` or add a JSON-attempt grading test.

**Interfaces:**
- Produces: JSON-store MCQ grading resolves saved text/index/letter identically to Task 0.1 (letter is canonical).

- [ ] **Step 1: Locate the JSON grader.** Read `ListeningLearnerService.cs` around `SubmitAttemptAsync` / `DeserializeAnswers` and find where a JSON-store MCQ answer is compared to the question's correct answer. Confirm whether it compares to option text, letter, or index.
- [ ] **Step 2: Write a failing test** mirroring 0.1 for a JSON-backed paper (real option text, chosen by letter, grades correct).
- [ ] **Step 3: Apply the same resolve-by-key/text/index logic** (reuse `ListeningOptionIdHelper.ResolveLegacyAnswer` against the JSON options array; letter is canonical).
- [ ] **Step 4: Run tests (CI) and confirm pass.**
- [ ] **Step 5: Commit** (`fix(listening): JSON-store MCQ grading resolves letter/text/index`).

### Task 0.3: Learner submits the option key; text is display-only

**Files:**
- Modify: `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningLearnerService.cs` — in `LearnerQuestionDto` (l.2488-2502) and `MapRelationalQuestion` (l.1951-1991) emit an `optionKeys: ["A","B","C"]` array parallel to `options` (keep `options` = texts for display).
- Modify: `lib/listening-api.ts` — `ListeningSessionQuestionDto` (~l.126-134) add `optionKeys?: string[]`.
- Modify: `components/domain/listening/BCQuestionRenderer.tsx` — accept `optionKeys?: string[]`; the selected `value` and `onChange` payload become the **key** (`optionKeys[i]` ?? letter), while the button still renders the option text and the A/B/C badge.
- Modify: `app/listening/player/[id]/page.tsx` (l.1758-1772) — pass `optionKeys={question.optionKeys}`; `value` compares against key.
- Test: `components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`.

**Interfaces:**
- Consumes: `optionKeys[]` from the session DTO (backend Task 0.3 backend half).
- Produces: `onChange(optionKey)` — the persisted answer is the letter. Falls back to `String.fromCharCode(65+index)` when `optionKeys` is absent (older cached sessions), so behavior is safe pre/post deploy.

- [ ] **Step 1: Write the failing test** — clicking option index 1 calls `onChange('B')` (key), not the option text; the card still shows the option text.

```tsx
it('submits the option key, not the display text', async () => {
  const onChange = vi.fn();
  render(
    <BCQuestionRenderer
      questionNumber={25} partLabel="PART B" prompt="What does the nurse advise?"
      options={['Increase fluids', 'Reduce the dose', 'Refer on']}
      optionKeys={['A', 'B', 'C']}
      value="" onChange={onChange}
    />,
  );
  await userEvent.click(screen.getByText('Reduce the dose'));
  expect(onChange).toHaveBeenCalledWith('B');
  expect(screen.getByText('Reduce the dose')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run it and confirm it fails** — `npx vitest run components/domain/listening/__tests__/BCQuestionRenderer.test.tsx`.
- [ ] **Step 3: Implement.** Add `optionKeys?: string[]` to `BCQuestionRendererProps`; compute `const key = optionKeys?.[index] ?? String.fromCharCode(65 + index);`; selection compares `value === key`; `onClick`/keyboard nav call `onChange(key)`; the visible label still renders `option` (text) and the badge renders `String.fromCharCode(65+index)`.
- [ ] **Step 4: Add the backend half** — emit `optionKeys` in both learner DTO builders; add the field to `ListeningSessionQuestionDto`; pass it in the player.
- [ ] **Step 5: Run tests (Vitest) + `npm run build` typecheck; confirm pass.**
- [ ] **Step 6: Commit**

```bash
git add components/domain/listening/BCQuestionRenderer.tsx \
        components/domain/listening/__tests__/BCQuestionRenderer.test.tsx \
        lib/listening-api.ts app/listening/player/[id]/page.tsx \
        backend/src/OetWithDrHesham.Api/Services/Listening/ListeningLearnerService.cs
git commit -m "feat(listening): Part B/C card submits option key (letter); option text is display-only"
```

### Task 0.4: End-to-end grading-neutrality regression

- [ ] **Step 1:** Backend test — build one paper with placeholder options and one with real prose options, same correct letter `B`, same learner selection → identical `RawScore`. Assert placeholder-vs-prose parity.
- [ ] **Step 2:** Run on CI; confirm pass.
- [ ] **Step 3:** Commit (`test(listening): option text is grading-neutral (placeholder vs prose parity)`).

---

## Phase 1 — Manual inline text in the Listening fast builder

Goal: admins type stem + 3 option texts; placeholders gone; advanced-editor text never clobbered.

### Task 1.1: Add stem + option-text inputs to `ListeningAnswerSheetBuilder`

**Files:**
- Modify: `app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.tsx`
- Test: `app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.test.tsx`

**Interfaces:**
- Consumes: `ListeningAuthoredQuestion { stem, options[], correctAnswer, explanation }` (already supports stem/options).
- Produces: saved questions carry the typed `stem` (fallback `See PDF` only when blank) and typed `options` (fallback `Option A/B/C` only when blank); correct answer stays the letter; re-save preserves existing real text.

- [ ] **Step 1: Write failing tests:** (a) typing a stem + three option texts and saving calls `replaceListeningStructure` with those exact values (not `See PDF`/`Option A`); (b) a question whose existing `stem` is real prose is seeded into the row and NOT reset to `See PDF` after a re-save with no stem edit.
- [ ] **Step 2: Run and confirm fail** — `npx vitest run app/admin/content/listening/[paperId]/questions/ListeningAnswerSheetBuilder.test.tsx`.
- [ ] **Step 3: Implement.**
  - Extend `BuilderRow` with `stem: string` and `options: string[]` (length 3).
  - `buildRows`: seed `stem` from `existing?.stem` (blank if it equals the `See PDF` sentinel) and `options` from `existing?.options` (blank each if it equals `Option {A|B|C}`); keep `correctAnswer`/`rationale`.
  - Render a stem `Textarea` and three option `Input`s per row (keep the correct-letter `Select` + rationale `Textarea`).
  - `handleSaveAll`: build each question with `stem: row.stem.trim() || 'See PDF'` and `options: row.options.map((o,i) => o.trim() || MCQ3_OPTIONS[i])`; keep `correctAnswer: row.correctAnswer`. Do NOT hardcode the placeholders unconditionally (remove l.183-184 hardcode).
- [ ] **Step 4: Run tests + typecheck; confirm pass.**
- [ ] **Step 5: Commit** (`feat(listening): fast builder captures real stem + option text (no more See PDF placeholder)`).

---

## Phase 2 — Per-extract context + drop the PDF for inline sections

### Task 2.1: `ContextIntro` column + EF migration

**Files:**
- Modify: `backend/src/OetWithDrHesham.Api/Domain/ListeningEntities.cs` (ListeningExtract, beside `NotesBodyMarkdown` ~l.260)
- Create: migration `<timestamp>_AddListeningExtractContextIntro.cs`
- Modify: `LearnerDbContextModelSnapshot.cs`

- [ ] **Step 1:** Add `public string? ContextIntro { get; set; }` with `[MaxLength(2048)]` to `ListeningExtract`.
- [ ] **Step 2:** Generate the migration on CI (owner rule: migrations on CI). Locally scaffold via the `/new-migration` command if available; otherwise author the additive `AddColumn<string>("ContextIntro", "ListeningExtracts", nullable: true, maxLength: 2048)` up + `DropColumn` down, and update the snapshot.
- [ ] **Step 3:** Push branch; confirm the migration + build are green on CI.
- [ ] **Step 4:** Commit (`feat(listening): add ListeningExtract.ContextIntro column + migration`).

### Task 2.2: Carry `ContextIntro` through authoring, storage, backfill, learner DTO

**Files:** `ListeningAuthoringService.cs` (records l.148-220; `NormalizeExtractFromStorage` l.1093; `NormalizeExtractForStorage` l.1108; `ApplyExtractPatch` l.1662-1677), `ListeningBackfillService.cs` (~l.223-243), `ListeningLearnerService.cs` (`ListeningExtractMetaDto` l.3350, `MapRelationalExtract` l.2002-2021, JSON extract path), `lib/listening-authoring-api.ts` (l.135-160, l.281-297), `lib/listening-api.ts` (l.157-194).

**Interfaces:**
- Produces: `contextIntro` round-trips admin → storage → relational → learner; it is NOT force-nulled for B/C (contrast the `notesBody` Part-A gate).

- [ ] **Step 1:** Backend test — set `contextIntro` on a B/C extract via the authoring patch, reload, assert it persists and surfaces in the learner extract DTO (relational path). Add a JSON-path assertion too.
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement:** add `ContextIntro` to `ListeningAuthoredExtract` + `ListeningExtractPatch` + `ApplyExtractPatch`; read key `"contextIntro"` in `NormalizeExtractFromStorage`; persist in `NormalizeExtractForStorage` **without** the `IsPartANotesCode` gate; project onto the relational row in backfill (do NOT null for B/C); add to `ListeningExtractMetaDto` + populate in `MapRelationalExtract` and the JSON `ExtractExtractMetadata` path; add `contextIntro` to the TS contracts.
- [ ] **Step 4: Run tests (CI) + typecheck; confirm pass.**
- [ ] **Step 5: Commit** (`feat(listening): persist + serve per-extract ContextIntro for Part B/C`).

### Task 2.3: `inlineTextReady` flag + player PDF gate + context render

**Files:** `ListeningLearnerService.cs` (compute per-section `inlineTextReady`), `ListeningStructureService.cs` (reuse `IsPdfBackedItem` l.949-953 as the placeholder test), `lib/listening-api.ts` (add `inlineTextReady?: boolean` to the section/extract DTO), `app/listening/player/[id]/page.tsx` (PDF gate l.1673-1685; context render in the section map l.1688-1689).

**Interfaces:**
- Consumes: per-section `inlineTextReady` (true when every options-bearing question in the section has a non-placeholder stem AND non-placeholder options) + `contextIntro`.
- Produces: PDF viewer + empty-state hidden for a B/C section when `inlineTextReady`; context line rendered once per extract above the cards. Part A untouched.

- [ ] **Step 1:** Frontend test (or player unit test) — a section whose questions carry real text does not render `ListeningQuestionPaperViewer`; one with placeholders still does; Part A note/overlay path unchanged.
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement:** backend computes `inlineTextReady` (options-bearing questions only, using `IsPdfBackedItem` for the stem sentinel + a placeholder-options check); expose on the DTO. In the player, derive `currentSectionInlineBcReady` and change the gate to `currentQuestionPaperUrl && !currentSectionInlineBcReady ? <ListeningQuestionPaperViewer/> : (currentSectionHasNotes || currentSectionInlineBcReady) ? null : <empty-state/>`. Render `contextIntro` (once per extract) above the question cards.
- [ ] **Step 4: Verify in preview** — start dev server, open a B/C paper with inline text, confirm no PDF + context shows; a placeholder paper still shows the PDF.
- [ ] **Step 5: Run tests + typecheck; commit** (`feat(listening): hide question-paper PDF for Part B/C sections authored inline; show per-extract context`).

---

## Phase 3 — AI "Read from PDF" returns stem + option text (Listening B/C)

### Task 3.1: Widen the extractor tool + prompt + validation

**Files:** `backend/src/OetWithDrHesham.Api/Services/Listening/ListeningPartBCExtractionService.cs` (SystemPrompt l.329-356, ToolSchemaJson l.358-377, `BcToolAnswer` l.178, `ListeningPartBCAnswer` l.34, ValidateAndProject l.143, max_tokens l.218). Test: new extraction unit test.

**Interfaces:**
- Produces: `ListeningPartBCAnswer { int Number, string CorrectAnswer, string? Rationale, string? Stem, string? OptionA, string? OptionB, string? OptionC }`.

- [ ] **Step 1:** Test — given a stub AI response including stems + option texts, `ValidateAndProject` returns them; given a response missing them, it degrades to the existing stub/review warning (no hard-fail) and still yields the letter.
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement:** add `stem`, `optionA`, `optionB`, `optionC` to the tool JSON schema + system prompt (grounded in the OCR'd question paper); add the fields to `BcToolAnswer` + `ListeningPartBCAnswer`; carry them through `ValidateAndProject` as soft/optional; raise `max_tokens` above 4000 (e.g. 8000) so 12 Part C questions don't truncate.
- [ ] **Step 4: Run tests (CI); confirm pass.**
- [ ] **Step 5: Commit** (`feat(listening): Part B/C AI extractor also returns stem + option texts`).

### Task 3.2: Prefill the review UI + save real text

**Files:** `lib/listening-authoring-api.ts` (`ListeningPartBCAnswer` l.442-458 — add stem+options), `app/admin/content/listening/[paperId]/questions/ListeningPartAiExtraction.tsx` (Row l.65, review list l.248-274, onSaveAll l.136-154).

- [ ] **Step 1:** Frontend test — an extraction result with stems+options prefills the row inputs; `onSaveAll` sends the extracted/edited stem+options (placeholders only when left blank).
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement:** extend the TS `ListeningPartBCAnswer` type; add stem + 3 option inputs to each review row; seed them from the extraction; in `onSaveAll` replace the hardcoded `stem:'See PDF'` (l.144) and `options:[...MCQ3_OPTIONS]` (l.145) with `stem: row.stem.trim() || 'See PDF'` and per-option fallbacks.
- [ ] **Step 4: Run tests + typecheck; confirm pass.**
- [ ] **Step 5: Commit** (`feat(listening): AI extraction panel prefills + saves real stem/options for Part B/C`).

---

## Phase 4 — Reading B/C parity (4-option Part C)

### Task 4.1: `ReadingAnswerSheetBuilder` captures stem + option text (no clobber)

**Files:** `app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.tsx` (l.67-68, l.119-133, l.235-238). Test: `ReadingAnswerSheetBuilder.test.tsx`.

- [ ] **Step 1:** Failing tests — save writes typed stem + option texts (not `See PDF`/`Option A`); `parseExisting` seeds an existing real stem so re-save doesn't clobber; MCQ3 keeps 3 options, MCQ4 keeps 4.
- [ ] **Step 2: Run/confirm fail** (`npx vitest run .../ReadingAnswerSheetBuilder.test.tsx`).
- [ ] **Step 3: Implement:** extend the row model + `parseExisting`/`buildRows` to read `stem` and option texts; render a stem `Textarea` + 3/4 option inputs; on save write real values with placeholder fallbacks; keep exact option counts.
- [ ] **Step 4: Run tests + typecheck; commit** (`feat(reading): fast builder captures real stem + option text for Part B/C`).

### Task 4.2: Reading per-part/section context field + migration

**Files:** `backend/src/OetWithDrHesham.Api/Domain/ReadingEntities.cs` (+ migration + snapshot), Reading structure/authoring service persist/read, `ReadingLearnerEndpoints.cs` projection.

- [ ] **Step 1:** Backend test — context round-trips to the learner projection for B/C.
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement** the additive nullable column + wiring (mirror Listening Task 2.2). Migration on CI.
- [ ] **Step 4: Run tests (CI); commit** (`feat(reading): per-part context field for Part B/C`).

### Task 4.3: Drop the PDF for inline B/C + relax the publish gate

**Files:** `backend/src/OetWithDrHesham.Api/Services/Reading/ReadingStructureService.cs` (publish gate l.1021-1045), `backend/src/OetWithDrHesham.Api/Endpoints/ReadingLearnerEndpoints.cs` (questionPaperAssets l.72-94 + inline-ready flag), `app/reading/paper/[paperId]/page.tsx` (PartBody PDF viewer l.1474, grid l.1469).

**Interfaces:**
- Produces: B/C part with complete inline stems+options is publish-ready without a QuestionPaper PDF; Part A still requires its PDF; learner paper page hides `<ReadingPdfViewer>` for B/C when inline-ready and widens the questions column.

- [ ] **Step 1:** Backend test — a B/C part with full inline text + no PDF passes `ValidatePaperAsync`; Part A with no PDF still fails; 4-option Part C round-trips through validate/project. Frontend test — B/C paper page with inline-ready hides the PDF viewer.
- [ ] **Step 2: Run/confirm fail.**
- [ ] **Step 3: Implement:** part-scope the PDF-required error so B/C-with-inline-text is exempt (keep Part A); expose an inline-ready signal on the learner structure so the client suppresses the PDF; conditionally hide `<ReadingPdfViewer>` in PartBody and give the questions column full width. Do NOT reorder options.
- [ ] **Step 4: Verify in preview** (a B/C reading paper with inline text shows no PDF; Part A unchanged).
- [ ] **Step 5: Run tests (CI + Vitest); commit** (`feat(reading): drop question-paper PDF for inline Part B/C; relax publish gate (Part A keeps PDF)`).

### Task 4.4 (optional): Reading AI extraction prefill

- [ ] Wire `ReadingExtractionService` output (already emits full manifests with stem/options) into the rebuilt Reading builder fields, mirroring Listening Task 3.2. Commit if in scope for this release.

---

## Final integration

- [ ] Push branch; ensure `Build & Deploy (web + API)` workflow is green (the real prod gate; ignore flaky `QA Smoke`). Verify migrations applied.
- [ ] Manual verify on a preview/staging paper for both modules: author inline (manual + AI), confirm learner shows heading + options inline, PDF hidden, grading correct for a chosen letter.
- [ ] Squash-merge to `main` (`gh pr merge <#> --squash --admin --delete-branch`) → triggers blue/green prod deploy. Hand off to owner for live verification.

## Self-review (plan vs spec)

- **Spec coverage:** D1 → Phase 0 (0.1-0.4). Goal-1 manual → Phase 1; AI → Phase 3. Goal-2 PDF-drop → Phase 2.3 / 4.3 (D2, D5). Goal-3 context → Phase 2.1-2.2 / 4.2 (D3). Goal-4 grading → Phase 0 (D1). Goal-5 Reading + 4-option → Phase 4. D4 no-clobber → Tasks 1.1 / 4.1. All covered.
- **Placeholder scan:** no TBD/TODO; each task names exact files + concrete edits. Two tasks (0.2, 2.x JSON path) require reading a method to confirm the exact matcher before editing — flagged explicitly, not hand-waved.
- **Type consistency:** `ResolveLegacyAnswer`/`ExtractOptionIndex` signatures match `ListeningOptionIdHelper`. `optionKeys[]` used consistently in 0.3 (DTO, renderer, player). `ListeningPartBCAnswer` fields consistent between 3.1 (backend) and 3.2 (frontend). `ContextIntro`/`contextIntro` naming consistent (C# PascalCase ↔ TS camelCase).
