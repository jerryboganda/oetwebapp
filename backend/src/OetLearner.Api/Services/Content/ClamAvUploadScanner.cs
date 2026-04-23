using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Content;

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

    private readonly UploadScannerOptions _options;
    private readonly ILogger<ClamAvUploadScanner> _logger;

    public ClamAvUploadScanner(
        IOptions<UploadScannerOptions> options,
        ILogger<ClamAvUploadScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(bool clean, string? reason)> ScanAsync(
        Stream stream,
        string filename,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);
        var scanCt = timeoutCts.Token;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(_options.Host, _options.Port, scanCt);
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
            return _options.FailClosedOnError
                ? (false, $"scan_error: {response}")
                : (true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(
                "ClamAV scan timed out after {Timeout} for {Filename}",
                _options.Timeout, filename);
            return _options.FailClosedOnError
                ? (false, "scan_timeout")
                : (true, null);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            _logger.LogError(ex,
                "ClamAV unreachable at {Host}:{Port} while scanning {Filename}",
                _options.Host, _options.Port, filename);
            return _options.FailClosedOnError
                ? (false, "scan_unreachable")
                : (true, null);
        }
    }
}
