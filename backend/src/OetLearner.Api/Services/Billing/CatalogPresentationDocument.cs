using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OetLearner.Api.Services.Billing;

// ── Wire contracts for the catalog presentation editors ───────────────────────
// Typed DTOs double as the allow-list: System.Text.Json ignores unknown members,
// so anything the editors do not own never reaches the stored document.

/// <summary>Per-area optimistic-concurrency tokens for <c>RuntimeSettings.CatalogPresentationJson</c>.</summary>
public sealed record CatalogPresentationRevisions(string Storefront, string WebsitePackages);

public sealed record WebsitePackageOverlayInput(
    string? Name,
    int? PackageNo,
    string? Category,
    string? Section,
    string? FormatLine,
    string? Description,
    string?[]? MetaChips,
    string?[]? Badges,
    string?[]? Features,
    string? BestFor,
    bool? Featured);

public sealed record WebsiteSectionOverlayInput(string? Title, string? Description);

public sealed record WebsitePackagesInput(
    Dictionary<string, WebsitePackageOverlayInput?>? ByCode,
    Dictionary<string, WebsiteSectionOverlayInput?>? Sections);

/// <summary>One dirty billing row sent with a Subscriptions &amp; Packages save.</summary>
public sealed record PackageCommercialUpdateInput(
    string? Kind,
    string? Code,
    decimal? Price,
    string? Currency,
    string? Interval,
    string? Status,
    bool? IsVisible,
    bool? IsDraft);

public sealed record SaveWebsitePackagesRequest(
    string? ExpectedRevision,
    WebsitePackagesInput? WebsitePackages,
    List<PackageCommercialUpdateInput>? CommercialUpdates);

public sealed record SaveStorefrontRequest(
    string? ExpectedRevision,
    JsonElement? Storefront,
    JsonElement? ByCode);

public sealed record AdminCatalogPlanSummary(
    string Id,
    string Code,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    string Interval,
    string Status,
    bool IsVisible,
    bool IsDraft,
    string ProductCategory,
    string Profession,
    int DisplayOrder,
    int ActiveSubscribers,
    int DurationMonths,
    int AccessDurationDays,
    int IncludedCredits,
    int BundledWritingAssessments,
    int BundledSpeakingSessions,
    int BundledAiCredits,
    bool BundledTutorBook,
    bool BundledBasicEnglish,
    IReadOnlyList<string> DashboardModules,
    IReadOnlyList<string> IncludedSubtests);

public sealed record AdminCatalogAddOnSummary(
    string Id,
    string Code,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    string Interval,
    string Status,
    string AddonKind,
    int DurationDays,
    int GrantCredits,
    bool AppliesToAllPlans);

public sealed record PackageCommercialOutcome(string Kind, string Code, bool Changed);

public sealed record WebsitePackagesSaveResult(
    JsonObject? Presentation,
    CatalogPresentationRevisions Revisions,
    IReadOnlyList<string> DroppedCodes,
    IReadOnlyList<AdminCatalogPlanSummary> Plans,
    IReadOnlyList<AdminCatalogAddOnSummary> AddOns,
    IReadOnlyList<PackageCommercialOutcome> Commercial,
    IReadOnlyList<string> MirroredCodes);

public sealed record StorefrontSaveResult(
    JsonObject? Presentation,
    CatalogPresentationRevisions Revisions,
    IReadOnlyList<string> DroppedCodes);

/// <summary>
/// Package-code helpers shared by the presentation save, the plan lifecycle hooks
/// and the catalog seeder. Overlay keys are the static website codes, while live
/// billing rows may still carry a legacy code.
/// </summary>
public static class CatalogPackageCodes
{
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9_.-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Mirrors `legacyCodes` in lib/catalog-website-packages.ts and PublicSlugForPlanCode in
    // Oet2026CatalogEndpoints: the two standalone Speaking plans keep their *-plan DB codes.
    private static readonly (string Canonical, string Legacy)[] PlanAliases = new (string Canonical, string Legacy)[]
    {
        ("speaking-1session", "speaking-1session-plan"),
        ("speaking-2sessions", "speaking-2sessions-plan"),
    };

    /// <summary>The ten section keys the website package list is grouped by.</summary>
    public static readonly IReadOnlySet<string> SectionKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "full-recorded", "separate", "ai", "listening", "reading",
        "writing-ai", "speaking-ai", "listening-recalls", "tutorbook", "mock",
    };

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidCode(string? normalizedCode) => !string.IsNullOrEmpty(normalizedCode) && CodePattern.IsMatch(normalizedCode);

    /// <summary>
    /// The code itself plus its alias when it is one of the aliased plan codes.
    /// More than one candidate marks a plan-only code: the same code on a legacy
    /// add-on row (for example <c>speaking-1session</c>) must never be treated as the package.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? code)
    {
        var normalized = Normalize(code);
        foreach (var alias in PlanAliases)
        {
            if (normalized == alias.Canonical) return new[] { alias.Canonical, alias.Legacy };
            if (normalized == alias.Legacy) return new[] { alias.Legacy, alias.Canonical };
        }

        return new[] { normalized };
    }

    /// <summary>Default website section for a plan created in Pricing (mirrors defaultSectionForCategory in the web app).</summary>
    public static string DefaultSectionForCategory(string? productCategory)
        => Normalize(productCategory) switch
        {
            "full_course" or "full_course_bundle" or "crash_course" or "crash_course_bundle" or "foundation" => "full-recorded",
            "writing_crash" or "writing_crash_bundle" or "speaking_crash" or "speaking_session" or "combo_double" or "combo_mega" => "separate",
            "recall_package" => "listening-recalls",
            "book" => "tutorbook",
            _ => "separate",
        };
}

/// <summary>
/// Pure helpers for the presentation document stored in
/// <c>RuntimeSettings.CatalogPresentationJson</c>:
/// <c>{ storefront, byCode, websitePackages: { byCode, sections } }</c>.
/// Revisions are hashes of re-serialised subtrees, never of raw database text.
/// </summary>
public static class CatalogPresentationDocument
{
    public const string StorefrontKey = "storefront";
    public const string ByCodeKey = "byCode";
    public const string WebsitePackagesKey = "websitePackages";
    public const string SectionsKey = "sections";

    public const int MaxDocumentBytes = 256 * 1024;
    private const int MaxStorefrontBytes = 64 * 1024;
    private const int MaxCardsBytes = 128 * 1024;
    public const int MaxOverlays = 512;
    private const int MaxSections = 24;

    // Characters stripped from admin text, spelled as numeric casts so no invisible character sits in the source.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;
    private const char ZeroWidthSpace = (char)0x200B;
    private const char ByteOrderMark = (char)0xFEFF;
    private const char BidiEmbeddingFirst = (char)0x202A;
    private const char BidiEmbeddingLast = (char)0x202E;
    private const char BidiIsolateFirst = (char)0x2066;
    private const char BidiIsolateLast = (char)0x2069;

    private static readonly string[] StorefrontRootKeys ={ "accent", "hero", "categories", "professionLabels", "legend", "sections", "cta" };
    private static readonly string[] CardKeys = { "tagline", "featureBullets", "iconKey", "imageUrl", "featured", "badgeLabel", "accent", "displayOrder" };

    // ── Parse / revisions ─────────────────────────────────────────────────

    public static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    public static string Revision(JsonObject root, params string[] keys)
    {
        var canonical = new StringBuilder();
        foreach (var key in keys)
        {
            canonical.Append(key).Append('=').Append(root[key]?.ToJsonString() ?? "null").Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static CatalogPresentationRevisions Revisions(JsonObject root)
        => new(Revision(root, StorefrontKey, ByCodeKey), Revision(root, WebsitePackagesKey));

    /// <summary>Replaces one top-level section. An empty or null value removes the key. The value must be an un-parented node.</summary>
    public static void SetSection(JsonObject root, string key, JsonNode? value)
    {
        if (value is null or JsonObject { Count: 0 })
        {
            root.Remove(key);
        }
        else
        {
            root[key] = value;
        }
    }

    public static JsonObject? WebsitePackagesByCode(JsonObject root)
        => (root[WebsitePackagesKey] as JsonObject)?[ByCodeKey] as JsonObject;

    /// <summary>
    /// Removes, in place, the per-code entries (<c>websitePackages.byCode</c> and the storefront <c>byCode</c>)
    /// that belong only to billing rows the anonymous caller may not see, so unreleased copy never leaks.
    /// A key is kept when any of its alias candidates is visible or when none of them is a known row.
    /// Containers left empty by the pruning are removed too.
    /// </summary>
    public static void PrunePublic(JsonObject root, IReadOnlySet<string> visible, IReadOnlySet<string> hidden)
    {
        if (root[WebsitePackagesKey] is JsonObject packages)
        {
            if (packages[ByCodeKey] is JsonObject packageEntries && PruneEntries(packageEntries, visible, hidden) && packageEntries.Count == 0)
            {
                packages.Remove(ByCodeKey);
            }

            if (packages.Count == 0) root.Remove(WebsitePackagesKey);
        }

        if (root[ByCodeKey] is JsonObject cards && PruneEntries(cards, visible, hidden) && cards.Count == 0)
        {
            root.Remove(ByCodeKey);
        }
    }

    /// <summary>Returns true when at least one entry was removed.</summary>
    private static bool PruneEntries(JsonObject entries, IReadOnlySet<string> visible, IReadOnlySet<string> hidden)
    {
        var removed = false;
        foreach (var key in entries.Select(pair => pair.Key).ToList())
        {
            var candidates = CatalogPackageCodes.Candidates(key);
            if (candidates.Any(candidate => visible.Contains(candidate))) continue;
            if (!candidates.Any(candidate => hidden.Contains(candidate))) continue;

            entries.Remove(key);
            removed = true;
        }

        return removed;
    }

    /// <summary>Trimmed non-blank string value of an overlay key, otherwise null.</summary>
    public static string? ReadEntryText(JsonObject? entry, string key)
    {
        if (entry is null || entry[key] is not JsonValue value || !value.TryGetValue<string>(out var text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>String items of an overlay array (trimmed, blanks dropped), or null when the key is absent or not an array.</summary>
    public static List<string>? ReadEntryStrings(JsonObject? entry, string key)
    {
        if (entry is null || entry[key] is not JsonArray array) return null;
        var list = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                list.Add(text.Trim());
            }
        }

        return list;
    }

    // ── Text sanitising ───────────────────────────────────────────────────

    /// <summary>
    /// Normalises line breaks to <c>\n</c>, strips control characters, zero-width and bidi
    /// override characters (ZWJ and ZWNJ are kept), trims every line. Single-line mode joins
    /// the lines with one space.
    /// </summary>
    public static string CleanText(string? raw, bool multiline)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\r')
            {
                if (i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                builder.Append('\n');
            }
            else if (c == '\n' || c == LineSeparator || c == ParagraphSeparator)
            {
                builder.Append('\n');
            }
            else if (c == '\t')
            {
                builder.Append(' ');
            }
            else if (!IsStrippedChar(c))
            {
                builder.Append(c);
            }
        }

        var lines = builder.ToString().Split('\n').Select(line => line.Trim());
        return multiline
            ? string.Join('\n', lines).Trim()
            : string.Join(' ', lines.Where(line => line.Length > 0));
    }

    private static bool IsStrippedChar(char c)
        => char.IsControl(c)
           || c == ZeroWidthSpace
           || c == ByteOrderMark
           || (c >= BidiEmbeddingFirst && c <= BidiEmbeddingLast)
           || (c >= BidiIsolateFirst && c <= BidiIsolateLast);

    private static string Shorten(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length <= 64) return text;

        // Never cut between the two halves of a surrogate pair: a lone surrogate cannot be written as JSON.
        var cut = char.IsHighSurrogate(text[63]) ? 63 : 64;
        return text[..cut] + "...";
    }

    private static void AddDropped(List<string> droppedCodes, string code)
    {
        if (!droppedCodes.Contains(code)) droppedCodes.Add(code);
    }

    // ── Website packages ──────────────────────────────────────────────────

    /// <summary>
    /// Validates and canonicalises the <c>websitePackages</c> subtree. Over-limit values are
    /// reported, never truncated. Format-valid codes with no billing row, and live codes outside the code format,
    /// are dropped and listed in <paramref name="droppedCodes"/>. Returns null when nothing is left to store.
    /// </summary>
    public static JsonObject? SanitizeWebsitePackages(
        WebsitePackagesInput? input,
        ISet<string> knownCodes,
        List<ApiFieldError> errors,
        List<string> droppedCodes)
    {
        if (input is null) return null;

        var packages = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        if (input.ByCode is { } overlays)
        {
            if (overlays.Count > MaxOverlays)
            {
                errors.Add(new ApiFieldError(
                    "websitePackages.byCode",
                    "too_many",
                    $"At most {MaxOverlays} package entries can be saved; {overlays.Count} were sent."));
            }
            else
            {
                foreach (var pair in overlays)
                {
                    var code = CatalogPackageCodes.Normalize(pair.Key);
                    if (!CatalogPackageCodes.IsValidCode(code))
                    {
                        // A live plan whose code is outside the overlay code format (for example a custom "_x")
                        // can never carry an overlay; drop it instead of failing the whole save.
                        if (knownCodes.Contains(code))
                        {
                            AddDropped(droppedCodes, code);
                            continue;
                        }

                        errors.Add(new ApiFieldError(
                            "websitePackages.byCode." + Shorten(pair.Key),
                            "package_code_invalid",
                            $"'{Shorten(pair.Key)}' is not a valid package code."));
                        continue;
                    }

                    if (pair.Value is null) continue;
                    if (!knownCodes.Contains(code))
                    {
                        AddDropped(droppedCodes, code);
                        continue;
                    }

                    var entry = SanitizeOverlay(code, pair.Value, errors);
                    if (entry is not null) packages[code] = entry;
                }
            }
        }

        var sections = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        if (input.Sections is { } sectionInputs)
        {
            if (sectionInputs.Count > MaxSections)
            {
                errors.Add(new ApiFieldError(
                    "websitePackages.sections",
                    "too_many",
                    $"At most {MaxSections} section headings can be saved; {sectionInputs.Count} were sent."));
            }
            else
            {
                foreach (var pair in sectionInputs)
                {
                    var key = CatalogPackageCodes.Normalize(pair.Key);
                    var path = "websitePackages.sections." + Shorten(pair.Key);
                    if (!CatalogPackageCodes.SectionKeys.Contains(key))
                    {
                        errors.Add(new ApiFieldError(path, "invalid_section", $"'{Shorten(pair.Key)}' is not a known section."));
                        continue;
                    }

                    if (pair.Value is null) continue;
                    var title = CleanText(pair.Value.Title, false);
                    var description = CleanText(pair.Value.Description, true);
                    if (title.Length > 120)
                    {
                        errors.Add(TooLong(path + ".title", $"Section {key} title", title.Length, 120));
                    }

                    if (description.Length > 600)
                    {
                        errors.Add(TooLong(path + ".description", $"Section {key} description", description.Length, 600));
                    }

                    var entry = new JsonObject();
                    if (title.Length > 0) entry["title"] = title;
                    if (description.Length > 0) entry["description"] = description;
                    if (entry.Count > 0) sections[key] = entry;
                }
            }
        }

        var result = new JsonObject();
        if (packages.Count > 0)
        {
            var byCode = new JsonObject();
            foreach (var pair in packages) byCode[pair.Key] = pair.Value;
            result[ByCodeKey] = byCode;
        }

        if (sections.Count > 0)
        {
            var sectionNode = new JsonObject();
            foreach (var pair in sections) sectionNode[pair.Key] = pair.Value;
            result[SectionsKey] = sectionNode;
        }

        droppedCodes.Sort(StringComparer.Ordinal);
        return result.Count == 0 ? null : result;
    }

    private static JsonObject? SanitizeOverlay(string code, WebsitePackageOverlayInput input, List<ApiFieldError> errors)
    {
        var path = "websitePackages.byCode." + code;
        var entry = new JsonObject();

        var name = CleanText(input.Name, false);
        if (name.Length > 128) errors.Add(TooLong(path + ".name", $"Package {code} name", name.Length, 128));
        else if (name.Length > 0) entry["name"] = name;

        if (input.PackageNo is { } packageNo)
        {
            if (packageNo < 1 || packageNo > 999)
            {
                errors.Add(new ApiFieldError(path + ".packageNo", "out_of_range", $"Package {code} number must be between 1 and 999."));
            }
            else
            {
                entry["packageNo"] = packageNo;
            }
        }

        var category = CleanText(input.Category, false);
        if (category.Length > 120) errors.Add(TooLong(path + ".category", $"Package {code} category", category.Length, 120));
        else if (category.Length > 0) entry["category"] = category;

        var section = CleanText(input.Section, false).ToLowerInvariant();
        if (section.Length > 0)
        {
            if (CatalogPackageCodes.SectionKeys.Contains(section))
            {
                entry["section"] = section;
            }
            else
            {
                errors.Add(new ApiFieldError(path + ".section", "invalid_section", $"Package {code} has an unknown section '{Shorten(section)}'."));
            }
        }

        var formatLine = CleanText(input.FormatLine, false);
        if (formatLine.Length > 240) errors.Add(TooLong(path + ".formatLine", $"Package {code} format line", formatLine.Length, 240));
        else if (formatLine.Length > 0) entry["formatLine"] = formatLine;

        var description = CleanText(input.Description, true);
        if (description.Length > 1024) errors.Add(TooLong(path + ".description", $"Package {code} description", description.Length, 1024));
        else if (description.Length > 0) entry["description"] = description;

        AddList(entry, "metaChips", input.MetaChips, 8, 80, path, code, errors);
        AddList(entry, "badges", input.Badges, 8, 60, path, code, errors);
        AddList(entry, "features", input.Features, 30, 300, path, code, errors);

        var bestFor = CleanText(input.BestFor, true);
        if (bestFor.Length > 500) errors.Add(TooLong(path + ".bestFor", $"Package {code} best-for text", bestFor.Length, 500));
        else if (bestFor.Length > 0) entry["bestFor"] = bestFor;

        if (input.Featured is { } featured) entry["featured"] = featured;

        return entry.Count == 0 ? null : entry;
    }

    /// <summary>A present list is kept even when empty: an explicit empty list means "show none".</summary>
    private static void AddList(
        JsonObject entry,
        string key,
        string?[]? items,
        int maxItems,
        int maxLength,
        string path,
        string code,
        List<ApiFieldError> errors)
    {
        if (items is null) return;
        var lines = CleanList(items, maxItems, maxLength, path + "." + key, $"Package {code} {key}", errors);
        if (lines is null) return;

        var array = new JsonArray();
        foreach (var line in lines) array.Add(line);
        entry[key] = array;
    }

    private static List<string>? CleanList(
        IEnumerable<string?> raw,
        int maxItems,
        int maxLength,
        string path,
        string label,
        List<ApiFieldError> errors)
    {
        var lines = new List<string>();
        foreach (var item in raw)
        {
            var line = CleanText(item, false);
            if (line.Length == 0) continue;
            if (line.Length > maxLength)
            {
                errors.Add(new ApiFieldError(path, "too_long", $"{label}: an entry is {line.Length} characters; the limit is {maxLength}."));
                return null;
            }

            lines.Add(line);
        }

        if (lines.Count > maxItems)
        {
            errors.Add(new ApiFieldError(path, "too_many", $"{label}: {lines.Count} entries were sent; the limit is {maxItems}."));
            return null;
        }

        return lines;
    }

    private static ApiFieldError TooLong(string field, string label, int length, int limit)
        => new(field, "too_long", $"{label} is {length} characters; the limit is {limit}.");

    // ── Storefront + per-card presentation ────────────────────────────────

    /// <summary>
    /// Allow-lists the storefront config and the per-card overlays. Unknown card codes are dropped,
    /// invalid codes and over-size or unsafe values are reported. Null results mean "empty".
    /// </summary>
    public static (JsonObject? Storefront, JsonObject? ByCode) SanitizeStorefront(
        JsonElement? storefront,
        JsonElement? byCode,
        ISet<string> knownCodes,
        List<ApiFieldError> errors,
        List<string> droppedCodes)
    {
        var storefrontNode = SanitizeStorefrontRoot(storefront, errors);
        var byCodeNode = SanitizeCards(byCode, knownCodes, errors, droppedCodes);
        droppedCodes.Sort(StringComparer.Ordinal);
        return (storefrontNode, byCodeNode);
    }

    private static JsonObject? SanitizeStorefrontRoot(JsonElement? element, List<ApiFieldError> errors)
    {
        if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ApiFieldError("storefront", "invalid_type", "storefront must be an object."));
            return null;
        }

        if (JsonNode.Parse(value.GetRawText()) is not JsonObject source) return null;

        var result = new JsonObject();
        foreach (var key in StorefrontRootKeys)
        {
            if (!source.TryGetPropertyValue(key, out var child) || child is null) continue;
            if (child.GetValueKind() != ExpectedStorefrontKind(key))
            {
                errors.Add(new ApiFieldError("storefront." + key, "invalid_type", $"storefront.{key} has the wrong type."));
                continue;
            }

            result[key] = child.DeepClone();
        }

        CleanStrings(result);
        if (result["cta"] is JsonObject cta)
        {
            CheckUrl(cta, "primaryHref", "storefront.cta.primaryHref", errors);
            CheckUrl(cta, "secondaryHref", "storefront.cta.secondaryHref", errors);
            CheckUrl(cta, "imageUrl", "storefront.cta.imageUrl", errors);
        }

        if (result.Count == 0) return null;
        if (Encoding.UTF8.GetByteCount(result.ToJsonString()) > MaxStorefrontBytes)
        {
            errors.Add(new ApiFieldError("storefront", "too_large", $"storefront is larger than the {MaxStorefrontBytes / 1024} KB limit."));
            return null;
        }

        return result;
    }

    private static JsonValueKind ExpectedStorefrontKind(string key) => key switch
    {
        "accent" => JsonValueKind.String,
        "categories" or "legend" => JsonValueKind.Array,
        _ => JsonValueKind.Object,
    };

    private static JsonObject? SanitizeCards(
        JsonElement? element,
        ISet<string> knownCodes,
        List<ApiFieldError> errors,
        List<string> droppedCodes)
    {
        if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ApiFieldError("byCode", "invalid_type", "byCode must be an object."));
            return null;
        }

        if (JsonNode.Parse(value.GetRawText()) is not JsonObject source) return null;

        var cards = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var property in source)
        {
            var code = CatalogPackageCodes.Normalize(property.Key);
            if (!CatalogPackageCodes.IsValidCode(code))
            {
                // Same rule as the package overlay: a live code outside the format is dropped, not a 400.
                if (knownCodes.Contains(code))
                {
                    AddDropped(droppedCodes, code);
                    continue;
                }

                errors.Add(new ApiFieldError(
                    "byCode." + Shorten(property.Key),
                    "package_code_invalid",
                    $"'{Shorten(property.Key)}' is not a valid package code."));
                continue;
            }

            if (property.Value is null) continue;
            if (property.Value is not JsonObject card)
            {
                errors.Add(new ApiFieldError("byCode." + code, "invalid_type", $"byCode.{code} must be an object."));
                continue;
            }

            if (!knownCodes.Contains(code))
            {
                AddDropped(droppedCodes, code);
                continue;
            }

            var clean = SanitizeCard(code, card, errors);

            // The learner looks cards up by the live plan code as typed, so a mixed-case code keeps its
            // trimmed original spelling; validation and the known-code check above use the lower-cased form.
            var storedKey = property.Key.Trim();
            if (!string.Equals(storedKey, code, StringComparison.OrdinalIgnoreCase)) storedKey = code;
            if (clean.Count > 0) cards[storedKey] = clean;
        }

        if (cards.Count == 0) return null;
        var result = new JsonObject();
        foreach (var pair in cards) result[pair.Key] = pair.Value;

        if (Encoding.UTF8.GetByteCount(result.ToJsonString()) > MaxCardsBytes)
        {
            errors.Add(new ApiFieldError("byCode", "too_large", $"byCode is larger than the {MaxCardsBytes / 1024} KB limit."));
            return null;
        }

        return result;
    }

    private static JsonObject SanitizeCard(string code, JsonObject card, List<ApiFieldError> errors)
    {
        var path = "byCode." + code;
        var clean = new JsonObject();
        foreach (var key in CardKeys)
        {
            if (!card.TryGetPropertyValue(key, out var child) || child is null) continue;
            var field = path + "." + key;

            if (key == "featureBullets")
            {
                if (child is not JsonArray bullets)
                {
                    errors.Add(new ApiFieldError(field, "invalid_type", $"{field} must be a list."));
                    continue;
                }

                var raw = bullets.Select(item => item is JsonValue itemValue && itemValue.TryGetValue<string>(out var text) ? text : null);
                var lines = CleanList(raw, 30, 300, field, $"Card {code} feature bullets", errors);
                if (lines is null) continue;

                var array = new JsonArray();
                foreach (var line in lines) array.Add(line);
                clean[key] = array;
            }
            else if (key == "featured")
            {
                if (child.GetValueKind() is JsonValueKind.True or JsonValueKind.False) clean[key] = child.GetValue<bool>();
                else errors.Add(new ApiFieldError(field, "invalid_type", $"{field} must be true or false."));
            }
            else if (key == "displayOrder")
            {
                if (child.GetValueKind() == JsonValueKind.Number) clean[key] = child.DeepClone();
                else errors.Add(new ApiFieldError(field, "invalid_type", $"{field} must be a number."));
            }
            else if (child is JsonValue stringValue && stringValue.TryGetValue<string>(out var value))
            {
                var cleaned = CleanText(value, key == "tagline");
                if (cleaned.Length > 0) clean[key] = cleaned;
            }
            else
            {
                errors.Add(new ApiFieldError(field, "invalid_type", $"{field} must be text."));
            }
        }

        CheckUrl(clean, "imageUrl", path + ".imageUrl", errors);
        return clean;
    }

    /// <summary>Recursively cleans every string value (control and bidi characters, line breaks).</summary>
    private static void CleanStrings(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(pair => pair.Key).ToList())
            {
                var child = obj[key];
                if (child is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = CleanText(text, true);
                else CleanStrings(child);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var child = array[i];
                if (child is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = CleanText(text, true);
                else CleanStrings(child);
            }
        }
    }

    private static void CheckUrl(JsonObject owner, string field, string path, List<ApiFieldError> errors)
    {
        if (!owner.TryGetPropertyValue(field, out var node) || node is null) return;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            errors.Add(new ApiFieldError(path, "invalid_type", $"{path} must be text."));
            return;
        }

        var url = text.Trim();
        if (url.Length == 0) return;

        // Browsers drop tabs and line breaks inside a URL, so "/" + newline + "/host" would become a protocol-relative link.
        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || !IsSafeUrl(url))
        {
            errors.Add(new ApiFieldError(path, "invalid_url", $"{path} must start with / or https:// and contain no spaces."));
        }
    }

    private static bool IsSafeUrl(string url)
        => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || (url.StartsWith('/')
               && !url.StartsWith("//", StringComparison.Ordinal)
               && !url.StartsWith("/\\", StringComparison.Ordinal));
}
