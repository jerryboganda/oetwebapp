using System.Text;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionSupportKnowledgeIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Publishes the product's own account, access and billing rules as retrievable
/// support knowledge (Manifest 1.E).
///
/// <para>
/// Testing Pack 4 scenarios 13 and 14 ask the companion the two questions
/// support gets most: "I can't log in on my new phone, it wants a code" and "how
/// many devices am I allowed?". Neither was answerable. The rules existed only
/// as C# constants and runtime settings, so the companion either refused or —
/// worse, and the thing the packs score hardest — produced a confident,
/// plausible, wrong number. A learner told "you can use five devices" when the
/// real limit is two files a support ticket the next day.
/// </para>
///
/// <para>
/// <b>Read live, never frozen.</b> Every figure below comes from the same place
/// the enforcing code reads it: <see cref="TrustedDeviceService.DefaultMaxDevices"/>,
/// the security block of <see cref="IRuntimeSettingsProvider"/>, and
/// <see cref="AiGradingCreditCost"/>. An admin who changes the device-change
/// window in the settings screen changes what Sami says at the next reindex,
/// because there is no second copy of the number to forget about.
/// </para>
///
/// <para>
/// The one thing this deliberately does not do is state the learner's own
/// position — how many devices <i>they</i> have registered, whether <i>their</i>
/// package is expired. Those are per-learner facts, and putting them in a shared
/// corpus is precisely the cross-learner leak the isolation tests exist to
/// prevent. The rules are shared; the learner's state comes from their own
/// context on the turn.
/// </para>
/// </summary>
public sealed class CompanionSupportKnowledgeIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionSupportKnowledgeIndexer> logger,
    // Optional so the enforced defaults are reachable without a settings store.
    // The indexer already has to survive an unreadable settings row, and a test
    // that cannot exercise that path is testing the wrong thing.
    IRuntimeSettingsProvider? settings = null) : ICompanionSupportKnowledgeIndexer
{
    internal const string SourceKey = "platform:support-knowledge";
    internal const string Version = "2026-09-09.1";

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        SecuritySettings? security = null;
        try
        {
            if (settings is not null) security = (await settings.GetAsync(ct)).Security;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Degrade to the defaults the enforcing code itself falls back to,
            // rather than publishing nothing: a companion that cannot answer
            // "how many devices" at all is worse than one quoting the default,
            // and the default is what an unconfigured install actually enforces.
            logger.LogWarning(ex, "Runtime settings unavailable; support knowledge uses enforced defaults.");
            warnings.Add("Runtime settings could not be read; device/OTP figures fall back to code defaults.");
        }

        var windowDays = security?.DeviceChangeWindowDays ?? 7;
        var maxPerWindow = security?.DeviceChangeMaxPerWindow ?? 3;

        return await CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, SourceKey, Version,
            source =>
            {
                source.SourceType = "support_knowledge";
                source.Title = "Account, access and billing rules";
                source.AuthorityClass = CompanionAuthorityClass.PlatformSupport;
                source.State = CompanionSourceState.Approved;
                source.ExamTypeCode = "OET";
                source.ProfessionId = null;
                source.SubtestCode = null;
                source.IsProprietary = false;
                source.RequiredEntitlementScope = null;
                source.PackageScope = null;
                source.StorageLocator = "backend/src/OetLearner.Api/Security/TrustedDeviceService.cs";
                source.ApprovedAt ??= DateTimeOffset.UtcNow;
            },
            BuildChunks(windowDays, maxPerWindow), embed, warnings, ct);
    }

    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks(int windowDays, int maxPerWindow)
    {
        var devices = new StringBuilder();
        devices.AppendLine(
            $"Device limit. An account may keep {TrustedDeviceService.DefaultMaxDevices} trusted devices at once by default. " +
            $"Support can raise an individual account's limit to at most {TrustedDeviceService.MaxAllowedDevicesOverride}; " +
            "there is no self-service way to raise it.");
        devices.AppendLine(
            "A device is remembered after it has been approved once, so a learner who uses the same phone and the same " +
            "laptop is not asked again.");
        devices.AppendLine(
            "Signing in on a new device beyond the limit does not lock the account. The oldest trusted device is retired " +
            "to make room, and the new one takes its place.");
        devices.AppendLine(
            $"Device replacements are rate limited: at most {maxPerWindow} approved changes in a rolling {windowDays}-day window. " +
            "Beyond that the learner waits for the window to roll rather than being permanently blocked; support can help " +
            "if there is a genuine reason such as a lost or stolen phone.");
        devices.AppendLine(
            "Never tell a learner how many devices they currently have registered unless that has been provided for this " +
            "conversation. Point them at Account settings, where their own device list is shown.");

        var otp = new StringBuilder();
        otp.AppendLine(
            "Signing in on an unrecognised device asks for a one-time code sent to the account's email address. " +
            "This is expected behaviour, not a fault, and it is how a shared or resold account is prevented.");
        otp.AppendLine("If the code does not arrive, in order: check the spam or junk folder; confirm the email address on the " +
            "account is the one being checked; request a new code rather than reusing an old one (only the newest code works); " +
            "and if it still does not arrive, contact support with the account email and the approximate time of the attempt.");
        otp.AppendLine(
            "Codes are short-lived and single-use. A code that has been used, superseded by a newer request, or left too long " +
            "will be rejected — requesting a fresh one is the fix.");
        otp.AppendLine(
            "There is a short cooldown between code requests. Repeatedly pressing resend does not make a code arrive faster.");
        otp.AppendLine(
            "Never ask a learner to tell you their one-time code, and never repeat one back. If a learner pastes a code into " +
            "the conversation, tell them not to share codes with anyone including support, and that they should request a new one.");

        var expiry = new StringBuilder();
        expiry.AppendLine(
            "Course access is time limited. When a package expires, its modules close and the account falls back to free " +
            "access; the account itself is not deleted and progress, attempts and results remain.");
        expiry.AppendLine(
            "Renewing or buying a new package restores access. Subscriptions and packages, with renewal dates, are shown in " +
            "the learner's own account area.");
        expiry.AppendLine(
            "Never state whether a specific learner's access is active, expired or about to expire unless that has been " +
            "provided for this conversation, and never promise an extension, a refund or a discount. Those are decisions for " +
            "the team, not for the assistant.");

        var packages = new StringBuilder();
        packages.AppendLine(
            "Packages differ in what they include, and the difference is real rather than cosmetic: a Full Course and a Crash " +
            "Course are separate content sets, and material from one is not shown inside the other.");
        packages.AppendLine(
            "Some parts of the product are separate modules that a package may or may not include — mock exams, the vocabulary " +
            "Recalls sets, the materials library, the video library, and the AI Learning Companion itself.");
        packages.AppendLine(
            "Course content is also profession-specific. A learner registered for medicine is taught with medicine material; " +
            "another profession's material is a different product and is not shown to them.");
        packages.AppendLine(
            "If something a learner asks for is not in their package, say plainly what it is and where to see the options. " +
            "Do not describe any other route to the content, and do not speculate about what their package includes — " +
            "their account is the authority on that.");

        var credits = new StringBuilder();
        credits.AppendLine("AI Credits pay for AI marking. The current costs are:");
        credits.AppendLine($"- One Writing letter marked by AI: {AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity} credits.");
        credits.AppendLine($"- One Speaking role-play card marked by AI: {AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity} credits.");
        credits.AppendLine(
            $"- A full two-card Speaking exam: {AiGradingCreditCost.SpeakingExam * AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity} credits, " +
            "because it is two cards and each card is charged as it is revealed.");
        credits.AppendLine($"- One Reading paper: {AiGradingCreditCost.ReadingExam} credit.");
        credits.AppendLine($"- One Listening paper: {AiGradingCreditCost.ListeningExam} credit.");
        credits.AppendLine(
            "Always state the exact cost before starting anything chargeable and get the learner's agreement first. " +
            "If a chargeable action fails for a technical reason, the credits are returned — say so rather than leaving " +
            "the learner to ask.");
        credits.AppendLine(
            "Credits are bought in packages and spending is itemised in the learner's AI usage view. Practising without AI " +
            "marking does not cost credits.");

        var problems = new StringBuilder();
        problems.AppendLine(
            "If a learner believes an answer was marked wrongly, take it seriously and do not argue the mark. Ask which " +
            "question and what they answered, explain the reasoning behind the expected answer if the approved material " +
            "covers it, and tell them it can be reported for review from the question itself.");
        problems.AppendLine(
            "Never overturn, re-grade or promise to change a score, and never claim an official OET result. AI marking is " +
            "practice feedback and is not an OET result.");
        problems.AppendLine(
            "Escalate to the support team for: payment, refunds and invoices; access that is wrong after a purchase; " +
            "lost account access; a suspected marking error the learner wants formally reviewed; and anything about a real " +
            "OET booking, which is handled by OET itself and not by this platform.");
        problems.AppendLine(
            "Say when something is a support matter rather than attempting it. Do not claim to have opened a ticket, " +
            "issued a refund, changed a package or contacted anyone — the assistant cannot do those things.");

        return
        [
            new("Support — device limits and trusted devices", devices.ToString().Trim()),
            new("Support — sign-in codes (OTP) and email verification", otp.ToString().Trim()),
            new("Support — package expiry and renewal", expiry.ToString().Trim()),
            new("Support — what packages include, and package isolation", packages.ToString().Trim()),
            new("Support — AI Credits and what each action costs", credits.ToString().Trim()),
            new("Support — wrong answers, marking disputes and escalation", problems.ToString().Trim()),
        ];
    }
}
