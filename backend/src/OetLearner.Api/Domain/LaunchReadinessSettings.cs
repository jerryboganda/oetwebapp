using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Admin-managed launch evidence and server-driven release policy.
/// Native signing assets and private keys are intentionally represented as
/// references/status only; raw credentials stay outside the database.
/// </summary>
public class LaunchReadinessSettings
{
    [Key]
    [MaxLength(32)]
    public string Id { get; set; } = "global";

    [MaxLength(32)] public string MobileMinSupportedVersion { get; set; } = "1.0.0";
    [MaxLength(32)] public string MobileLatestVersion { get; set; } = "1.0.0";
    public bool MobileForceUpdate { get; set; }
    [MaxLength(512)] public string? IosAppStoreUrl { get; set; }
    [MaxLength(512)] public string? AndroidPlayStoreUrl { get; set; }
    [MaxLength(128)] public string? IosBundleId { get; set; }
    [MaxLength(64)] public string? AppleTeamId { get; set; }
    [MaxLength(64)] public string? AppleAssociatedDomainStatus { get; set; }
    [MaxLength(64)] public string? AppleUniversalLinksStatus { get; set; }
    [MaxLength(512)] public string? IosSigningProfileReference { get; set; }
    [MaxLength(64)] public string? IosIapStatus { get; set; }
    [MaxLength(64)] public string? IosPushStatus { get; set; }
    [MaxLength(128)] public string? AndroidPackageName { get; set; }
    [MaxLength(2048)] public string? AndroidSha256Fingerprints { get; set; }
    [MaxLength(512)] public string? AndroidSigningKeyReference { get; set; }
    [MaxLength(64)] public string? AndroidAssetLinksStatus { get; set; }
    [MaxLength(64)] public string? AndroidIapStatus { get; set; }
    [MaxLength(64)] public string? AndroidPushStatus { get; set; }

    [MaxLength(32)] public string DesktopMinSupportedVersion { get; set; } = "1.0.0";
    [MaxLength(32)] public string DesktopLatestVersion { get; set; } = "1.0.0";
    public bool DesktopForceUpdate { get; set; }
    [MaxLength(512)] public string? DesktopUpdateFeedUrl { get; set; }
    [MaxLength(64)] public string? DesktopUpdateChannel { get; set; }
    [MaxLength(64)] public string? WindowsSigningStatus { get; set; }
    [MaxLength(64)] public string? MacSigningStatus { get; set; }
    [MaxLength(64)] public string? LinuxSigningStatus { get; set; }

    [MaxLength(512)] public string? DeviceValidationEvidenceUrl { get; set; }
    [MaxLength(2048)] public string? DeviceValidationNotes { get; set; }
    [MaxLength(64)] public string? RealtimeLegalApprovalStatus { get; set; }
    [MaxLength(64)] public string? RealtimePrivacyApprovalStatus { get; set; }
    [MaxLength(64)] public string? RealtimeProtectedSmokeStatus { get; set; }
    [MaxLength(512)] public string? RealtimeEvidenceUrl { get; set; }
    public bool RealtimeSpendCapApproved { get; set; }
    public bool RealtimeTopologyApproved { get; set; }
    [MaxLength(64)] public string? ReleaseOwnerApprovalStatus { get; set; }
    [MaxLength(2048)] public string? LaunchNotes { get; set; }

    [MaxLength(64)] public string? UpdatedByAdminId { get; set; }
    [MaxLength(128)] public string? UpdatedByAdminName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
