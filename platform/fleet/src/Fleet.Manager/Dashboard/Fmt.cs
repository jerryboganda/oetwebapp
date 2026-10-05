using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Fleet.Core.Audit;

namespace Fleet.Manager.Dashboard;

/// <summary>
/// Display helpers for the owner console. Everything here returns PLAIN text: Razor encodes it on output, so nothing in the
/// console ever uses a raw-HTML escape hatch. Text that came from a helper, the OET API or a child process goes through
/// <see cref="Untrusted"/> first.
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B[@-Z\\-_]", RegexOptions.CultureInvariant);
    private static readonly Regex Control = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.CultureInvariant);

    /// <summary>
    /// Untrusted text for display: the manager stores helper-originated text HTML-encoded (so it is safe in any sink), which Razor would
    /// encode a second time, so it is decoded first; ANSI and control characters are removed, credential shapes are redacted and the length is capped.
    /// The result is still plain text that Razor encodes.
    /// </summary>
    public static string Untrusted(string? value, int max = 200)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(value);
        text = Ansi.Replace(text, string.Empty);
        text = Control.Replace(text, " ");
        text = LogScrubber.Scrub(text, 4000);
        return text.Length > max ? text[..max] + "…" : text;
    }

    /// <summary>A value that may be missing: the text or an em dash.</summary>
    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static string Age(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return ((int)span.TotalSeconds).ToString(Inv) + " s";
        }

        if (span.TotalMinutes < 60)
        {
            return ((int)span.TotalMinutes).ToString(Inv) + " min";
        }

        return span.TotalHours < 48
            ? ((int)span.TotalHours).ToString(Inv) + " h"
            : ((int)span.TotalDays).ToString(Inv) + " d";
    }

    public static string Ago(DateTimeOffset? at, DateTimeOffset now) =>
        at is null ? "never" : Age(now - at.Value) + " ago";

    /// <summary>"in 12 min" for a time ahead, "expired" for one that has passed, an em dash for none.</summary>
    public static string Until(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is null)
        {
            return "—";
        }

        var span = at.Value - now;
        return span <= TimeSpan.Zero ? "expired" : "in " + Age(span);
    }

    public static string Timestamp(DateTimeOffset? at) =>
        at is null ? "—" : at.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", Inv);

    /// <summary>A fraction (0..1) as a whole percent; an em dash when unknown.</summary>
    public static string Percent(double? fraction) =>
        fraction is null ? "—" : Math.Round(fraction.Value * 100.0, 0).ToString("0", Inv) + "%";

    /// <summary>A value that already is a percentage (0..100), for example a CPU load.</summary>
    public static string PercentPoints(double? value) =>
        value is null ? "—" : Math.Round(value.Value, 0).ToString("0", Inv) + "%";

    public static string MiB(int? mib)
    {
        if (mib is null)
        {
            return "—";
        }

        return mib.Value >= 1024
            ? (mib.Value / 1024.0).ToString("0.#", Inv) + " GiB"
            : mib.Value.ToString(Inv) + " MiB";
    }

    public static string GiB(int? gib) => gib is null ? "—" : gib.Value.ToString(Inv) + " GiB";

    public static string Cores(int? milli) =>
        milli is null ? "—" : (milli.Value / 1000.0).ToString("0.#", Inv) + " vCPU";

    /// <summary>"sha256:" and the first 12 hex characters: enough to tell digests apart on screen.</summary>
    public static string ShortDigest(string? digest)
    {
        if (string.IsNullOrEmpty(digest))
        {
            return "—";
        }

        var clean = Untrusted(digest, 80);
        return clean.StartsWith("sha256:", StringComparison.Ordinal) && clean.Length > 19 ? clean[..19] + "…" : clean;
    }

    /// <summary>The CSS modifier of a status badge. The badge always carries its words too: colour is never the only signal.</summary>
    public static string BadgeClass(string? state) => state switch
    {
        "Active" or "Online" or "online" or "Succeeded" or "done" or "ok" or "intact" or "approved" or "running" or "healthy" => "ok",
        "Draining" or "Probation" or "Stale" or "stale" or "Pending" or "Queued" or "Running" or "Enrolling" or "Created"
            or "HostKeyPending" or "HostKeyConfirmed" or "Bootstrapping" or "Provisioned" or "ImagePulling" or "AgentStarting"
            or "Verifying" or "Canary" or "AwaitingOwner" or "ImageAwaitingSync" or "Removing" or "reduced" or "warn" => "warn",
        "Failed" or "Offline" or "offline" or "Quarantined" or "Revoked" or "failed" or "bad" or "broken" or "host_key_changed" or "quarantined" => "bad",
        "info" => "info",
        _ => "muted",
    };

    /// <summary>What an enrollment or maintenance state means, in words.</summary>
    public static string StateLabel(string? state) => state switch
    {
        "Created" => "Created",
        "HostKeyPending" => "Waiting for your host-key check",
        "HostKeyConfirmed" => "Waiting for the SSH key",
        "Bootstrapping" => "Preparing the server",
        "Provisioned" => "Server prepared",
        "ImageAwaitingSync" => "Waiting for an image sync",
        "ImagePulling" => "Pulling the agent image",
        "AgentStarting" => "Starting the agent",
        "Verifying" => "Waiting for the first heartbeats",
        "Canary" => "Running the self-test",
        "Active" => "Active",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Queued" => "Queued",
        "Running" => "Running",
        "AwaitingOwner" => "Waiting for the SSH key",
        "Succeeded" => "Done",
        null => "—",
        _ => Untrusted(state, 40),
    };

    public static string KindLabel(string? kind) => kind switch
    {
        "enroll" => "Enrollment",
        "drain" => "Drain",
        "disable" => "Disable",
        "enable" => "Resume",
        "remove" => "Removal",
        "rotate-token" => "Token rotation",
        "rollout" => "Image rollout",
        "repair" => "Repair",
        null => "—",
        _ => Untrusted(kind, 40),
    };

    /// <summary>A step name in words. Rollout steps carry the host (<c>roll-image:helper-eu-01</c>).</summary>
    public static string StepLabel(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "—";
        }

        var colon = name.IndexOf(':');
        var baseName = colon < 0 ? name : name[..colon];
        var suffix = colon < 0 ? string.Empty : " (" + Untrusted(name[(colon + 1)..], 63) + ")";
        var label = baseName switch
        {
            "preflight" => "Server checks",
            "fleet-user" => "Restricted fleet account",
            "install-key" => "Manager SSH key",
            "docker" => "Docker engine",
            "firewall" => "Firewall",
            "host-baseline" => "Host baseline",
            "harden-ssh" => "SSH hardening",
            "discard-owner-key" => "Discard your SSH key",
            "image" => "Agent image",
            "agent-start" => "Start the agent",
            "verify" => "First heartbeats",
            "canary" => "Self-test (canary)",
            "activate" => "Activate",
            "api-drain" or "roll-drain" => "Stop new work (drain)",
            "wait-leases" or "wait-leases-strict" or "roll-wait" => "Wait for running jobs",
            "api-disable" => "Disable in the API",
            "ensure-canary" => "Self-test before resuming",
            "api-enable" => "Resume in the API",
            "uninstall" => "Remove fleet components from the helper",
            "api-revoke" => "Revoke in the API",
            "destroy-credentials" => "Erase stored credentials",
            "finalize-remove" => "Finish the removal",
            "rotate-and-render" => "Issue and install a new token",
            "restart-agent" => "Restart the agent",
            "verify-heartbeat" => "Confirm the new token works",
            "finalize-rotate" => "Finish the rotation",
            "roll-image" => "Pull the new image",
            "roll-run" => "Start the new image",
            "roll-verify" => "Confirm the new image",
            "roll-enable" => "Resume the helper",
            "roll-canary" => "Self-test",
            _ => Untrusted(baseName, 40),
        };
        return label + suffix;
    }

    /// <summary>The plain-words meaning of each stable failure reason and what to do about it.</summary>
    public static string FailureGuidance(string? reason) => reason switch
    {
        null => string.Empty,
        "inventory_invalid" => "The address or settings were refused before any connection was made. Correct them and add the helper again.",
        "forbidden_host" => "That address is the primary, a private range or part of the production deployment. A helper must be a separate, public VPS.",
        "host_key_unreachable" => "The helper did not answer on its SSH port. Check the address, the port and the provider firewall, then retry.",
        "host_key_mismatch" => "The characters you typed do not match the fingerprint. Compare it with the provider console again.",
        "host_key_changed" => "The helper's SSH host key is not the one you pinned. It was disabled automatically. Do not retry until you have verified it out of band and re-pinned it.",
        "ssh_unreachable" => "The manager could not reach the helper over SSH. Check that it is running and that its firewall allows the primary, then retry.",
        "auth_failed" => "The helper refused the key. Check that the user and the key are right (replace the key below), then retry.",
        "owner_credential_expired" => "Your temporary SSH key was erased after its 60 minutes. Provide it again to continue.",
        "preflight_rejected" => "The server does not meet the requirements (Ubuntu 22.04/24.04 or Debian 12, x86_64, systemd, 2+ cores, 4+ GiB RAM, 20+ GiB free, no existing OET containers). The detail names the check.",
        "bootstrap_step_failed" => "A preparation step failed. The detail names it; retry resumes at that step and never repeats finished ones.",
        "lockout_risk" => "The manager could not prove it can still log in after hardening SSH, so it stopped before changing anything that could lock you out. Retry or repair.",
        "owner_key_discard_failed" => "Your temporary key could not be erased. Revoke it on the Credentials page, then retry.",
        "api_unreachable" => "The OET API did not answer. Check the fleet credential and that the service is enabled, then retry.",
        "api_register_failed" => "The OET API refused to register the helper. The detail has the API's reason.",
        "token_render_failed" => "The node token could not be written to the helper. Retry.",
        "image_pull_failed" => "The agent image could not be pulled. For a new helper this usually means no pull token: run the fleet sync from CI, then retry.",
        "image_digest_unapproved" => "The agent image is not approved. Approve the release on the Operations page.",
        "image_id_mismatch" => "The pulled image is not the one that was approved. Do not retry blindly: check the release.",
        "agent_start_failed" => "The agent container did not start. The detail has the reason; retry or repair.",
        "agent_not_heartbeating" => "The agent started but the OET API never heard from it within 3 minutes. Check the helper's outbound HTTPS, then retry.",
        "protocol_unsupported" => "The agent speaks a protocol version the API does not accept. Roll out a compatible image.",
        "digest_not_approved_by_api" => "The API does not list this image as approved yet. Approve the release so the policy reaches the node, then retry.",
        "canary_timeout" => "The self-test did not finish in time. Retry.",
        "canary_mismatch" => "The self-test result was wrong, so the helper was NOT activated. Investigate before retrying.",
        "drain_timeout" => "Running jobs did not finish in time. Retry later, or force the removal if the VPS is gone.",
        _ => "An internal error stopped the operation. Retry; if it repeats, check the audit log and the manager's logs.",
    };
}
