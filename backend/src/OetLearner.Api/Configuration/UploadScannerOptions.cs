namespace OetLearner.Api.Configuration;

/// <summary>
/// Configures the upload-scanning pipeline. In production we require a real
/// scanner; the NoOp implementation is only acceptable in Development/Testing.
/// Bound from the <c>UploadScanner</c> configuration section.
/// </summary>
public sealed class UploadScannerOptions
{
    /// <summary>
    /// Provider to use. Valid values:
    ///   <c>clamav</c>  — connect to a clamd instance over TCP (recommended for prod).
    ///   <c>noop</c>    — ACCEPTS ALL UPLOADS. Only permitted in non-Production.
    /// </summary>
    public string Provider { get; set; } = "noop";

    /// <summary>clamd host. Ignored unless <see cref="Provider"/> = <c>clamav</c>.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>clamd TCP port. Default is the clamd stock port 3310.</summary>
    public int Port { get; set; } = 3310;

    /// <summary>Per-scan timeout. clamd INSTREAM of large files can take seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When the scanner is unreachable or errors, should uploads be rejected
    /// (<c>fail-closed</c>, safer) or allowed (<c>fail-open</c>)? Default is
    /// fail-closed for production. Only a real operational review should flip
    /// this to false.
    /// </summary>
    public bool FailClosedOnError { get; set; } = true;
}
