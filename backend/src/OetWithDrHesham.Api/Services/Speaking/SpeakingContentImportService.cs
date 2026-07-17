using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// WS9 (SPK-007) — structured import of scanned / text source PDFs into a
/// reviewable Speaking role-play draft.
///
/// <para>Flow:</para>
/// <list type="number">
///   <item>Magic-byte validate the upload is a real PDF (trust-no-client).</item>
///   <item>Persist the source PDF through <see cref="IFileStorage"/> as a
///   provenance asset (mission-critical storage rule — never raw File.*).</item>
///   <item>Extract text via <see cref="IPdfTextExtractor"/> (PdfPig with the
///   configured OCR fallback for scanned pages).</item>
///   <item>Run a builder-validation pass that mirrors the publish gate: which
///   structured fields are present in the extracted text, which required ones
///   are missing (publish blockers).</item>
///   <item>When <c>autoDraft</c> is requested and usable text was extracted,
///   ground the existing AI-draft path with the source material to produce a
///   <c>Draft</c> card the admin reviews + edits before publishing.</item>
/// </list>
///
/// <para>Mock-safe: with no AI keys the gateway falls back to a deterministic
/// starter template (surfaced via <c>Warning</c>); with the no-op PDF
/// extractor (CI default) extraction returns empty text and the source asset
/// is still persisted for manual structuring. No path throws on a scanned
/// PDF.</para>
/// </summary>
public interface ISpeakingContentImportService
{
    Task<SpeakingContentImportResult> ImportAsync(
        IAiGatewayService gateway,
        string adminId,
        string adminName,
        string professionId,
        string? topic,
        bool autoDraft,
        string fileName,
        string declaredContentType,
        Stream pdf,
        CancellationToken ct);
}

public sealed class SpeakingContentImportService(
    LearnerDbContext db,
    IFileStorage storage,
    IPdfTextExtractor extractor,
    IUploadContentValidator validator,
    AdminService admin,
    ILogger<SpeakingContentImportService> logger) : ISpeakingContentImportService
{
    /// <summary>Below this many extracted characters we treat the PDF as a
    /// scanned/image-only page that produced no usable text.</summary>
    private const int ScannedTextThreshold = 60;

    public async Task<SpeakingContentImportResult> ImportAsync(
        IAiGatewayService gateway,
        string adminId,
        string adminName,
        string professionId,
        string? topic,
        bool autoDraft,
        string fileName,
        string declaredContentType,
        Stream pdf,
        CancellationToken ct)
    {
        if (gateway is null) throw new ArgumentNullException(nameof(gateway));
        if (string.IsNullOrWhiteSpace(professionId))
        {
            throw ApiException.Validation("SPEAKING_IMPORT_PROFESSION_REQUIRED",
                "Profession id is required.");
        }
        if (pdf is null)
        {
            throw ApiException.Validation("SPEAKING_IMPORT_FILE_REQUIRED",
                "A source PDF file is required.");
        }

        // Buffer to a seekable stream so we can validate, persist, and extract
        // from the same bytes without re-reading the request body.
        await using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
        {
            throw ApiException.Validation("SPEAKING_IMPORT_FILE_EMPTY",
                "The uploaded file is empty.");
        }
        buffer.Position = 0;

        // 1) Magic-byte validation — reject anything that is not a real PDF.
        var ext = (Path.GetExtension(fileName) ?? string.Empty).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext)) ext = "pdf";
        var validation = await validator.ValidateAsync(buffer, ext, ct);
        buffer.Position = 0;
        if (!validation.Accepted || !string.Equals(validation.DetectedExtension, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("SPEAKING_IMPORT_NOT_A_PDF",
                validation.Reason ?? "The uploaded file is not a valid PDF.");
        }

        // 2) Persist the source PDF (provenance) via IFileStorage.
        var safeName = SanitizeFileName(fileName);
        var key = $"speaking/imports/{DateTimeOffset.UtcNow:yyyy/MM}/{Guid.NewGuid():N}-{safeName}";
        var bytesWritten = await storage.WriteAsync(key, buffer, ct);
        buffer.Position = 0;

        // 2b) Register a viewable MediaAsset for the source PDF so the admin's
        // manual authoring screen can render it side-by-side (critical for
        // scanned cards that must be transcribed by hand). Best-effort and
        // content-deduped by SHA — a failure here never blocks the import.
        string? sourceMediaId = null;
        try
        {
            buffer.Position = 0;
            var (_, sourceSha) = await StreamingSha256.ComputeAsync(new[] { buffer }, null, ct);
            var existingMedia = await db.MediaAssets
                .FirstOrDefaultAsync(m => m.Sha256 == sourceSha && m.Format == "pdf", ct);
            if (existingMedia is not null)
            {
                sourceMediaId = existingMedia.Id;
            }
            else
            {
                sourceMediaId = $"med_{Guid.NewGuid():N}";
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = sourceMediaId,
                    OriginalFilename = safeName,
                    MimeType = "application/pdf",
                    Format = "pdf",
                    SizeBytes = bytesWritten,
                    StoragePath = key,
                    Status = MediaAssetStatus.Ready,
                    Sha256 = sourceSha,
                    MediaKind = "document",
                    UploadedBy = adminId,
                    UploadedAt = DateTimeOffset.UtcNow,
                    ProcessedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speaking import: failed to register source MediaAsset for {Key}", key);
            sourceMediaId = null;
        }
        buffer.Position = 0;

        // 3) Extract text (PdfPig + configured OCR fallback; no-op in CI).
        string extracted;
        try
        {
            extracted = await extractor.ExtractAsync(buffer, ct) ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Speaking import text extraction failed for {Key}", key);
            extracted = string.Empty;
        }

        var extractedChars = extracted.Length;
        var likelyScanned = extractedChars < ScannedTextThreshold;

        // 4) Builder-validation report.
        var report = BuildValidationReport(extracted, likelyScanned);

        // 5) Optional AI draft grounded in the source material.
        string? draftCardId = null;
        AdminRolePlayCardDetail? draft = null;
        string? warning = likelyScanned
            ? "The PDF produced little or no extractable text (likely a scanned image). Attach it as a source asset and structure the card manually, or provision an OCR provider."
            : null;

        if (autoDraft && !likelyScanned)
        {
            try
            {
                var response = await admin.AiDraftRolePlayCardAsync(
                    gateway,
                    adminId,
                    adminName,
                    new AdminRolePlayCardAiDraftRequest(
                        ProfessionId: professionId,
                        Topic: topic,
                        Emotion: null,
                        Difficulty: null,
                        Setting: null,
                        CandidateRole: null,
                        InterlocutorRole: null,
                        CommunicationGoal: null,
                        SourceMaterial: extracted),
                    ct);
                draftCardId = response.CardId;
                draft = response.Card;
                warning = response.Warning;
            }
            catch (ApiException)
            {
                // Originality collisions / validation surface to the caller as-is.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Speaking import auto-draft failed for {Key}", key);
                warning = "Source text was extracted but the AI draft step failed. The source asset was saved; structure the card manually.";
            }
        }

        logger.LogInformation(
            "Speaking import: admin {AdminId} imported {Key} ({Bytes} bytes, {Chars} chars, scanned={Scanned}, draft={DraftId})",
            adminId, key, bytesWritten, extractedChars, likelyScanned, draftCardId ?? "(none)");

        return new SpeakingContentImportResult(
            SourceAssetKey: key,
            SourceBytes: bytesWritten,
            ExtractedChars: extractedChars,
            LikelyScanned: likelyScanned,
            Validation: report,
            DraftCardId: draftCardId,
            Draft: draft,
            Warning: warning,
            SourceMediaId: sourceMediaId);
    }

    /// <summary>
    /// Mirror the publish-gate intent: detect whether the extracted text
    /// carries each structured field. Required fields that are missing become
    /// publish blockers; advisory fields are warnings only.
    /// </summary>
    private static SpeakingImportValidationReport BuildValidationReport(string text, bool likelyScanned)
    {
        var checks = new List<SpeakingImportFieldCheck>();
        var blockers = new List<string>();

        void Add(string field, bool required, bool detected, string? note)
        {
            checks.Add(new SpeakingImportFieldCheck(field, detected, required, note));
            if (required && !detected)
            {
                blockers.Add(field);
            }
        }

        bool Has(params string[] markers) =>
            !likelyScanned && markers.Any(m => Regex.IsMatch(text, Regex.Escape(m), RegexOptions.IgnoreCase));

        Add("scenarioTitle", required: true, detected: !likelyScanned && text.TrimStart().Length > 0,
            note: likelyScanned ? "No extractable text." : null);
        Add("setting", required: true, detected: Has("setting", "clinic", "hospital", "ward", " that you work", "you are"),
            note: null);
        Add("candidateTasks", required: true, detected: Has("task", "you should", "you must", "find out", "explain", "advise", "reassure"),
            note: null);
        Add("patientBackground", required: true, detected: Has("patient", "background", "you are", "years old", "presenting", "complain"),
            note: null);
        Add("interlocutorScript", required: false, detected: Has("interlocutor", "role player", "role-player", "patient says", "responds", "cue"),
            note: "Advisory — the hidden interlocutor script is usually authored separately.");

        var isPublishable = blockers.Count == 0;
        return new SpeakingImportValidationReport(isPublishable, checks, blockers);
    }

    private static string SanitizeFileName(string name)
    {
        var baseName = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(baseName)) return "source.pdf";
        var cleaned = Regex.Replace(baseName, "[^A-Za-z0-9._-]", "-");
        return cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }
}
