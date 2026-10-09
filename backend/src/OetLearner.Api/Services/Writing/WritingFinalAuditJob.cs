using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

// ================================================================================================================
// TEMPORARY one-off job (owner directive, 9 Oct 2026: "apply the repairs, run the 45-case self-check and the final
// live verification yourself; I do not want to do anything"). Remove this file, its two registrations and the
// /health/writing-final-audit route in Program.cs once Writing is closed.
//
// What it does, once per API start in Production, 75 s after boot, with NO credential and NO AI call ($0 Writing rule):
//   1. fixes the one OCR typo in a task text ("Nor=h Adelaide" -> "North Adelaide");
//   2. applies the audited Model Answer repairs: a repair is applied only when every `find` text is still in the LIVE
//      answer, the full deterministic Model Answer gate passes on the edited text (ValidateAsync), and only then is it
//      imported and approved. The previous text is written to the audit log. A failing gate changes nothing;
//   3. probes the embedded text of a few original stimulus PDFs for the names / titles the stored notes lost
//      (embedded text only: no OCR provider is called);
//   4. runs the Validator self-check on the server;
//   5. re-validates EVERY stored Model Answer against the CURRENT case notes without applying anything.
// The outcome is kept in memory and served at GET /health/writing-final-audit (anonymous, scenario ids and rule ids
// only, never a name or a letter). Re-running is harmless: repairs whose text is already changed report
// "already-applied".
// ================================================================================================================

public sealed record WritingFinalAuditRepairOutcome(string Id, string Label, string Outcome, string? Detail);

public sealed record WritingFinalAuditProbe(string Id, string Label, string Token, int Found, string? Context);

public sealed record WritingFinalAuditSelfCheck(int Total, int Passed, int Failed, IReadOnlyList<string> FailedCases);

public sealed record WritingFinalAuditActive(
    int VisibleVerified,
    int VisibleAnyVersion,
    int Checked,
    int Passed,
    int Failed,
    IReadOnlyList<string> SourceConflicts,
    IReadOnlyList<string> VisibleFailures,
    IReadOnlyList<string> StaleVisible);

public sealed record WritingFinalAuditSnapshot(
    string State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ValidatorVersion,
    WritingFinalAuditSelfCheck? SelfCheck,
    WritingFinalAuditActive? Active,
    IReadOnlyList<WritingFinalAuditRepairOutcome> Repairs,
    IReadOnlyList<WritingFinalAuditProbe> PdfProbes,
    IReadOnlyList<string> Errors);

public sealed class WritingFinalAuditStatus
{
    private volatile WritingFinalAuditSnapshot _snapshot = new("pending", null, null, null, null, null, [], [], []);

    public WritingFinalAuditSnapshot Snapshot
    {
        get => _snapshot;
        set => _snapshot = value;
    }
}

internal static class WritingFinalAuditSpec
{
    internal sealed record Repair(string Id, string Label, (string Find, string Replace)[] Edits);

    internal sealed record PdfProbe(string Id, string Label, string[] Tokens);

    internal const string TaskFixId = "48c52e8e-1019-40a8-83a1-897f23cd3c6d";

    internal static readonly Repair[] Repairs =
    [
        new("035403b8-a8da-42a9-97ea-298b4038b55c", "pack 17", [("he has been informed of the possible side effects of the antibiotics", "he is to be informed of the possible side effects of the antibiotics")]),
        new("066ecfa1-c02e-4b71-9d9c-6e5393bb6da8", "pack 18", [("\n\n2 September 2009\n\nDear", "\n\nDear")]),
        new("0fc02e37-24b0-47cb-a408-5a090aa587ac", "pack 23", [("\n\n6 April 2019\n\nDear", "\n\nDear")]),
        new("1443641c-09b5-4b30-bc9a-01250cc1eaf1", "pack 25", [("\nRe: Ms Jane Robinson, aged 19\n", "\nRe: Ms Jane Robinson, DOB: 26 October 1988\n")]),
        new("2b7fae26-e8b4-4803-9be1-99f8524dbcfd", "pack 30", [("Referrals have also been initiated to", "Referrals are also to be initiated to")]),
        new("2dc96911-e1eb-4963-a156-1a6465be3be1", "pack 31", [("\n\n4 February 2014\n\nDear", "\n\nDear"), ("Mrs Casey was admitted today after fainting and falling", "Mrs Casey was admitted on 4 February 2014 after fainting and falling")]),
        new("422ee8ad-c86e-4a49-9269-fa7f0381bb02", "pack 38", [("\nRe: Ms Gemma Brown\n", "\nRe: Ms Gemma Brown, DOB: 19 January 1991\n")]),
        new("4753a45d-359e-4571-b5dc-4b1615c2d4a7", "pack 42", [("renal failure secondary to dehydration, mild dementia and pneumonia.", "renal failure secondary to dehydration and mild dementia.")]),
        new("48c52e8e-1019-40a8-83a1-897f23cd3c6d", "pack 43", [("\nAdelaide 3001", "\nNorth Adelaide 3001")]),
        new("7436a118-c943-478d-b8ef-626565204522", "pack 54", [("\nRe: Ms Ling Wu\n", "\nRe: Ms Ling Wu, DOB: 1 March 1996\n")]),
        new("75134963-0c27-4481-b9f5-2b1786421781", "pack 55", [("\n\n8 July 2017\n\nDear Admissions Officer,", "\n\n11 July 2017\n\nDear Admissions Officer,"), ("Today, endoscopy, biopsy and barium swallow confirmed", "On 8 July, endoscopy, biopsy and barium swallow confirmed")]),
        new("7b09ff8d-9448-49c5-9fb3-e232bc0e20d5", "pack 57", [("\nRe: Ms Nina Sharman\n", "\nRe: Ms Nina Sharman, DOB: 9 February 1951\n")]),
        new("9489ce12-1555-4dce-958d-d648e0218e74", "pack 64", [(", requiring significant assistance.", ".")]),
        new("95f2ea74-0f4e-4424-af68-ced741716552", "pack 66", [("There is no history of such infection, IV drug use or overseas travel.", "Her partner has no IV drug use or recent overseas travel."), ("She has been on the oral contraceptive pill for twelve months.", "She has taken the contraceptive pill for twelve months."), ("her last sexual contact was fourteen days ago", "her last sexual contact was fourteen days before presentation")]),
        new("a3d1b730-604c-4af7-82ef-cf1c40015bac", "pack 74", [("\n\n22 April 2015\n\nDear", "\n\nDear"), ("re-dress Ms Norris's wound today.", "re-dress her wound on 22 April.")]),
        new("bfa16ff7-8dac-42c4-b5a2-632f1d04f040", "pack 84", [("has untreated dyslipidaemia", "has previously untreated dyslipidaemia")]),
        new("c110e41b-a05f-4c1a-8500-af7a9dc71b74", "pack 85", [("\n\n11 January 2018\n\nDear", "\n\nDear")]),
        new("cb30e37c-9a24-4a9a-a7ae-54eb7f473a5f", "pack 87", [("Quitline contact has been encouraged", "Quitline contact is to be encouraged")]),
        new("f08ba66d-735a-4509-85f6-59954cf029f6", "pack 97", [("\n\n28 June 2017\n\nDear", "\n\n2 July 2017\n\nDear"), ("Mrs Davies was admitted today after a fall at home", "Mrs Davies was admitted on 28 June after a fall at home")]),
        new("fae4d05e-3d0a-4d67-9bdc-cfd511644814", "pack 101", [(" upon his request,", "")]),
        new("ea93ffbd-a2cf-4027-9767-9f40392804b9", "pack 107", [("I would be grateful if you could arrange a bath board or alternative shower equipment and temporary domestic support at your earliest convenience.", "I would be grateful if you could review bath board or alternative shower equipment needs and assess temporary domestic support at your earliest convenience.")]),
        new("245b5873-ed37-4dd6-9226-8c4280a82969", "pack 117", [("increase the risk of a blood clot.", "increase the risk of blood clot failure.")]),
        new("843c7231-6322-4731-bb8c-b352b3619054", "pack 126", [("Re: Mr Ian Roden, DOB", "Re: Mr Alex Roden, DOB"), ("prescribed for Mr Ian Roden by your locum", "prescribed for Mr Alex Roden by your locum")]),
        new("8dc0a8b6-071a-4705-b41a-cbe0a79281ab", "pack 128", [("I am writing to request your prescribing care for Mrs Tomomi Aoki, who has influenza, and to provide the medication history you requested.", "I am writing to outline the medication history of Mrs Tomomi Aoki, who has influenza, as you requested."), ("I would be grateful if you could continue Mrs Aoki's vitamin B6 and prenatal vitamin prescriptions.", "I would be grateful if you could consider this medication history in Mrs Aoki's continuing care.")]),
        new("c371b7c0-24fa-4476-8208-c610f41262bf", "pack 134", [("Today, Mrs Katrina Morrison presented", "Today, Ms Katrina Morrison presented")]),
        new("d092de42-1f30-4fdb-82c0-133ef3a78793", "pack 139", [("I counselled him on lifestyle, exercise and diet.", "I plan to counsel him on lifestyle, exercise and diet.")]),
        new("0a935f31-5d0f-42f9-b36a-68090d3ed939", "pack 142", [("\nRe: Mr Anthony Miller, aged 58\n", "\nRe: Mr Anthony Miller, DOB: 28 February 1968\n")]),
        new("8ce14aa5-d564-401a-86dc-e0039e7ae437", "pack 180", [(" Insulin, 50 IU and a statin, 40 mg, were continued.", "")]),
        new("dced34e1-ab84-43e3-af54-3e124d08d5b8", "pack 194", [("I advised smoking cessation and discussed likely investigations.", "Smoking cessation and likely investigations will be discussed.")]),
        new("065df5a5-52e3-48e4-bc6c-df726f8d4084", "pack 198", [("I would be grateful if you could monitor Mrs Jackson's pregnancy and discuss her delivery options.", "I would be grateful if you could discuss her home delivery request with Mrs Jackson.")]),
    ];

    internal static readonly PdfProbe[] PdfProbes =
    [
        new("8dc5d15f-8eef-437d-80f8-f3985ca3b76b", "pack 61", ["City of London", "London"]),
        new("f08ba66d-735a-4509-85f6-59954cf029f6", "pack 97", ["July", "ready for discharge"]),
        new("4f8f628a-f9af-472c-ac73-3e7d1467cde9", "pack 46", ["insulin", "meal"]),
        new("2e32afab-118a-4c2b-9e1b-e5b24d6de735", "pack 32", ["Mr ", "Mrs"]),
        new("843c7231-6322-4731-bb8c-b352b3619054", "pack 126", ["Ian", "Alex"]),
    ];
}

public sealed class WritingFinalAuditJob(
    IServiceScopeFactory scopes,
    WritingFinalAuditStatus status,
    IHostEnvironment env,
    ILogger<WritingFinalAuditJob> log) : BackgroundService
{
    internal const string AdminId = "system:writing-final-audit";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!env.IsProduction()) return;
        try { await Task.Delay(TimeSpan.FromSeconds(75), ct); }
        catch (OperationCanceledException) { return; }

        var started = DateTimeOffset.UtcNow;
        status.Snapshot = status.Snapshot with { State = "running", StartedAt = started };
        try
        {
            await RunAsync(started, ct);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Writing final audit job failed at top level.");
            status.Snapshot = status.Snapshot with
            {
                State = "failed",
                FinishedAt = DateTimeOffset.UtcNow,
                Errors = [.. status.Snapshot.Errors, Short(ex.GetType().Name + ": " + ex.Message, 300)],
            };
        }
    }

    private async Task RunAsync(DateTimeOffset started, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<LearnerDbContext>();
        var clock = sp.GetRequiredService<TimeProvider>();
        var service = sp.GetRequiredService<IWritingTaskModelAnswerService>();
        var engine = sp.GetRequiredService<WritingRuleEngine>();
        var errors = new List<string>();
        var repairs = new List<WritingFinalAuditRepairOutcome>();

        try { repairs.Add(await FixTaskTextAsync(db, clock, ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("task-fix: " + Short(ex.Message, 200)); }

        foreach (var repair in WritingFinalAuditSpec.Repairs)
        {
            ct.ThrowIfCancellationRequested();
            try { repairs.Add(await RepairAsync(service, db, clock, repair, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                repairs.Add(new(repair.Id, repair.Label, "error", Short(ex.GetType().Name + ": " + ex.Message, 240)));
                db.ChangeTracker.Clear();
            }
        }

        var probes = new List<WritingFinalAuditProbe>();
        try { probes = await ProbePdfsAsync(sp, db, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("pdf-probes: " + Short(ex.Message, 200)); }

        WritingFinalAuditSelfCheck? selfCheck = null;
        try
        {
            var report = WritingValidatorSelfCheck.Run(engine, clock.GetUtcNow());
            selfCheck = new(
                report.Total,
                report.Passed,
                report.Failed,
                report.Cases.Where(c => !c.Ok).Select(c => Short(c.Group + ": " + c.Name + " -> " + c.Detail, 260)).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("self-check: " + Short(ex.Message, 200)); }

        WritingFinalAuditActive? active = null;
        try { active = await RevalidateAllAsync(service, engine, db, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add("revalidation: " + Short(ex.Message, 200)); }

        status.Snapshot = new WritingFinalAuditSnapshot(
            "done", started, DateTimeOffset.UtcNow, WritingRuleEngine.ValidatorVersion, selfCheck, active, repairs, probes, errors);
        log.LogInformation(
            "Writing final audit done: {Applied} repairs applied, self-check {Passed}/{Total}, {Conflicts} source conflicts.",
            repairs.Count(r => r.Outcome == "applied"), selfCheck?.Passed, selfCheck?.Total, active?.SourceConflicts.Count);
    }

    private static async Task<WritingFinalAuditRepairOutcome> FixTaskTextAsync(LearnerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var id = Guid.Parse(WritingFinalAuditSpec.TaskFixId);
        var scenario = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scenario?.TaskPromptMarkdown is null) return new(WritingFinalAuditSpec.TaskFixId, "task text OCR", "missing", null);
        if (!scenario.TaskPromptMarkdown.Contains("Nor=h Adelaide", StringComparison.Ordinal))
            return new(WritingFinalAuditSpec.TaskFixId, "task text OCR", "already-applied", null);

        scenario.TaskPromptMarkdown = scenario.TaskPromptMarkdown.Replace("Nor=h Adelaide", "North Adelaide", StringComparison.Ordinal);
        scenario.UpdatedAt = clock.GetUtcNow();
        WritingServiceHelpers.AddAuditEvent(db, clock, AdminId, "WritingScenario", id.ToString("D"),
            "writing.final-audit.task-text-fixed", "OCR typo in the recipient address: Nor=h Adelaide -> North Adelaide");
        await db.SaveChangesAsync(ct);
        return new(WritingFinalAuditSpec.TaskFixId, "task text OCR", "applied", null);
    }

    private static async Task<WritingFinalAuditRepairOutcome> RepairAsync(
        IWritingTaskModelAnswerService service,
        LearnerDbContext db,
        TimeProvider clock,
        WritingFinalAuditSpec.Repair repair,
        CancellationToken ct)
    {
        var id = Guid.Parse(repair.Id);
        var dto = await service.GetAsync(id, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.ModelAnswerText))
            return new(repair.Id, repair.Label, "no-answer", null);

        var current = dto.ModelAnswerText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var next = current;
        var present = 0;
        foreach (var (find, replace) in repair.Edits)
        {
            if (!next.Contains(find, StringComparison.Ordinal)) continue;
            present++;
            next = next.Replace(find, replace, StringComparison.Ordinal);
        }

        if (present == 0)
        {
            var done = repair.Edits.All(e => current.Contains(e.Replace, StringComparison.Ordinal));
            return new(repair.Id, repair.Label, done ? "already-applied" : "live-text-differs", null);
        }

        if (present != repair.Edits.Length)
            return new(repair.Id, repair.Label, "live-text-differs", present + "/" + repair.Edits.Length + " edits matched");
        if (dto.Status != "Ready" || !dto.IsCandidateVisible)
            return new(repair.Id, repair.Label, "not-active", dto.Status);

        var gate = await service.ValidateAsync(id, next, false, AdminId, ct);
        if (!gate.Passed)
        {
            var findings = string.Join(", ", gate.DeterministicFindings.Take(6)
                .Select(f => f.RuleId + " [" + Short(f.Quote ?? string.Empty, 40) + "]"));
            return new(repair.Id, repair.Label, "gate-failed",
                Short((gate.HoldReason ?? "hold") + "; words=" + gate.BodyWordCount + "; " + findings, 420));
        }

        WritingTaskModelAnswerDto? after;
        try
        {
            var imported = await service.ImportAsync(id, next, AdminId, false, ct);
            after = await service.ApproveAsync(id, AdminId, ct) ?? imported;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leave a live answer hidden: put the previous, previously-published text back.
            db.ChangeTracker.Clear();
            try
            {
                await service.ImportAsync(id, current, AdminId, false, ct);
                await service.ApproveAsync(id, AdminId, ct);
                return new(repair.Id, repair.Label, "rolled-back", Short(ex.GetType().Name + ": " + ex.Message, 240));
            }
            catch (Exception rollback) when (rollback is not OperationCanceledException)
            {
                return new(repair.Id, repair.Label, "rollback-failed", Short(ex.GetType().Name + " / " + rollback.GetType().Name, 240));
            }
        }

        WritingServiceHelpers.AddAuditEvent(db, clock, AdminId, "WritingTaskModelAnswer", id.ToString("D"),
            "writing.final-audit.repaired", Short(JsonSerializer.Serialize(new { previousText = current }), 7000));
        await db.SaveChangesAsync(ct);

        return new(repair.Id, repair.Label, after is { IsCandidateVisible: true } ? "applied" : "applied-not-visible", after?.VerificationStatus);
    }

    private static async Task<List<WritingFinalAuditProbe>> ProbePdfsAsync(IServiceProvider sp, LearnerDbContext db, CancellationToken ct)
    {
        var storage = sp.GetRequiredService<IFileStorage>();
        var extractor = new PdfPigPdfTextExtractor(sp.GetRequiredService<ILogger<PdfPigPdfTextExtractor>>());
        var result = new List<WritingFinalAuditProbe>();
        foreach (var probe in WritingFinalAuditSpec.PdfProbes)
        {
            ct.ThrowIfCancellationRequested();
            var id = Guid.Parse(probe.Id);
            var pdfId = await db.WritingScenarios.AsNoTracking().Where(s => s.Id == id).Select(s => s.StimulusPdfMediaAssetId).FirstOrDefaultAsync(ct);
            var asset = string.IsNullOrWhiteSpace(pdfId) ? null : await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pdfId, ct);
            if (asset is null || string.IsNullOrWhiteSpace(asset.StoragePath))
            {
                result.Add(new(probe.Id, probe.Label, "(pdf)", -1, "no stimulus PDF attached"));
                continue;
            }

            string text;
            try
            {
                await using var stream = await storage.OpenReadAsync(asset.StoragePath, ct);
                text = await extractor.ExtractAsync(stream, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(new(probe.Id, probe.Label, "(pdf)", -1, "read failed: " + Short(ex.GetType().Name, 60)));
                continue;
            }

            text = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length < 40)
            {
                result.Add(new(probe.Id, probe.Label, "(pdf)", -1, "no embedded text (scanned PDF)"));
                continue;
            }

            foreach (var token in probe.Tokens)
            {
                var count = 0;
                string? context = null;
                var at = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    count++;
                    context ??= text.Substring(Math.Max(0, at - 40), Math.Min(text.Length - Math.Max(0, at - 40), token.Length + 80));
                    at = text.IndexOf(token, at + token.Length, StringComparison.OrdinalIgnoreCase);
                }

                result.Add(new(probe.Id, probe.Label, token, count, context));
            }
        }

        return result;
    }

    private static async Task<WritingFinalAuditActive> RevalidateAllAsync(
        IWritingTaskModelAnswerService service,
        WritingRuleEngine engine,
        LearnerDbContext db,
        CancellationToken ct)
    {
        var visibleVerified = await db.WritingTaskModelAnswers.AsNoTracking().CountAsync(WritingTaskModelAnswerService.CandidateVisibleVerified, ct);
        var visibleAny = await db.WritingTaskModelAnswers.AsNoTracking()
            .CountAsync(a => a.Status == WritingAssessmentModelAnswerStatus.Ready && a.IsCandidateVisible, ct);

        var conflicts = new List<string>();
        var failures = new List<string>();
        var checkedCount = 0;
        var passed = 0;
        var failed = 0;
        var offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await service.RevalidateAsync(
                new WritingModelAnswerRevalidationRequest(false, false, null, offset, 250, false), AdminId, ct);
            if (page.Items.Count == 0) break;
            foreach (var item in page.Items)
            {
                checkedCount++;
                if (item.Passed) passed++; else failed++;
                if (!item.VisibleBefore) continue;
                var parsed = RulebookProfessionParser.TryParse(item.Profession, out var profession);
                foreach (var f in item.Findings)
                {
                    var check = f.RuleId.StartsWith("BUILTIN.", StringComparison.Ordinal)
                        ? f.RuleId["BUILTIN.".Length..]
                        : parsed ? engine.CheckIdForRule(profession, f.RuleId) : null;
                    if (WritingTaskModelAnswerService.IsSourceFidelityCheck(check))
                        conflicts.Add(item.ScenarioId.ToString("D") + ":" + check);
                }

                if (!item.Passed && failures.Count < 80)
                    failures.Add(item.ScenarioId.ToString("D") + ": " + string.Join(",", item.Findings.Take(4).Select(f => f.RuleId))
                        + (item.HoldReason is null ? string.Empty : " (" + item.HoldReason + ")"));
            }

            offset += page.Items.Count;
            if (offset >= page.TotalRows) break;
        }

        // Ready + visible but verified under another validator version: candidates do not see these (CandidateVisibleVerified).
        var staleRows = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => a.Status == WritingAssessmentModelAnswerStatus.Ready && a.IsCandidateVisible
                && a.ValidatorVersion != WritingRuleEngine.ValidatorVersion)
            .Select(a => new { a.ScenarioId, a.ValidatorVersion })
            .Take(20)
            .ToListAsync(ct);
        var stale = staleRows.Select(r => r.ScenarioId.ToString("D") + " v=" + (r.ValidatorVersion ?? "none")).ToList();

        return new WritingFinalAuditActive(visibleVerified, visibleAny, checkedCount, passed, failed, conflicts, failures, stale);
    }

    private static string Short(string value, int max)
        => value.Length <= max ? value : value[..max];
}
