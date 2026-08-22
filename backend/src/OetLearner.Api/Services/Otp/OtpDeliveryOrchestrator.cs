using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Otp;

internal sealed class OtpDeliveryOrchestrator(
    LearnerDbContext db,
    IRuntimeSettingsProvider runtimeSettings,
    IFirebaseSmsOtpClient firebaseSms,
    ILogger<OtpDeliveryOrchestrator>? logger = null) : IOtpDeliveryOrchestrator
{
    public async Task<OtpSmsDeliveryResult> TrySendFirebaseSmsAsync(
        ApplicationUserAccount account,
        string purpose,
        string? recaptchaToken,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(purpose, EmailOtpService.EmailVerificationPurpose, StringComparison.Ordinal))
        {
            return new OtpSmsDeliveryResult(false, false, null, null);
        }

        var settings = (await runtimeSettings.GetAsync(cancellationToken)).FirebaseOtp;
        if (!settings.Enabled || !settings.SmsEnabled || string.IsNullOrWhiteSpace(settings.WebApiKey))
        {
            return new OtpSmsDeliveryResult(false, false, null, null);
        }

        if (string.IsNullOrWhiteSpace(recaptchaToken))
        {
            return new OtpSmsDeliveryResult(false, false, null, null);
        }

        var phone = await ResolveStoredPhoneAsync(account.Id, cancellationToken);
        if (phone is null)
        {
            return new OtpSmsDeliveryResult(false, false, null, null);
        }

        try
        {
            var result = await firebaseSms.SendVerificationCodeAsync(
                phone, recaptchaToken.Trim(), settings.WebApiKey, cancellationToken);

            if (result.Success && !string.IsNullOrWhiteSpace(result.SessionInfo))
            {
                return new OtpSmsDeliveryResult(true, true, phone, result.SessionInfo);
            }

            if (!settings.FallbackToBrevo)
            {
                throw ApiException.Validation(
                    "otp_sms_unavailable",
                    "Unable to send a verification code by SMS. Try again later.");
            }

            logger?.LogWarning("Firebase SMS OTP send did not succeed; falling back to email.");
            return new OtpSmsDeliveryResult(true, false, phone, null);
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!settings.FallbackToBrevo)
            {
                throw ApiException.Validation(
                    "otp_sms_unavailable",
                    "Unable to send a verification code by SMS. Try again later.");
            }

            logger?.LogWarning(ex, "Firebase SMS OTP send failed; falling back to email.");
            return new OtpSmsDeliveryResult(true, false, phone, null);
        }
    }

    private async Task<string?> ResolveStoredPhoneAsync(string accountId, CancellationToken cancellationToken)
    {
        var mobile = await db.LearnerRegistrationProfiles
            .AsNoTracking()
            .Where(x => x.ApplicationUserAccountId == accountId)
            .Select(x => x.MobileNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return PhoneNumberNormalizer.TryNormalize(mobile);
    }
}
