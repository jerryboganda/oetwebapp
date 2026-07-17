using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Content;

/// <summary>
/// Virus-scanner backed by a <c>clamd</c> daemon over TCP using the native
/// <c>INSTREAM</c> protocol.
///
/// <para>
/// Why INSTREAM and not <c>SCAN /path</c>: we want to scan bytes <b>before</b>
/// they ever touch disk (the content-addressable storage writes happen only
/// after upload-validator and scanner both pass). INSTREAM chunks the stream
/// over the existing TCP connection and keeps the scan in memory on the clamd
/// side. See https://linux.die.net/man/8/clamd for protocol details.
/// </para>
///
/// <para>
/// Deployment note: run the <c>clamav/clamav</c> container as a compose service
/// named <c>clamav</c> on the same internal network as <c>learner-api</c>, and
/// set <c>UploadScanner__Host=clamav</c>. The clamd definitions file (signatures)
/// is the responsibility of the clamav container's scheduled freshclam run.
/// </para>
/// </summary>
public sealed class ClamAvUploadScanner : IUploadScanner
{
    // clamd chunk size cap is 25 MB by default; stay well under it to be friendly
    // to clamd configurations that tighten StreamMaxLength.
    private const int ChunkSize = 64 * 1024;

    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IHostEnvironment _environment;
    private readonly UploadScannerOptions _deploymentOptions;
    private readonly ILogger<ClamAvUploadScanner> _logger;
    private bool _warnedNoopOnce;

    public ClamAvUploadScanner(
        IRuntimeSettingsProvider runtimeSettings,
        IHostEnvironment environment,
        IOptions<UploadScannerOptions> deploymentOptions,
        ILogger<ClamAvUploadScanner> logger)
    {
        _runtimeSettings = runtimeSettings;
        _environment = environment;
        _deploymentOptions = deploymentOptions.Value;
        _logger = logger;
    }

    public async Task<(bool clean, string? reason)> ScanAsync(
        Stream stream,
        string filename,
        CancellationToken ct)
    {
        var settings = (await _runtimeSettings.GetAsync(ct)).UploadScanner;
        if (!string.Equals(settings.Provider, "clamav", StringComparison.OrdinalIgnoreCase))
        {
            if (_environment.IsProduction())
            {
                _logger.LogError(
                    "Upload scanner provider is {Provider} in production; rejecting {Filename} fail-closed.",
                    settings.Provider, filename);
                return (false, "scan_provider_not_clamav");
            }

            if (!_warnedNoopOnce)
            {
                _warnedNoopOnce = true;
                _logger.LogWarning(
                    "Upload scanner provider is {Provider}; uploads are not being scanned by ClamAV.",
                    settings.Provider);
            }
            return (true, null);
        }

        if (_environment.IsProduction())
        {
            if (!settings.FailClosedOnError)
            {
                _logger.LogError(
                    "Upload scanner failClosedOnError is false in production; rejecting {Filename}.",
                    filename);
                return (false, "scan_fail_open_forbidden");
            }

            var endpointReason = UploadScannerEndpointGuard.GetUnsafeEndpointReason(
                settings.Host,
                settings.Port,
                _deploymentOptions.Host,
                _deploymentOptions.Port,
                requireDeploymentEndpoint: true);
            if (endpointReason is not null)
            {
                _logger.LogError(
                    "Upload scanner endpoint rejected in production for {Filename}: {Reason}",
                    filename,
                    endpointReason);
                return (false, "scan_endpoint_not_allowed");
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        var scanCt = timeoutCts.Token;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(settings.Host, settings.Port, scanCt);
            await using var netStream = tcp.GetStream();

            // INSTREAM command. Null-terminated per clamd protocol.
            var cmd = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await netStream.WriteAsync(cmd, scanCt);

            // Send chunks as <uint32 BE length> <bytes>; zero-length terminates.
            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), scanCt)) > 0)
            {
                var sizePrefix = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(sizePrefix, (uint)read);
                await netStream.WriteAsync(sizePrefix, scanCt);
                await netStream.WriteAsync(buffer.AsMemory(0, read), scanCt);
            }
            var endMarker = new byte[] { 0, 0, 0, 0 };
            await netStream.WriteAsync(endMarker, scanCt);

            // Response is a single line ending at '\0' (because we used zINSTREAM).
            // Typical forms:
            //   "stream: OK\0"
            //   "stream: Win.Test.EICAR_HDB-1 FOUND\0"
            //   "stream: <error>\0"
            using var ms = new MemoryStream();
            var responseBuffer = new byte[512];
            while (true)
            {
                var r = await netStream.ReadAsync(responseBuffer, scanCt);
                if (r == 0) break;
                ms.Write(responseBuffer, 0, r);
                if (Array.IndexOf(responseBuffer, (byte)0, 0, r) >= 0) break;
            }
            var response = Encoding.ASCII.GetString(ms.ToArray()).TrimEnd('\0', '\n', '\r').Trim();

            if (response.EndsWith("OK", StringComparison.Ordinal))
            {
                return (true, null);
            }
            if (response.Contains("FOUND", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "ClamAV flagged upload {Filename}: {Response}",
                    filename, response);
                return (false, response);
            }

            // Anything else is an error. Fail-closed / fail-open based on options.
            _logger.LogError(
                "ClamAV returned unexpected response for {Filename}: {Response}",
                filename, response);
            return settings.FailClosedOnError
                ? (false, $"scan_error: {response}")
                : (true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(
                "ClamAV scan timed out after {Timeout} for {Filename}",
                TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)), filename);
            return settings.FailClosedOnError
                ? (false, "scan_timeout")
                : (true, null);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            _logger.LogError(ex,
                "ClamAV unreachable at {Host}:{Port} while scanning {Filename}",
                settings.Host, settings.Port, filename);
            return settings.FailClosedOnError
                ? (false, "scan_unreachable")
                : (true, null);
        }
    }
}

public static class UploadScannerEndpointGuard
{
    public static string? GetUnsafeEndpointReason(
        string? host,
        int port,
        string? deploymentHost = null,
        int? deploymentPort = null,
        bool requireDeploymentEndpoint = false)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "ClamAV host is required.";
        if (port is < 1 or > 65535)
            return "ClamAV port must be between 1 and 65535.";

        var normalizedHost = NormalizeHost(host);
        if (IsBlockedHostName(normalizedHost))
            return "ClamAV host is not allowed.";
        if (IPAddress.TryParse(normalizedHost, out _))
            return "ClamAV host must be a deployment-owned DNS name, not an IP address literal.";

        if (!requireDeploymentEndpoint)
            return null;

        if (string.IsNullOrWhiteSpace(deploymentHost))
            return "Deployment ClamAV host is not configured.";

        var normalizedDeploymentHost = NormalizeHost(deploymentHost);
        var normalizedDeploymentPort = deploymentPort is > 0 and <= 65535 ? deploymentPort.Value : 3310;
        if (!normalizedHost.Equals(normalizedDeploymentHost, StringComparison.OrdinalIgnoreCase)
            || port != normalizedDeploymentPort)
        {
            return "ClamAV endpoint must match the deployment-owned scanner configuration.";
        }

        return null;
    }

    private static string NormalizeHost(string host)
        => host.Trim().TrimEnd('.');

    private static bool IsBlockedHostName(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
