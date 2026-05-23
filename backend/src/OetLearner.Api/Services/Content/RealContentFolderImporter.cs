using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Parses an uploaded ZIP that mirrors the user's `Project Real Content/`
/// folder layout and emits one proposal per target row (ContentPaper,
/// ResultTemplateAsset, SpeakingSharedResource, ScoringPolicy,
/// RulebookVersion reference-PDF attachment).
///
/// The importer is intentionally tolerant of the casual folder names in the
/// source ("Card 1 ( Already known Pt )", "Writing 5 ( Update & Referral - From Specialist to GP or Dentist )",
/// etc.) and uses regex heuristics. The admin reviews + edits proposals
/// before commit; nothing is auto-created without an explicit Commit call.
///
/// Returns drafts only; nothing is auto-published.
/// </summary>
public sealed class RealContentFolderImporter
{
    private const long MaxEntryBytes = 200L * 1024 * 1024;
    private const long MaxTextEntryBytes = 2L * 1024 * 1024;
    private const long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntryCount = 2_000;
    private readonly LearnerDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IUploadContentValidator _validator;
    private readonly IUploadScanner _scanner;
    private readonly StorageOptions _opts;
    private readonly ILogger<RealContentFolderImporter> _log;

    public RealContentFolderImporter(
        LearnerDbContext db,
        IFileStorage storage,
        IUploadContentValidator validator,
        IUploadScanner scanner,
        IOptions<StorageOptions> opts,
        ILogger<RealContentFolderImporter> log)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _scanner = scanner;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<RealContentImportStageResult> StageAsync(string adminId, Stream zipStream, string filename, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var stagingPrefix = $"staging/real-content-import/{adminId}/{sessionId}";
        var proposals = new List<RealContentProposal>();
        var issues = new List<string>();

        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        // First pass: catalog every file by normalized path
        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && e.Length > 0)
            .Select(e => new { Entry = e, NormPath = NormalizePath(e.FullName) })
            .ToList();

        if (entries.Count > MaxEntryCount)
        {
            throw new InvalidOperationException($"ZIP contains too many files (max {MaxEntryCount}).");
        }

        var totalUncompressedBytes = entries.Sum(e => e.Entry.Length);
        if (totalUncompressedBytes > MaxUncompressedBytes)
        {
            throw new InvalidOperationException($"ZIP uncompressed size is too large (max {MaxUncompressedBytes} bytes).");
        }

        if (entries.Any(e => e.Entry.Length > MaxEntryBytes))
        {
            throw new InvalidOperationException($"ZIP contains a file larger than {MaxEntryBytes} bytes.");
        }

        // Group by top-level segment
        var groups = entries
            .GroupBy(e =>
            {
                var slash = e.NormPath.IndexOf('/');
                return slash < 0 ? string.Empty : e.NormPath[..slash];
            })
            .ToList();

        foreach (var group in groups)
        {
            var topName = group.Key.ToLowerInvariant();
            try
            {
                if (topName.StartsWith("listening"))
                {
                    ParseListeningGroup(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
                else if (topName.StartsWith("reading"))
                {
                    ParseReadingGroup(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
                else if (topName.StartsWith("writing"))
                {
                    ParseWritingGroup(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
                else if (topName.StartsWith("speaking"))
                {
                    ParseSpeakingGroup(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
                else if (topName.Contains("table format") || topName.Contains("result"))
                {
                    ParseResultTablesGroup(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
                else if (group.Key == "" && group.Any())
                {
                    // Top-level loose files (Scoring System.txt, top-level rulebook PDFs)
                    ParseRootFiles(group.Select(g => g.Entry).ToList(), proposals, issues);
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Failed parsing {group.Key}: {ex.Message}");
                _log.LogWarning(ex, "RealContent importer: parse error for {Top}", group.Key);
            }
        }

        // Stage every source file under a session-scoped key. Paper proposals
        // carry multiple asset files; each asset needs its own staged key for
        // the commit step's validation/hash/content-address flow.
        var entryByPath = entries.ToDictionary(e => e.NormPath, e => e.Entry, StringComparer.OrdinalIgnoreCase);
        var stagedByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = proposals
            .Select(p => p.SourcePath)
            .Concat(proposals.SelectMany(p => p.Assets.Select(a => a.SourcePath)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sourcePath in sourcePaths)
        {
            if (!entryByPath.TryGetValue(sourcePath, out var entry))
            {
                issues.Add($"Missing source entry for proposal: {sourcePath}");
                continue;
            }

            var key = $"{stagingPrefix}/{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
            var entryValidationError = await ValidateEntryBeforeStagingAsync(entry, sourcePath, ct);
            if (entryValidationError is not null)
            {
                issues.Add(entryValidationError);
                continue;
            }

            await using (var entryStream = entry.Open())
            await using (var destStream = await _storage.OpenWriteAsync(key, ct))
            {
                await entryStream.CopyToAsync(destStream, ct);
            }
            stagedByPath[sourcePath] = key;
        }

        foreach (var p in proposals)
        {
            if (stagedByPath.TryGetValue(p.SourcePath, out var key))
            {
                p.StagedStorageKey = key;
            }

            foreach (var asset in p.Assets)
            {
                if (stagedByPath.TryGetValue(asset.SourcePath, out var assetKey))
                {
                    asset.StagedStorageKey = assetKey;
                }
            }
        }

        return new RealContentImportStageResult
        {
            SessionId = sessionId,
            UploadedFilename = filename,
            StagedAt = DateTimeOffset.UtcNow,
            Proposals = proposals,
            Issues = issues,
        };
    }

    // ─── Parsers ────────────────────────────────────────────────────────────

    private static readonly Regex ListeningSampleRx = new(@"listening sample\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReadingSampleRx = new(@"reading sample\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WritingNRx = new(@"writing\s*(\d+)\s*\(?\s*([^)]*)\)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CardNRx = new(@"card\s*(\d+)\s*\(?\s*([^)]*)\)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void ParseListeningGroup(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        var bySample = new Dictionary<int, List<ZipArchiveEntry>>();
        foreach (var e in entries)
        {
            var m = ListeningSampleRx.Match(e.FullName);
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[1].Value);
            if (!bySample.ContainsKey(n)) bySample[n] = new List<ZipArchiveEntry>();
            bySample[n].Add(e);
        }
        foreach (var (n, files) in bySample.OrderBy(kv => kv.Key))
        {
            var p = new RealContentProposal
            {
                Target = RealContentTarget.ListeningPaper,
                Title = $"Listening Sample {n}",
                Subtest = "listening",
                AppliesToAllProfessions = true,
                Slug = $"listening-sample-{n}",
                SourcePath = NormalizePath(files.First().FullName),
            };
            foreach (var f in files)
            {
                var nm = Path.GetFileName(f.FullName).ToLowerInvariant();
                var role = ClassifyListeningAsset(nm);
                if (role is null) { issues.Add($"Unclassified listening file: {f.FullName}"); continue; }
                p.Assets.Add(new RealContentAssetProposal
                {
                    Role = role,
                    SourcePath = NormalizePath(f.FullName),
                    OriginalFilename = Path.GetFileName(f.FullName),
                });
            }
            proposals.Add(p);
        }
    }

    private static string? ClassifyListeningAsset(string lowerName)
    {
        if (lowerName.EndsWith(".mp3") || lowerName.Contains("audio") && (lowerName.EndsWith(".mp3") || lowerName.EndsWith(".wav") || lowerName.EndsWith(".ogg")))
            return "Audio";
        if (lowerName.Contains("question") && lowerName.EndsWith(".pdf")) return "QuestionPaper";
        if (lowerName.Contains("script") && lowerName.EndsWith(".pdf")) return "AudioScript";
        if (lowerName.Contains("answer") && lowerName.EndsWith(".pdf")) return "AnswerKey";
        if (lowerName.EndsWith(".pdf")) return "Supplementary";
        if (lowerName.EndsWith(".mp3") || lowerName.EndsWith(".wav") || lowerName.EndsWith(".ogg")) return "Audio";
        return null;
    }

    private static void ParseReadingGroup(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        var bySample = new Dictionary<int, List<ZipArchiveEntry>>();
        foreach (var e in entries)
        {
            var m = ReadingSampleRx.Match(e.FullName);
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[1].Value);
            if (!bySample.ContainsKey(n)) bySample[n] = new List<ZipArchiveEntry>();
            bySample[n].Add(e);
        }
        foreach (var (n, files) in bySample.OrderBy(kv => kv.Key))
        {
            var p = new RealContentProposal
            {
                Target = RealContentTarget.ReadingPaper,
                Title = $"Reading Sample {n}",
                Subtest = "reading",
                AppliesToAllProfessions = true,
                Slug = $"reading-sample-{n}",
                SourcePath = NormalizePath(files.First().FullName),
            };
            foreach (var f in files)
            {
                var nm = Path.GetFileName(f.FullName).ToLowerInvariant();
                var role = nm.Contains("part a") ? "QuestionPaper" : nm.Contains("part b") || nm.Contains("b&c") ? "QuestionPaper" : "Supplementary";
                p.Assets.Add(new RealContentAssetProposal
                {
                    Role = role,
                    SourcePath = NormalizePath(f.FullName),
                    OriginalFilename = Path.GetFileName(f.FullName),
                    Part = nm.Contains("part a") ? "A" : nm.Contains("part b") || nm.Contains("b&c") ? "B" : null,
                });
            }
            proposals.Add(p);
        }
    }

    private static readonly Dictionary<string, string> WritingLetterTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "routine", "routine_referral" },
        { "urgent", "urgent_referral" },
        { "non medical", "non_medical_referral" },
        { "non-medical", "non_medical_referral" },
        { "update & discharge", "update_discharge" },
        { "discharge", "update_discharge" },
        { "update & referral", "update_referral_specialist_to_gp" },
        { "specialist", "update_referral_specialist_to_gp" },
    };

    private static void ParseWritingGroup(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        // Group by writing folder
        var byFolder = entries.GroupBy(e => SecondSegment(e.FullName) ?? "").ToList();
        foreach (var folderGroup in byFolder)
        {
            var folder = folderGroup.Key.ToLowerInvariant();
            if (folder.Contains("rulebook"))
            {
                // Writing rulebook PDF
                var pdf = folderGroup.FirstOrDefault(e => e.FullName.ToLowerInvariant().EndsWith(".pdf"));
                if (pdf is not null)
                {
                    proposals.Add(new RealContentProposal
                    {
                        Target = RealContentTarget.RulebookReferencePdf,
                        Title = "Writing Rulebook (Medicine)",
                        RulebookKind = "writing",
                        RulebookProfession = "medicine",
                        SourcePath = NormalizePath(pdf.FullName),
                    });
                }
                continue;
            }
            var m = WritingNRx.Match(folder);
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[1].Value);
            var hint = m.Groups[2].Value.Trim().ToLowerInvariant();
            var letterType = WritingLetterTypeMap
                .FirstOrDefault(kv => hint.Contains(kv.Key)).Value ?? "routine_referral";
            var p = new RealContentProposal
            {
                Target = RealContentTarget.WritingPaper,
                Title = $"Writing {n} ({hint})",
                Subtest = "writing",
                LetterType = letterType,
                ProfessionId = "medicine",
                Slug = $"writing-{n}-{letterType}",
                SourcePath = NormalizePath(folderGroup.First().FullName),
            };
            foreach (var f in folderGroup)
            {
                var nm = Path.GetFileName(f.FullName).ToLowerInvariant();
                if (!nm.EndsWith(".pdf")) continue;
                var role = nm.Contains("case notes") || nm.Contains("case-notes") ? "CaseNotes"
                    : nm.Contains("answer sheet") || nm.Contains("answer-sheet") || nm.Contains("model") ? "ModelAnswer"
                    : "Supplementary";
                p.Assets.Add(new RealContentAssetProposal
                {
                    Role = role,
                    SourcePath = NormalizePath(f.FullName),
                    OriginalFilename = Path.GetFileName(f.FullName),
                });
            }
            proposals.Add(p);
        }
    }

    private static readonly Dictionary<string, string> SpeakingCardTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "already known", "already_known_pt" },
        { "follow up", "follow_up_visit" },
        { "follow-up", "follow_up_visit" },
        { "examination", "examination" },
        { "first visit - emergency", "first_visit_emergency" },
        { "emergency", "first_visit_emergency" },
        { "first visit", "first_visit_routine" },
    };

    private static void ParseSpeakingGroup(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        // Top-level Speaking files: warm-up + assessment criteria + rulebook
        foreach (var e in entries)
        {
            var path = NormalizePath(e.FullName);
            var nm = Path.GetFileName(e.FullName).ToLowerInvariant();
            var depth = path.Split('/').Length;
            if (depth <= 2 && nm.EndsWith(".pdf"))
            {
                if (nm.Contains("intro") || nm.Contains("warm"))
                {
                    proposals.Add(new RealContentProposal
                    {
                        Target = RealContentTarget.SpeakingSharedResource,
                        Title = "Speaking Warm-Up Questions",
                        SharedResourceKind = "WarmUpQuestions",
                        SourcePath = path,
                    });
                }
                else if (nm.Contains("assessment criteria"))
                {
                    proposals.Add(new RealContentProposal
                    {
                        Target = RealContentTarget.SpeakingSharedResource,
                        Title = "Speaking Assessment Criteria",
                        SharedResourceKind = "AssessmentCriteria",
                        SourcePath = path,
                    });
                }
            }
        }
        // Card folders
        var byFolder = entries.GroupBy(e => SecondSegment(e.FullName) ?? "").ToList();
        foreach (var folderGroup in byFolder)
        {
            var folder = folderGroup.Key.ToLowerInvariant();
            if (folder.Contains("rulebook"))
            {
                var pdf = folderGroup.FirstOrDefault(e => e.FullName.ToLowerInvariant().EndsWith(".pdf"));
                if (pdf is not null)
                {
                    proposals.Add(new RealContentProposal
                    {
                        Target = RealContentTarget.RulebookReferencePdf,
                        Title = "Speaking Rulebook (Medicine)",
                        RulebookKind = "speaking",
                        RulebookProfession = "medicine",
                        SourcePath = NormalizePath(pdf.FullName),
                    });
                }
                continue;
            }
            var m = CardNRx.Match(folder);
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[1].Value);
            var hint = m.Groups[2].Value.Trim().ToLowerInvariant();
            var cardType = SpeakingCardTypeMap
                .FirstOrDefault(kv => hint.Contains(kv.Key)).Value ?? "first_visit_routine";
            var p = new RealContentProposal
            {
                Target = RealContentTarget.SpeakingPaper,
                Title = $"Speaking Card {n} ({hint})",
                Subtest = "speaking",
                CardType = cardType,
                ProfessionId = "medicine",
                Slug = $"speaking-card-{n}-{cardType}",
                SourcePath = NormalizePath(folderGroup.First().FullName),
            };
            foreach (var f in folderGroup)
            {
                var nm = Path.GetFileName(f.FullName).ToLowerInvariant();
                if (!nm.EndsWith(".pdf")) continue;
                p.Assets.Add(new RealContentAssetProposal
                {
                    Role = "RoleCard",
                    SourcePath = NormalizePath(f.FullName),
                    OriginalFilename = Path.GetFileName(f.FullName),
                });
            }
            if (p.Assets.Count > 0) proposals.Add(p);
        }
    }

    private static void ParseResultTablesGroup(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        var seq = 1;
        foreach (var e in entries.Where(x =>
        {
            var s = x.FullName.ToLowerInvariant();
            return s.EndsWith(".jpg") || s.EndsWith(".jpeg") || s.EndsWith(".png") || s.EndsWith(".webp");
        }))
        {
            var nm = Path.GetFileNameWithoutExtension(e.FullName);
            proposals.Add(new RealContentProposal
            {
                Target = RealContentTarget.ResultTemplate,
                Title = $"Result template {seq:00}",
                TemplateKey = $"real-content-{seq:00}-{Slugify(nm)}",
                SourcePath = NormalizePath(e.FullName),
                SortOrder = seq,
            });
            seq++;
        }
    }

    private static void ParseRootFiles(IReadOnlyList<ZipArchiveEntry> entries, List<RealContentProposal> proposals, List<string> issues)
    {
        foreach (var e in entries)
        {
            var nm = Path.GetFileName(e.FullName).ToLowerInvariant();
            if (nm == "scoring system.txt")
            {
                proposals.Add(new RealContentProposal
                {
                    Target = RealContentTarget.ScoringPolicyBody,
                    Title = "Scoring System (markdown)",
                    SourcePath = NormalizePath(e.FullName),
                });
            }
            else if (nm.Contains("writing") && nm.Contains("rulebook") && nm.EndsWith(".pdf"))
            {
                proposals.Add(new RealContentProposal
                {
                    Target = RealContentTarget.RulebookReferencePdf,
                    Title = "Writing Rulebook (Medicine)",
                    RulebookKind = "writing",
                    RulebookProfession = "medicine",
                    SourcePath = NormalizePath(e.FullName),
                });
            }
            else if (nm.Contains("speaking") && nm.Contains("rulebook") && nm.EndsWith(".pdf"))
            {
                proposals.Add(new RealContentProposal
                {
                    Target = RealContentTarget.RulebookReferencePdf,
                    Title = "Speaking Rulebook (Medicine)",
                    RulebookKind = "speaking",
                    RulebookProfession = "medicine",
                    SourcePath = NormalizePath(e.FullName),
                });
            }
        }
    }

    private static string NormalizePath(string p)
    {
        var normalized = p.Replace('\\', '/').Trim().TrimStart('/');
        var slash = normalized.IndexOf('/');
        if (slash <= 0) return normalized;

        var firstSegment = normalized[..slash];
        var compact = Regex.Replace(firstSegment, "[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();
        return compact is "projectrealcontent" ? normalized[(slash + 1)..] : normalized;
    }

    private async Task<string?> ValidateEntryBeforeStagingAsync(ZipArchiveEntry entry, string sourcePath, CancellationToken ct)
    {
        var ext = (Path.GetExtension(sourcePath)?.TrimStart('.') ?? string.Empty).ToLowerInvariant();
        var fileName = Path.GetFileName(sourcePath);

        if (ext == "txt")
        {
            if (entry.Length > MaxTextEntryBytes)
            {
                return $"Text file too large: {sourcePath}";
            }

            try
            {
                await using var textStream = entry.Open();
                using var reader = new StreamReader(
                    textStream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                    detectEncodingFromByteOrderMarks: true);
                _ = await reader.ReadToEndAsync(ct);
            }
            catch (DecoderFallbackException)
            {
                return $"Text file is not valid UTF-8: {sourcePath}";
            }

            await using var scanText = entry.Open();
            var textScan = await _scanner.ScanAsync(scanText, fileName, ct);
            return textScan.clean ? null : $"{sourcePath}: {textScan.reason ?? "file failed security scanning"}";
        }

        await using (var validationStream = entry.Open())
        {
            var validation = await _validator.ValidateAsync(validationStream, ext, ct);
            if (!validation.Accepted)
            {
                return $"{sourcePath}: {validation.Reason ?? $"invalid .{ext} file content"}";
            }
        }

        await using var scanStream = entry.Open();
        var scan = await _scanner.ScanAsync(scanStream, fileName, ct);
        return scan.clean ? null : $"{sourcePath}: {scan.reason ?? "file failed security scanning"}";
    }

    private static string? SecondSegment(string p)
    {
        var parts = NormalizePath(p).Split('/');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    // ─── Commit ─────────────────────────────────────────────────────────────

    public async Task<RealContentCommitResult> CommitAsync(
        string adminId,
        IReadOnlyList<RealContentProposal> approved,
        bool canPublishContent,
        CancellationToken ct)
    {
        var created = new List<RealContentCreatedRow>();
        var errors = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var p in approved)
        {
            try
            {
                switch (p.Target)
                {
                    case RealContentTarget.ListeningPaper:
                    case RealContentTarget.ReadingPaper:
                    case RealContentTarget.WritingPaper:
                    case RealContentTarget.SpeakingPaper:
                        await CommitPaperAsync(adminId, p, now, created, errors, ct);
                        break;
                    case RealContentTarget.ResultTemplate:
                        await CommitResultTemplateAsync(adminId, p, now, created, errors, ct);
                        break;
                    case RealContentTarget.SpeakingSharedResource:
                        await CommitSpeakingSharedAsync(adminId, p, now, created, errors, ct);
                        break;
                    case RealContentTarget.RulebookReferencePdf:
                        await CommitRulebookPdfAsync(adminId, p, now, created, errors, ct);
                        break;
                    case RealContentTarget.ScoringPolicyBody:
                        await CommitScoringPolicyAsync(adminId, p, now, created, errors, ct);
                        break;
                    default:
                        errors.Add($"Unknown target {p.Target}");
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{p.Target} '{p.Title}': {ex.Message}");
                _log.LogWarning(ex, "RealContent commit error for {Target}: {Title}", p.Target, p.Title);
            }
        }

        await _db.SaveChangesAsync(ct);
        return new RealContentCommitResult { Created = created, Errors = errors };
    }

    private async Task<(string mediaId, long bytes, string sha)> EnsureMediaAssetAsync(string adminId, RealContentProposal p, string sourceKey, CancellationToken ct)
    {
        var ext = (Path.GetExtension(p.SourcePath)?.TrimStart('.') ?? "").ToLowerInvariant();
        // Load the staged bytes into an in-memory buffer and immediately dispose the
        // source stream so the underlying file is no longer locked when we later call
        // _storage.Move (on Windows, Move requires no open handles on the source file).
        await using var buffer = new MemoryStream();
        {
            await using var staged = await _storage.OpenReadAsync(sourceKey, ct);
            await staged.CopyToAsync(buffer, ct);
        }
        buffer.Position = 0;

        var validation = await _validator.ValidateAsync(buffer, ext, ct);
        if (!validation.Accepted)
        {
            throw new InvalidOperationException(validation.Reason ?? $"Invalid imported .{ext} file content.");
        }

        buffer.Position = 0;
        var scan = await _scanner.ScanAsync(buffer, Path.GetFileName(p.SourcePath), ct);
        if (!scan.clean)
        {
            throw new InvalidOperationException(scan.reason ?? "Imported file failed security scanning.");
        }

        // Re-hash the staged file after validation/scanning.
        long bytes;
        string sha;
        buffer.Position = 0;
        (bytes, sha) = await StreamingSha256.ComputeAsync(new[] { buffer }, null, ct);
        var publishedKey = ContentAddressed.PublishedKey(_opts.ContentUpload.PublishedSubpath, sha, ext);
        if (!_storage.Exists(publishedKey))
            _storage.Move(sourceKey, publishedKey, overwrite: false);
        else
            _storage.Delete(sourceKey);

        var existing = await _db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha && m.Format == ext, ct);
        if (existing is not null) return (existing.Id, bytes, sha);

        var mediaId = $"med_{Guid.NewGuid():N}";
        _db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = Path.GetFileName(p.SourcePath),
            MimeType = ext == "mp3" ? "audio/mpeg"
                : ext == "wav" ? "audio/wav"
                : ext == "ogg" ? "audio/ogg"
                : ext == "pdf" ? "application/pdf"
                : ext == "png" ? "image/png"
                : ext == "webp" ? "image/webp"
                : "image/jpeg",
            Format = ext,
            SizeBytes = bytes,
            StoragePath = publishedKey,
            Status = MediaAssetStatus.Ready,
            Sha256 = sha,
            MediaKind = ext is "mp3" or "wav" or "ogg" ? "audio" : ext is "pdf" or "txt" ? "document" : "image",
            UploadedBy = adminId,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        return (mediaId, bytes, sha);
    }

    private async Task CommitPaperAsync(string adminId, RealContentProposal p, DateTimeOffset now,
        List<RealContentCreatedRow> created, List<string> errors, CancellationToken ct)
    {
        var paperId = Guid.NewGuid().ToString("N");
        var paper = new ContentPaper
        {
            Id = paperId,
            SubtestCode = p.Subtest ?? throw new InvalidOperationException("subtest required"),
            Title = p.Title,
            Slug = $"{p.Slug ?? p.Subtest}-{paperId[..8]}",
            ProfessionId = p.ProfessionId,
            AppliesToAllProfessions = p.AppliesToAllProfessions,
            Difficulty = "standard",
            EstimatedDurationMinutes = 0,
            Status = ContentStatus.Draft,
            CardType = p.CardType,
            LetterType = p.LetterType,
            Priority = 0,
            TagsCsv = "real-content-import",
            SourceProvenance = "Project Real Content folder",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Set<ContentPaper>().Add(paper);
        AddAuditEvent(adminId, "RealContentPaperImported", "ContentPaper", paperId, $"subtest={paper.SubtestCode};title={paper.Title}");

        var order = 0;
        foreach (var a in p.Assets)
        {
            if (a.StagedStorageKey is null) { errors.Add($"Asset not staged: {a.SourcePath}"); continue; }
            var (mediaId, _, _) = await EnsureMediaAssetAsync(adminId, new RealContentProposal { SourcePath = a.SourcePath }, a.StagedStorageKey, ct);
            _db.Set<ContentPaperAsset>().Add(new ContentPaperAsset
            {
                Id = Guid.NewGuid().ToString("N"),
                PaperId = paperId,
                Role = Enum.Parse<PaperAssetRole>(a.Role),
                Part = a.Part,
                MediaAssetId = mediaId,
                Title = a.OriginalFilename ?? a.Role,
                DisplayOrder = order++,
                IsPrimary = order == 1,
                CreatedAt = now,
            });
        }
        created.Add(new RealContentCreatedRow { Target = p.Target, Id = paperId, Title = paper.Title });
    }

    private async Task CommitResultTemplateAsync(string adminId, RealContentProposal p, DateTimeOffset now,
        List<RealContentCreatedRow> created, List<string> errors, CancellationToken ct)
    {
        if (p.StagedStorageKey is null) { errors.Add($"Template not staged: {p.SourcePath}"); return; }
        var (mediaId, _, _) = await EnsureMediaAssetAsync(adminId, p, p.StagedStorageKey, ct);
        var id = $"rtpl_{Guid.NewGuid():N}";
        var key = (p.TemplateKey ?? id);
        // ensure uniqueness
        var exists = await _db.Set<ResultTemplateAsset>().AnyAsync(x => x.TemplateKey == key, ct);
        if (exists) key = $"{key}-{id[..6]}";
        _db.Set<ResultTemplateAsset>().Add(new ResultTemplateAsset
        {
            Id = id,
            TemplateKey = key,
            Title = p.Title,
            MediaAssetId = mediaId,
            IsActive = false,
            SortOrder = p.SortOrder ?? 0,
            UploadedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent(adminId, "RealContentResultTemplateImported", "ResultTemplateAsset", id, key);
        created.Add(new RealContentCreatedRow { Target = p.Target, Id = id, Title = p.Title });
    }

    private async Task CommitSpeakingSharedAsync(string adminId, RealContentProposal p, DateTimeOffset now,
        List<RealContentCreatedRow> created, List<string> errors, CancellationToken ct)
    {
        if (p.StagedStorageKey is null) { errors.Add($"Speaking shared not staged: {p.SourcePath}"); return; }
        var (mediaId, _, _) = await EnsureMediaAssetAsync(adminId, p, p.StagedStorageKey, ct);
        var id = $"sss_{Guid.NewGuid():N}";
        _db.Set<SpeakingSharedResource>().Add(new SpeakingSharedResource
        {
            Id = id,
            Kind = p.SharedResourceKind ?? SpeakingSharedResourceKinds.WarmUpQuestions,
            Title = p.Title,
            ProfessionId = p.ProfessionId,
            MediaAssetId = mediaId,
            Status = ContentStatus.Draft,
            UploadedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent(adminId, "RealContentSpeakingSharedResourceImported", "SpeakingSharedResource", id, p.Title);
        created.Add(new RealContentCreatedRow { Target = p.Target, Id = id, Title = p.Title });
    }

    private async Task CommitRulebookPdfAsync(string adminId, RealContentProposal p, DateTimeOffset now,
        List<RealContentCreatedRow> created, List<string> errors, CancellationToken ct)
    {
        if (p.StagedStorageKey is null) { errors.Add($"Rulebook PDF not staged: {p.SourcePath}"); return; }
        if (string.IsNullOrEmpty(p.RulebookKind) || string.IsNullOrEmpty(p.RulebookProfession))
        {
            errors.Add($"Rulebook PDF missing kind/profession: {p.SourcePath}");
            return;
        }
        var rb = await _db.Set<RulebookVersion>()
            .Include(x => x.Sections)
            .Include(x => x.Rules)
            .Where(x => x.Kind == p.RulebookKind && x.Profession == p.RulebookProfession && x.Status == RulebookStatus.Published)
            .OrderByDescending(x => x.PublishedAt)
            .FirstOrDefaultAsync(ct);
        if (rb is null)
        {
            errors.Add($"No published rulebook found for {p.RulebookKind}/{p.RulebookProfession}");
            return;
        }
        var (mediaId, _, _) = await EnsureMediaAssetAsync(adminId, p, p.StagedStorageKey, ct);
        var draftId = $"rb_{p.RulebookKind}_{p.RulebookProfession}_{Guid.NewGuid():N}";
        var draft = new RulebookVersion
        {
            Id = draftId,
            Kind = rb.Kind,
            Profession = rb.Profession,
            Version = $"{rb.Version}-pdf-draft-{now:yyyyMMddHHmmss}",
            Status = RulebookStatus.Draft,
            AuthoritySource = rb.AuthoritySource,
            ReferencePdfAssetId = mediaId,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = adminId,
            Sections = rb.Sections.Select(section => new RulebookSectionRow
            {
                Id = Guid.NewGuid().ToString("N"),
                RulebookVersionId = draftId,
                Code = section.Code,
                Title = section.Title,
                OrderIndex = section.OrderIndex,
            }).ToList(),
            Rules = rb.Rules.Select(rule => new RulebookRuleRow
            {
                Id = Guid.NewGuid().ToString("N"),
                RulebookVersionId = draftId,
                Code = rule.Code,
                SectionCode = rule.SectionCode,
                Title = rule.Title,
                Body = rule.Body,
                Severity = rule.Severity,
                AppliesToJson = rule.AppliesToJson,
                TurnStage = rule.TurnStage,
                ExemplarPhrasesJson = rule.ExemplarPhrasesJson,
                ForbiddenPatternsJson = rule.ForbiddenPatternsJson,
                CheckId = rule.CheckId,
                ParamsJson = rule.ParamsJson,
                ExamplesJson = rule.ExamplesJson,
                OrderIndex = rule.OrderIndex,
            }).ToList(),
        };
        _db.Set<RulebookVersion>().Add(draft);
        AddAuditEvent(adminId, "RealContentRulebookReferencePdfDraftImported", "RulebookVersion", draft.Id, $"source={rb.Id};media={mediaId}");
        created.Add(new RealContentCreatedRow { Target = p.Target, Id = draft.Id, Title = $"Draft PDF rulebook for {p.RulebookKind}/{p.RulebookProfession}" });
    }

    private async Task CommitScoringPolicyAsync(string adminId, RealContentProposal p, DateTimeOffset now,
        List<RealContentCreatedRow> created, List<string> errors, CancellationToken ct)
    {
        if (p.StagedStorageKey is null) { errors.Add($"Scoring policy not staged: {p.SourcePath}"); return; }
        await using var stream = await _storage.OpenReadAsync(p.StagedStorageKey, ct);
        using var reader = new StreamReader(stream);
        var bodyMarkdown = await reader.ReadToEndAsync(ct);

        var id = $"scr_{Guid.NewGuid():N}";
        var policyJson = ScoringPolicyValidation.CanonicalDefaultPolicyJson;
        var validationError = ScoringPolicyValidation.ValidateCanonicalPolicyJson(policyJson);
        if (validationError is not null)
        {
            errors.Add(validationError);
            return;
        }
        _db.Set<ScoringPolicy>().Add(new ScoringPolicy
        {
            Id = id,
            BodyMarkdown = bodyMarkdown,
            PolicyJson = policyJson,
            IsActive = false,
            UpdatedByUserId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AddAuditEvent(adminId, "RealContentScoringPolicyDraftImported", "ScoringPolicy", id, "Inactive scoring policy draft imported from Project Real Content folder");
        created.Add(new RealContentCreatedRow { Target = p.Target, Id = id, Title = "Draft scoring policy" });
    }

    private void AddAuditEvent(string actorId, string action, string resourceType, string? resourceId, string? details)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = actorId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public enum RealContentTarget
{
    ListeningPaper,
    ReadingPaper,
    WritingPaper,
    SpeakingPaper,
    ResultTemplate,
    SpeakingSharedResource,
    RulebookReferencePdf,
    ScoringPolicyBody,
}

public sealed class RealContentProposal
{
    public RealContentTarget Target { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtest { get; set; }
    public bool AppliesToAllProfessions { get; set; }
    public string? ProfessionId { get; set; }
    public string? CardType { get; set; }
    public string? LetterType { get; set; }
    public string? Slug { get; set; }
    public string? PeriodLabel { get; set; }
    public string? TemplateKey { get; set; }
    public int? SortOrder { get; set; }
    public string? SharedResourceKind { get; set; }
    public string? RulebookKind { get; set; }
    public string? RulebookProfession { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string? StagedStorageKey { get; set; }
    public List<RealContentAssetProposal> Assets { get; set; } = new();
}

public sealed class RealContentAssetProposal
{
    public string Role { get; set; } = string.Empty;
    public string? Part { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string? OriginalFilename { get; set; }
    public string? StagedStorageKey { get; set; }
}

public sealed class RealContentImportStageResult
{
    public string SessionId { get; set; } = string.Empty;
    public string UploadedFilename { get; set; } = string.Empty;
    public DateTimeOffset StagedAt { get; set; }
    public List<RealContentProposal> Proposals { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public sealed class RealContentCreatedRow
{
    public RealContentTarget Target { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public sealed class RealContentCommitResult
{
    public List<RealContentCreatedRow> Created { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
