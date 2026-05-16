using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public interface ILaunchReadinessService
{
    Task<AdminLaunchReadinessSettingsResponse> GetSettingsAsync(CancellationToken ct);
    Task<AdminLaunchReadinessSettingsResponse> UpdateSettingsAsync(
        string? adminId,
        string? adminName,
        AdminLaunchReadinessSettingsRequest request,
        CancellationToken ct);
    Task<PublicAppReleaseSettingsResponse> GetPublicReleasePolicyAsync(string? platform, CancellationToken ct);
}

public sealed class LaunchReadinessService(LearnerDbContext db) : ILaunchReadinessService
{
    private static readonly Regex VersionPattern = new(
        @"^\d+(?:\.\d+){0,3}(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<AdminLaunchReadinessSettingsResponse> GetSettingsAsync(CancellationToken ct)
        => ToResponse(await GetOrCreateAsync(ct));

    public async Task<AdminLaunchReadinessSettingsResponse> UpdateSettingsAsync(
        string? adminId,
        string? adminName,
        AdminLaunchReadinessSettingsRequest request,
        CancellationToken ct)
    {
        Validate(request);
        var row = await GetOrCreateAsync(ct);

        Apply(request, row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByAdminId = adminId;
        row.UpdatedByAdminName = adminName;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            ActorId = adminId ?? "system",
            ActorName = adminName ?? "system",
            Action = "LaunchReadinessSettingsUpdated",
            ResourceType = "LaunchReadinessSettings",
            ResourceId = row.Id,
            Details = JsonSupport.Serialize(new
            {
                row.MobileMinSupportedVersion,
                row.MobileLatestVersion,
                row.MobileForceUpdate,
                row.DesktopMinSupportedVersion,
                row.DesktopLatestVersion,
                row.DesktopForceUpdate,
                row.ReleaseOwnerApprovalStatus,
                row.RealtimeLegalApprovalStatus,
                row.RealtimePrivacyApprovalStatus,
                row.RealtimeProtectedSmokeStatus,
                row.RealtimeSpendCapApproved,
                row.RealtimeTopologyApproved,
            }),
            OccurredAt = row.UpdatedAt,
        });

        await db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task<PublicAppReleaseSettingsResponse> GetPublicReleasePolicyAsync(string? platform, CancellationToken ct)
    {
        var row = await GetOrCreateAsync(ct);
        var normalized = string.IsNullOrWhiteSpace(platform) ? "mobile" : platform.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ios" => new(normalized, row.MobileMinSupportedVersion, row.MobileLatestVersion, row.MobileForceUpdate, row.IosAppStoreUrl, null, null),
            "android" => new(normalized, row.MobileMinSupportedVersion, row.MobileLatestVersion, row.MobileForceUpdate, row.AndroidPlayStoreUrl, null, null),
            "desktop" or "windows" or "mac" or "macos" or "linux" => new(normalized, row.DesktopMinSupportedVersion, row.DesktopLatestVersion, row.DesktopForceUpdate, null, row.DesktopUpdateFeedUrl, row.DesktopUpdateChannel),
            _ => new(normalized, row.MobileMinSupportedVersion, row.MobileLatestVersion, row.MobileForceUpdate, null, null, null),
        };
    }

    private async Task<LaunchReadinessSettings> GetOrCreateAsync(CancellationToken ct)
    {
        var row = await db.LaunchReadinessSettings.FirstOrDefaultAsync(x => x.Id == "global", ct);
        if (row is not null) return row;

        row = new LaunchReadinessSettings
        {
            Id = "global",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.LaunchReadinessSettings.Add(row);
        return row;
    }

    private static void Validate(AdminLaunchReadinessSettingsRequest request)
    {
        ValidateVersion(request.MobileMinSupportedVersion, nameof(request.MobileMinSupportedVersion));
        ValidateVersion(request.MobileLatestVersion, nameof(request.MobileLatestVersion));
        ValidateVersion(request.DesktopMinSupportedVersion, nameof(request.DesktopMinSupportedVersion));
        ValidateVersion(request.DesktopLatestVersion, nameof(request.DesktopLatestVersion));

        ValidateHttpsUrl(request.IosAppStoreUrl, nameof(request.IosAppStoreUrl));
        ValidateHttpsUrl(request.AndroidPlayStoreUrl, nameof(request.AndroidPlayStoreUrl));
        ValidateHttpsUrl(request.DesktopUpdateFeedUrl, nameof(request.DesktopUpdateFeedUrl));
        ValidateHttpsUrl(request.DeviceValidationEvidenceUrl, nameof(request.DeviceValidationEvidenceUrl));
        ValidateHttpsUrl(request.RealtimeEvidenceUrl, nameof(request.RealtimeEvidenceUrl));
    }

    private static void ValidateVersion(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!VersionPattern.IsMatch(value.Trim()))
            throw ApiException.Validation("INVALID_RELEASE_VERSION", $"{field} must be a semantic version such as 1.2.3.");
    }

    private static void ValidateHttpsUrl(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw ApiException.Validation("INVALID_RELEASE_URL", $"{field} must be an external https:// URL.");
        }
    }

    private static void Apply(AdminLaunchReadinessSettingsRequest r, LaunchReadinessSettings row)
    {
        if (r.MobileMinSupportedVersion is not null) row.MobileMinSupportedVersion = Clean(r.MobileMinSupportedVersion) ?? "1.0.0";
        if (r.MobileLatestVersion is not null) row.MobileLatestVersion = Clean(r.MobileLatestVersion) ?? "1.0.0";
        if (r.MobileForceUpdate.HasValue) row.MobileForceUpdate = r.MobileForceUpdate.Value;
        if (r.IosAppStoreUrl is not null) row.IosAppStoreUrl = Clean(r.IosAppStoreUrl);
        if (r.AndroidPlayStoreUrl is not null) row.AndroidPlayStoreUrl = Clean(r.AndroidPlayStoreUrl);
        if (r.IosBundleId is not null) row.IosBundleId = Clean(r.IosBundleId);
        if (r.AppleTeamId is not null) row.AppleTeamId = Clean(r.AppleTeamId);
        if (r.AppleAssociatedDomainStatus is not null) row.AppleAssociatedDomainStatus = Clean(r.AppleAssociatedDomainStatus);
        if (r.AppleUniversalLinksStatus is not null) row.AppleUniversalLinksStatus = Clean(r.AppleUniversalLinksStatus);
        if (r.IosSigningProfileReference is not null) row.IosSigningProfileReference = Clean(r.IosSigningProfileReference);
        if (r.IosIapStatus is not null) row.IosIapStatus = Clean(r.IosIapStatus);
        if (r.IosPushStatus is not null) row.IosPushStatus = Clean(r.IosPushStatus);
        if (r.AndroidPackageName is not null) row.AndroidPackageName = Clean(r.AndroidPackageName);
        if (r.AndroidSha256Fingerprints is not null) row.AndroidSha256Fingerprints = Clean(r.AndroidSha256Fingerprints);
        if (r.AndroidSigningKeyReference is not null) row.AndroidSigningKeyReference = Clean(r.AndroidSigningKeyReference);
        if (r.AndroidAssetLinksStatus is not null) row.AndroidAssetLinksStatus = Clean(r.AndroidAssetLinksStatus);
        if (r.AndroidIapStatus is not null) row.AndroidIapStatus = Clean(r.AndroidIapStatus);
        if (r.AndroidPushStatus is not null) row.AndroidPushStatus = Clean(r.AndroidPushStatus);
        if (r.DesktopMinSupportedVersion is not null) row.DesktopMinSupportedVersion = Clean(r.DesktopMinSupportedVersion) ?? "1.0.0";
        if (r.DesktopLatestVersion is not null) row.DesktopLatestVersion = Clean(r.DesktopLatestVersion) ?? "1.0.0";
        if (r.DesktopForceUpdate.HasValue) row.DesktopForceUpdate = r.DesktopForceUpdate.Value;
        if (r.DesktopUpdateFeedUrl is not null) row.DesktopUpdateFeedUrl = Clean(r.DesktopUpdateFeedUrl);
        if (r.DesktopUpdateChannel is not null) row.DesktopUpdateChannel = Clean(r.DesktopUpdateChannel);
        if (r.WindowsSigningStatus is not null) row.WindowsSigningStatus = Clean(r.WindowsSigningStatus);
        if (r.MacSigningStatus is not null) row.MacSigningStatus = Clean(r.MacSigningStatus);
        if (r.LinuxSigningStatus is not null) row.LinuxSigningStatus = Clean(r.LinuxSigningStatus);
        if (r.DeviceValidationEvidenceUrl is not null) row.DeviceValidationEvidenceUrl = Clean(r.DeviceValidationEvidenceUrl);
        if (r.DeviceValidationNotes is not null) row.DeviceValidationNotes = Clean(r.DeviceValidationNotes);
        if (r.RealtimeLegalApprovalStatus is not null) row.RealtimeLegalApprovalStatus = Clean(r.RealtimeLegalApprovalStatus);
        if (r.RealtimePrivacyApprovalStatus is not null) row.RealtimePrivacyApprovalStatus = Clean(r.RealtimePrivacyApprovalStatus);
        if (r.RealtimeProtectedSmokeStatus is not null) row.RealtimeProtectedSmokeStatus = Clean(r.RealtimeProtectedSmokeStatus);
        if (r.RealtimeEvidenceUrl is not null) row.RealtimeEvidenceUrl = Clean(r.RealtimeEvidenceUrl);
        if (r.RealtimeSpendCapApproved.HasValue) row.RealtimeSpendCapApproved = r.RealtimeSpendCapApproved.Value;
        if (r.RealtimeTopologyApproved.HasValue) row.RealtimeTopologyApproved = r.RealtimeTopologyApproved.Value;
        if (r.ReleaseOwnerApprovalStatus is not null) row.ReleaseOwnerApprovalStatus = Clean(r.ReleaseOwnerApprovalStatus);
        if (r.LaunchNotes is not null) row.LaunchNotes = Clean(r.LaunchNotes);
    }

    private static string? Clean(string? value)
    {
        var cleaned = value?.Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private static AdminLaunchReadinessSettingsResponse ToResponse(LaunchReadinessSettings r) => new(
        r.MobileMinSupportedVersion,
        r.MobileLatestVersion,
        r.MobileForceUpdate,
        r.IosAppStoreUrl,
        r.AndroidPlayStoreUrl,
        r.IosBundleId,
        r.AppleTeamId,
        r.AppleAssociatedDomainStatus,
        r.AppleUniversalLinksStatus,
        r.IosSigningProfileReference,
        r.IosIapStatus,
        r.IosPushStatus,
        r.AndroidPackageName,
        r.AndroidSha256Fingerprints,
        r.AndroidSigningKeyReference,
        r.AndroidAssetLinksStatus,
        r.AndroidIapStatus,
        r.AndroidPushStatus,
        r.DesktopMinSupportedVersion,
        r.DesktopLatestVersion,
        r.DesktopForceUpdate,
        r.DesktopUpdateFeedUrl,
        r.DesktopUpdateChannel,
        r.WindowsSigningStatus,
        r.MacSigningStatus,
        r.LinuxSigningStatus,
        r.DeviceValidationEvidenceUrl,
        r.DeviceValidationNotes,
        r.RealtimeLegalApprovalStatus,
        r.RealtimePrivacyApprovalStatus,
        r.RealtimeProtectedSmokeStatus,
        r.RealtimeEvidenceUrl,
        r.RealtimeSpendCapApproved,
        r.RealtimeTopologyApproved,
        r.ReleaseOwnerApprovalStatus,
        r.LaunchNotes,
        r.UpdatedAt,
        r.UpdatedByAdminId,
        r.UpdatedByAdminName);
}
