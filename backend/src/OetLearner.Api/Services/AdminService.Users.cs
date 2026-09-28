using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services;

public partial class AdminService
{

    // ════════════════════════════════════════════
    //  User Ops
    // ════════════════════════════════════════════

    public async Task<object> GetUserListAsync(string? role, string? status, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        var clampedPageSize = Math.Clamp(pageSize, 1, 500);

        var rows = new List<AdminUserListRow>();

        rows.AddRange(await db.Users.AsNoTracking()
            .Select(u => new AdminUserListRow(
                u.Id,
                u.DisplayName,
                u.Email,
                u.Role,
                u.AccountStatus ?? ActiveUserStatus,
                u.AuthAccountId,
                u.LastActiveAt,
                u.CreatedAt,
                u.ActiveProfessionId))
            .ToListAsync(ct));

        var expertProjections = await db.ExpertUsers.AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.DisplayName,
                e.Email,
                e.Role,
                e.IsActive,
                e.AuthAccountId,
                e.CreatedAt,
                e.SpecialtiesJson,
            })
            .ToListAsync(ct);
        rows.AddRange(expertProjections.Select(e => new AdminUserListRow(
            e.Id,
            e.DisplayName,
            e.Email,
            e.Role,
            e.IsActive ? ActiveUserStatus : SuspendedUserStatus,
            e.AuthAccountId,
            e.CreatedAt,
            e.CreatedAt,
            JsonSupport.Deserialize(e.SpecialtiesJson, Array.Empty<string>()).FirstOrDefault())));

        rows.AddRange(await db.ApplicationUserAccounts.AsNoTracking()
            .Where(a => a.Role == ApplicationUserRoles.Admin)
            .Select(a => new AdminUserListRow(
                a.Id,
                "Admin Account",
                a.Email,
                a.Role,
                ActiveUserStatus,
                a.Id,
                a.LastLoginAt ?? a.UpdatedAt,
                a.CreatedAt,
                null))
            .ToListAsync(ct));

        var referencedAuthIds = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.AuthAccountId))
            .Select(r => r.AuthAccountId!)
            .Distinct()
            .ToList();
        var authAccountFlags = await db.ApplicationUserAccounts.AsNoTracking()
            .Where(a => referencedAuthIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                IsDeleted = a.DeletedAt != null,
                MfaEnabled = a.AuthenticatorEnabledAt != null,
                LockoutUntil = a.LockoutUntil,
            })
            .ToListAsync(ct);
        var authFlagsLookup = authAccountFlags.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var nowUtc = timeProvider.GetUtcNow();

        rows = rows.Select(row => new AdminUserListRow(
            row.Id,
            row.Name,
            row.Email,
            row.Role,
            ResolveUserStatus(row.Status, !string.IsNullOrWhiteSpace(row.AuthAccountId) && authFlagsLookup.TryGetValue(row.AuthAccountId!, out var f) && f.IsDeleted),
            row.AuthAccountId,
            row.LastLogin,
            row.CreatedAt,
            row.Profession)).ToList();

        IEnumerable<AdminUserListRow> filtered = rows;

        if (!string.IsNullOrWhiteSpace(role) && role != "all")
            filtered = filtered.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(u =>
                u.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var summaryBase = filtered.ToList();

        filtered = string.IsNullOrWhiteSpace(status) || status == "all"
            ? summaryBase.Where(u => u.Status != DeletedUserStatus)
            : summaryBase.Where(u => u.Status == status);

        var materialized = filtered
            .OrderByDescending(u => u.LastLogin ?? DateTimeOffset.MinValue)
            .ToList();

        var summary = new
        {
            total = summaryBase.Count,
            active = summaryBase.Count(u => u.Status == ActiveUserStatus),
            suspended = summaryBase.Count(u => u.Status == SuspendedUserStatus),
            deleted = summaryBase.Count(u => u.Status == DeletedUserStatus),
            mfaEnabled = summaryBase.Count(u =>
                !string.IsNullOrWhiteSpace(u.AuthAccountId)
                && authFlagsLookup.TryGetValue(u.AuthAccountId!, out var f)
                && f.MfaEnabled),
            lockedOut = summaryBase.Count(u =>
                !string.IsNullOrWhiteSpace(u.AuthAccountId)
                && authFlagsLookup.TryGetValue(u.AuthAccountId!, out var f)
                && f.LockoutUntil != null
                && f.LockoutUntil > nowUtc),
        };

        var items = materialized
            .Skip((page - 1) * clampedPageSize)
            .Take(clampedPageSize)
            .Select(u =>
            {
                var hasAuth = !string.IsNullOrWhiteSpace(u.AuthAccountId)
                    && authFlagsLookup.TryGetValue(u.AuthAccountId!, out _);
                var flags = hasAuth ? authFlagsLookup[u.AuthAccountId!] : null;
                return new
                {
                    id = u.Id,
                    name = u.Name,
                    email = u.Email,
                    role = u.Role,
                    status = u.Status,
                    lastLogin = u.LastLogin,
                    createdAt = u.CreatedAt,
                    profession = u.Profession,
                    mfaEnabled = flags?.MfaEnabled ?? false,
                    lockedOut = flags?.LockoutUntil != null && flags.LockoutUntil > nowUtc,
                };
            })
            .ToList();

        return new { total = materialized.Count, page, pageSize = clampedPageSize, items, summary };
    }

    public async Task<object> GetUserDetailAsync(string userId, CancellationToken ct)
    {
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var authAccount = learner.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == learner.AuthAccountId, ct)
                : null;
            var status = ResolveUserStatus(learner.AccountStatus, authAccount?.DeletedAt is not null);
            var attemptCount = await db.Attempts.CountAsync(a => a.UserId == userId, ct);
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
            var security = await BuildSecuritySnapshotAsync(authAccount, learner.Email, ct);
            var subscription = await BuildLearnerSubscriptionAsync(learner.Id, ct);
            var recentActivity = await GetRecentUserActivityAsync(learner.Id, learner.AuthAccountId, ct);
            var registration = await db.LearnerRegistrationProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.LearnerUserId == learner.Id, ct);
            var profileProfessionId = registration?.ProfessionId ?? learner.ActiveProfessionId;
            var profileCountryTarget = TargetCountryOptions.TryCanonicalize(registration?.CountryTarget, out var canonicalCountryTarget)
                ? canonicalCountryTarget
                : registration?.CountryTarget;
            return new
            {
                learner.Id,
                name = learner.DisplayName,
                learner.Email,
                role = learner.Role,
                status,
                lastLogin = authAccount?.LastLoginAt ?? learner.LastActiveAt,
                tasksCompleted = attemptCount,
                creditBalance = wallet?.CreditBalance ?? 0,
                profession = profileProfessionId,
                authAccountId = learner.AuthAccountId,
                createdAt = learner.CreatedAt,
                // ── Full editable profile (registration data captured at signup) ──
                displayName = learner.DisplayName,
                firstName = registration?.FirstName,
                lastName = registration?.LastName,
                mobileNumber = registration?.MobileNumber,
                professionId = profileProfessionId,
                examTypeId = registration?.ExamTypeId ?? learner.ActiveExamTypeCode,
                countryTarget = profileCountryTarget,
                timezone = learner.Timezone,
                locale = learner.Locale,
                marketingOptIn = registration?.MarketingOptIn,
                agreeToTerms = registration?.AgreeToTerms,
                agreeToPrivacy = registration?.AgreeToPrivacy,
                // Read-only acquisition attribution (display only; never edited here).
                attribution = registration is null ? null : new
                {
                    registration.UtmSource,
                    registration.UtmMedium,
                    registration.UtmCampaign,
                    registration.UtmTerm,
                    registration.UtmContent,
                    registration.ReferrerUrl,
                    registration.LandingPath,
                },
                security,
                subscription,
                recentActivity,
                availableActions = new
                {
                    canSuspend = status is not DeletedUserStatus,
                    canDelete = !string.Equals(learner.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canRestore = status is DeletedUserStatus && !string.Equals(learner.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canAdjustCredits = status is not DeletedUserStatus,
                    canTriggerPasswordReset = learner.AuthAccountId is not null && status is not DeletedUserStatus,
                    canVerifyEmail = authAccount is not null
                        && status is ActiveUserStatus
                        && authAccount.EmailVerifiedAt is null,
                    canForceSignOut = (security?.ActiveSessionCount ?? 0) > 0,
                    canUnlock = security?.LockedOut ?? false,
                    canResendInvite = CanResendInvite(authAccount, status)
                }
            };
        }

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var authAccount = expert.AuthAccountId is not null
                ? await db.ApplicationUserAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == expert.AuthAccountId, ct)
                : null;
            var status = ResolveUserStatus(expert.IsActive ? ActiveUserStatus : SuspendedUserStatus, authAccount?.DeletedAt is not null);
            var reviewCount = await db.ExpertReviewAssignments.CountAsync(a => a.AssignedReviewerId == userId, ct);
            var security = await BuildSecuritySnapshotAsync(authAccount, expert.Email, ct);
            var recentActivity = await GetRecentUserActivityAsync(expert.Id, expert.AuthAccountId, ct);
            return new
            {
                expert.Id,
                name = expert.DisplayName,
                expert.Email,
                role = expert.Role,
                status,
                lastLogin = authAccount?.LastLoginAt ?? expert.CreatedAt,
                tasksGraded = reviewCount,
                specialties = JsonSupport.Deserialize(expert.SpecialtiesJson, Array.Empty<string>()),
                authAccountId = expert.AuthAccountId,
                createdAt = expert.CreatedAt,
                // ── Editable profile (tutor) ──
                displayName = expert.DisplayName,
                timezone = expert.Timezone,
                security,
                subscription = (object?)null,
                recentActivity,
                availableActions = new
                {
                    canSuspend = status is not DeletedUserStatus,
                    canDelete = !string.Equals(expert.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canRestore = status is DeletedUserStatus && !string.Equals(expert.Role, ApplicationUserRoles.Admin, StringComparison.Ordinal),
                    canAdjustCredits = false,
                    canTriggerPasswordReset = expert.AuthAccountId is not null && status is not DeletedUserStatus,
                    canVerifyEmail = authAccount is not null
                        && status is ActiveUserStatus
                        && authAccount.EmailVerifiedAt is null,
                    canForceSignOut = (security?.ActiveSessionCount ?? 0) > 0,
                    canUnlock = security?.LockedOut ?? false,
                    canResendInvite = CanResendInvite(authAccount, status)
                }
            };
        }

        var adminAccount = await db.ApplicationUserAccounts.FirstOrDefaultAsync(
            a => a.Id == userId && a.Role == ApplicationUserRoles.Admin,
            ct);
        if (adminAccount is not null)
        {
            var status = ResolveUserStatus(ActiveUserStatus, adminAccount.DeletedAt is not null);
            var security = await BuildSecuritySnapshotAsync(adminAccount, adminAccount.Email, ct);
            var recentActivity = await GetRecentUserActivityAsync(adminAccount.Id, adminAccount.Id, ct);
            return new
            {
                adminAccount.Id,
                name = "Admin Account",
                adminAccount.Email,
                role = adminAccount.Role,
                status,
                lastLogin = adminAccount.LastLoginAt ?? adminAccount.UpdatedAt,
                authAccountId = adminAccount.Id,
                createdAt = adminAccount.CreatedAt,
                security,
                subscription = (object?)null,
                recentActivity,
                availableActions = new
                {
                    canSuspend = false,
                    canDelete = false,
                    canRestore = false,
                    canAdjustCredits = false,
                    canTriggerPasswordReset = status is not DeletedUserStatus,
                    canVerifyEmail = status is not DeletedUserStatus && adminAccount.EmailVerifiedAt is null,
                    canForceSignOut = (security?.ActiveSessionCount ?? 0) > 0,
                    canUnlock = security?.LockedOut ?? false,
                    canResendInvite = CanResendInvite(adminAccount, status)
                }
            };
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    public async Task<object> InviteUserAsync(
        string adminId,
        string adminName,
        AdminUserInviteRequest request,
        CancellationToken ct)
    {
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (role is not (ApplicationUserRoles.Learner or ApplicationUserRoles.Expert or ApplicationUserRoles.Admin))
        {
            throw ApiException.Validation("invalid_role", "Role must be learner, expert, or admin.");
        }

        var email = AuthEmailAddress.TrimAndValidateOrThrow(request.Email);
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.ApplicationUserAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail, ct))
        {
            throw ApiException.Conflict("email_already_exists", "An account with this email already exists.");
        }

        var professionId = string.IsNullOrWhiteSpace(request.ProfessionId)
            ? null
            : request.ProfessionId.Trim();

        var specialties = (request.Specialties is { Count: > 0 }
            ? request.Specialties.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct()
            : (professionId is not null ? new[] { professionId } : Array.Empty<string>())).ToArray();

        if (role == ApplicationUserRoles.Learner)
        {
            if (string.IsNullOrWhiteSpace(professionId))
            {
                throw ApiException.Validation("profession_required", "A profession is required for learner invitations.");
            }

            var professionExists = await db.Professions.AsNoTracking().AnyAsync(
                p => p.Id == professionId && p.Status == ActiveUserStatus,
                ct);
            if (!professionExists)
            {
                throw ApiException.Validation("invalid_profession", "The selected profession is not active or does not exist.");
            }
        }
        else if (role == ApplicationUserRoles.Expert)
        {
            if (specialties.Length == 0)
            {
                throw ApiException.Validation("specialties_required", "At least one specialty is required for tutor invitations.");
            }
        }
        else if (professionId is not null)
        {
            throw ApiException.Validation("profession_not_allowed", "Admin invitations cannot be assigned a profession.");
        }

        var displayName = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim();
        var now = timeProvider.GetUtcNow();
        var authAccountId = GenerateAccountId(role);
        var tempPassword = $"Tmp!{Guid.NewGuid():N}";

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var authAccount = new ApplicationUserAccount
        {
            Id = authAccountId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = role,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, tempPassword);
        db.ApplicationUserAccounts.Add(authAccount);

        string userId;
        switch (role)
        {
            case ApplicationUserRoles.Learner:
                userId = GenerateDomainId("usr");
                db.Users.Add(new LearnerUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Learner,
                    DisplayName = displayName,
                    Email = email,
                    Timezone = "UTC",
                    Locale = "en-AU",
                    ActiveProfessionId = professionId,
                    CreatedAt = now,
                    LastActiveAt = now,
                    AccountStatus = "active"
                });
                db.Wallets.Add(new Wallet
                {
                    Id = GenerateDomainId("wallet"),
                    UserId = userId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now
                });
                break;
            case ApplicationUserRoles.Expert:
                userId = GenerateDomainId("expert");
                db.ExpertUsers.Add(new ExpertUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Expert,
                    DisplayName = displayName,
                    Email = email,
                    SpecialtiesJson = JsonSupport.Serialize(specialties),
                    Timezone = "UTC",
                    IsActive = true,
                    CreatedAt = now
                });
                break;
            default:
                userId = authAccountId;
                break;
        }

        await db.SaveChangesAsync(ct);

        // Email send gets its own 10-second deadline so a slow/misconfigured
        // email provider never blocks the invite response. If it fails, the
        // admin receives the temporary password directly as a fallback.
        OtpChallengeResponse? inviteChallenge = null;
        using var emailCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        emailCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            inviteChallenge = await emailOtpService.RequestPasswordResetOtpAsync(email, emailCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Email provider timed out; admin receives temp password instead.
        }
        catch (Exception)
        {
            // Email send failed; admin receives temp password instead.
        }

        await LogAuditAsync(adminId, adminName, "Invited User", "User", userId, $"Invited {role}: {email}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new
        {
            id = userId,
            email,
            role,
            temporaryPassword = inviteChallenge is null ? tempPassword : (string?)null,
            invitation = inviteChallenge is null ? null : new
            {
                purpose = inviteChallenge.Purpose,
                deliveryChannel = inviteChallenge.DeliveryChannel,
                destinationHint = inviteChallenge.DestinationHint,
                expiresAt = inviteChallenge.ExpiresAt,
                retryAfterSeconds = inviteChallenge.RetryAfterSeconds
            }
        };
    }

    // ── Manual "Add User" (create with password OR invite) ───────────────────

    /// <summary>
    /// Admin manual user creation. When a password is supplied the account is created
    /// ready-to-use (hashed, email pre-verified, no OTP email); otherwise the invite/OTP
    /// email flow runs exactly like <see cref="InviteUserAsync"/>. Phone number, when
    /// provided for a learner, is persisted to the registration profile. Allocation of
    /// packages/modules/expiry is done separately by the access-allocation endpoints.
    /// </summary>
    public async Task<object> CreateUserAsync(
        string adminId,
        string adminName,
        AdminUserCreateRequest request,
        CancellationToken ct)
    {
        var role = (request.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (role is not (ApplicationUserRoles.Learner or ApplicationUserRoles.Expert or ApplicationUserRoles.Admin))
        {
            throw ApiException.Validation("invalid_role", "Role must be learner, expert, or admin.");
        }

        var email = AuthEmailAddress.TrimAndValidateOrThrow(request.Email);
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.ApplicationUserAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail, ct))
        {
            throw ApiException.Conflict("email_already_exists", "An account with this email already exists.");
        }

        var professionId = string.IsNullOrWhiteSpace(request.ProfessionId)
            ? null
            : request.ProfessionId.Trim();
        if (role is ApplicationUserRoles.Learner or ApplicationUserRoles.Expert)
        {
            if (string.IsNullOrWhiteSpace(professionId))
            {
                throw ApiException.Validation("profession_required", "A profession is required for learner and expert accounts.");
            }

            var professionExists = await db.Professions.AsNoTracking().AnyAsync(
                p => p.Id == professionId && p.Status == ActiveUserStatus,
                ct);
            if (!professionExists)
            {
                throw ApiException.Validation("invalid_profession", "The selected profession is not active or does not exist.");
            }
        }
        else if (professionId is not null)
        {
            throw ApiException.Validation("profession_not_allowed", "Admin accounts cannot be assigned a profession.");
        }

        if (role == ApplicationUserRoles.Learner)
        {
            if (request.TargetExamDate is null)
            {
                throw ApiException.Validation("target_exam_date_required", "A target exam date is required for learner accounts.");
            }
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (request.TargetExamDate.Value < today)
            {
                throw ApiException.Validation("target_exam_date_in_past", "The target exam date must be today or later.");
            }
        }

        var usePassword = !string.IsNullOrWhiteSpace(request.Password);
        if (usePassword && request.Password!.Trim().Length < 8)
        {
            throw ApiException.Validation("weak_password", "Password must be at least 8 characters.");
        }

        var displayName = string.IsNullOrWhiteSpace(request.Name) ? email : request.Name.Trim();
        var now = timeProvider.GetUtcNow();
        var authAccountId = GenerateAccountId(role);
        var initialPassword = usePassword ? request.Password!.Trim() : $"Tmp!{Guid.NewGuid():N}";

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var authAccount = new ApplicationUserAccount
        {
            Id = authAccountId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = role,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, initialPassword);
        db.ApplicationUserAccounts.Add(authAccount);

        string userId;
        switch (role)
        {
            case ApplicationUserRoles.Learner:
                userId = GenerateDomainId("usr");
                db.Users.Add(new LearnerUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Learner,
                    DisplayName = displayName,
                    Email = email,
                    Timezone = "UTC",
                    Locale = "en-AU",
                    ActiveProfessionId = professionId,
                    CreatedAt = now,
                    LastActiveAt = now,
                    AccountStatus = "active"
                });
                db.Wallets.Add(new Wallet
                {
                    Id = GenerateDomainId("wallet"),
                    UserId = userId,
                    CreditBalance = 0,
                    LedgerSummaryJson = "[]",
                    LastUpdatedAt = now
                });
                break;
            case ApplicationUserRoles.Expert:
                userId = GenerateDomainId("expert");
                db.ExpertUsers.Add(new ExpertUser
                {
                    Id = userId,
                    AuthAccountId = authAccountId,
                    Role = ApplicationUserRoles.Expert,
                    DisplayName = displayName,
                    Email = email,
                    SpecialtiesJson = JsonSupport.Serialize(new[] { professionId! }),
                    Timezone = "UTC",
                    IsActive = true,
                    CreatedAt = now
                });
                break;
            default:
                userId = authAccountId;
                break;
        }

        await db.SaveChangesAsync(ct);

        // Invite/OTP email only when NOT setting a password directly. Own 10s deadline
        // so a slow provider never blocks the response (admin gets the temp password back).
        OtpChallengeResponse? inviteChallenge = null;
        if (!usePassword)
        {
            using var emailCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            emailCts.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                inviteChallenge = await emailOtpService.RequestPasswordResetOtpAsync(email, emailCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
            }
        }

        await LogAuditAsync(adminId, adminName, "Created User", "User", userId,
            $"Created {role}: {email} ({(usePassword ? "password set" : "invite email")})", ct);
        await CommitIfOwnedAsync(tx, ct);

        // Persist phone, exam date (and split name) into the learner registration
        // profile via the existing profile updater, which create-fills required
        // defaults. Best-effort: a profile failure must not undo an otherwise-
        // created account. Runs for every learner (not only when a phone number
        // was given) so the mandatory TargetExamDate always lands.
        if (role == ApplicationUserRoles.Learner)
        {
            var (firstName, lastName) = SplitDisplayName(displayName);
            try
            {
                await UpdateUserProfileAsync(adminId, adminName, userId, new AdminUserProfileUpdateRequest(
                    DisplayName: null,
                    FirstName: firstName,
                    LastName: lastName,
                    MobileNumber: string.IsNullOrWhiteSpace(request.MobileNumber) ? null : request.MobileNumber.Trim(),
                    ProfessionId: professionId,
                    ExamTypeId: null,
                    // UpdateUserProfileAsync's registration-profile creation branch
                    // requires a non-null CountryTarget — without one the whole
                    // profile row (and this TargetExamDate) silently fails to be
                    // created (caught below as non-fatal). No country field exists
                    // on the admin Add-User form, so default it the same way the
                    // rest of the codebase does when nothing was registered.
                    CountryTarget: "Australia",
                    TargetExamDate: request.TargetExamDate,
                    Timezone: null,
                    Locale: null,
                    MarketingOptIn: null,
                    AgreeToTerms: null,
                    AgreeToPrivacy: null,
                    Specialties: null,
                    Reason: "admin_add_user"), ct);
            }
            catch (Exception)
            {
                // Non-fatal: account exists; admin can edit the profile afterwards.
            }
        }

        return new
        {
            id = userId,
            email,
            role,
            temporaryPassword = (!usePassword && inviteChallenge is null) ? initialPassword : (string?)null,
            invitation = inviteChallenge is null ? null : new
            {
                purpose = inviteChallenge.Purpose,
                deliveryChannel = inviteChallenge.DeliveryChannel,
                destinationHint = inviteChallenge.DestinationHint,
                expiresAt = inviteChallenge.ExpiresAt,
                retryAfterSeconds = inviteChallenge.RetryAfterSeconds
            }
        };
    }

    private static (string FirstName, string? LastName) SplitDisplayName(string displayName)
    {
        var trimmed = displayName.Trim();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex <= 0)
        {
            return (trimmed, null);
        }
        return (trimmed[..spaceIndex].Trim(), trimmed[(spaceIndex + 1)..].Trim());
    }

    public async Task<object> BulkImportUsersAsync(
        string adminId,
        string adminName,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.Validation("file_required", "A CSV file is required.");
        }

        if (file.Length > MaxImportFileBytes)
        {
            throw ApiException.Validation("file_too_large", "CSV file must be under 5 MB.");
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!contentType.Contains("csv", StringComparison.Ordinal) && !contentType.Contains("text/plain", StringComparison.Ordinal)
            && !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("invalid_file_type", "Only CSV files are accepted.");
        }

        List<CsvUserRow> rows;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            rows = ParseCsvRows(await reader.ReadToEndAsync(ct));
        }

        if (rows.Count == 0)
        {
            throw ApiException.Validation("empty_csv", "CSV contains no data rows.");
        }

        if (rows.Count > MaxImportRows)
        {
            throw ApiException.Validation("too_many_rows", $"CSV must not exceed {MaxImportRows} rows. Found {rows.Count}.");
        }

        var errors = new List<object>();
        var created = 0;
        var skipped = 0;
        var now = timeProvider.GetUtcNow();

        // Pre-fetch existing emails for duplicate detection
        var importEmails = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .Select(r => r.Email!.Trim().ToUpperInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingEmailsList = await db.ApplicationUserAccounts
            .Where(a => importEmails.Contains(a.NormalizedEmail))
            .Select(a => a.NormalizedEmail)
            .ToListAsync(ct);
        var existingEmails = new HashSet<string>(existingEmailsList, StringComparer.OrdinalIgnoreCase);

        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2; // +2 because row 1 is header, data starts at 2

            // Validate email
            var rawEmail = (row.Email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawEmail) || !EmailValidator.IsValid(rawEmail))
            {
                errors.Add(new { row = rowNumber, email = rawEmail, error = "Invalid email format." });
                continue;
            }

            var normalizedEmail = rawEmail.ToUpperInvariant();

            // Skip duplicates within the CSV itself
            if (!seenEmails.Add(normalizedEmail))
            {
                skipped++;
                continue;
            }

            // Skip existing accounts
            if (existingEmails.Contains(normalizedEmail))
            {
                skipped++;
                continue;
            }

            // Validate role
            var role = (row.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (!ValidImportRoles.Contains(role))
            {
                errors.Add(new { row = rowNumber, email = rawEmail, error = $"Invalid role '{row.Role}'. Must be learner, expert, or admin." });
                continue;
            }

            // Sanitize name fields
            var firstName = SanitizeField(row.FirstName, 100);
            var lastName = SanitizeField(row.LastName, 100);
            var displayName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                ? rawEmail
                : $"{firstName} {lastName}".Trim();
            var profession = SanitizeField(row.Profession, 100);

            var authAccountId = GenerateAccountId(role);
            var tempPassword = $"Tmp!{Guid.NewGuid():N}";

            var authAccount = new ApplicationUserAccount
            {
                Id = authAccountId,
                Email = rawEmail,
                NormalizedEmail = normalizedEmail,
                PasswordHash = string.Empty,
                Role = role,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, tempPassword);
            db.ApplicationUserAccounts.Add(authAccount);

            switch (role)
            {
                case ApplicationUserRoles.Learner:
                    var learnerId = GenerateDomainId("usr");
                    db.Users.Add(new LearnerUser
                    {
                        Id = learnerId,
                        AuthAccountId = authAccountId,
                        Role = ApplicationUserRoles.Learner,
                        DisplayName = displayName,
                        Email = rawEmail,
                        Timezone = "UTC",
                        Locale = "en-AU",
                        ActiveProfessionId = string.IsNullOrWhiteSpace(profession) ? null : profession,
                        CreatedAt = now,
                        LastActiveAt = now,
                        AccountStatus = "active"
                    });
                    db.Wallets.Add(new Wallet
                    {
                        Id = GenerateDomainId("wallet"),
                        UserId = learnerId,
                        CreditBalance = 0,
                        LedgerSummaryJson = "[]",
                        LastUpdatedAt = now
                    });
                    break;
                case ApplicationUserRoles.Expert:
                    db.ExpertUsers.Add(new ExpertUser
                    {
                        Id = GenerateDomainId("expert"),
                        AuthAccountId = authAccountId,
                        Role = ApplicationUserRoles.Expert,
                        DisplayName = displayName,
                        Email = rawEmail,
                        SpecialtiesJson = JsonSupport.Serialize(string.IsNullOrWhiteSpace(profession) ? Array.Empty<string>() : new[] { profession }),
                        Timezone = "UTC",
                        IsActive = true,
                        CreatedAt = now
                    });
                    break;
                default:
                    // Admin — no separate domain entity
                    break;
            }

            created++;
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Bulk Import Users", "User", "bulk",
            $"Imported {created} users, skipped {skipped}, errors {errors.Count}", ct);
        await CommitIfOwnedAsync(tx, ct);

        return new
        {
            total = rows.Count,
            created,
            skipped,
            errors
        };
    }

    private static List<CsvUserRow> ParseCsvRows(string csvContent)
    {
        var rows = new List<CsvUserRow>();
        using var reader = new StringReader(csvContent);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return rows;

        var headers = ParseCsvLine(headerLine)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToArray();

        var emailIdx = Array.IndexOf(headers, "email");
        var firstNameIdx = Array.IndexOf(headers, "firstname");
        var lastNameIdx = Array.IndexOf(headers, "lastname");
        var roleIdx = Array.IndexOf(headers, "role");
        var professionIdx = Array.IndexOf(headers, "profession");

        if (emailIdx < 0)
        {
            throw ApiException.Validation("missing_email_column", "CSV must have an 'email' column header.");
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);

            rows.Add(new CsvUserRow
            {
                Email = GetField(fields, emailIdx),
                FirstName = GetField(fields, firstNameIdx),
                LastName = GetField(fields, lastNameIdx),
                Role = GetField(fields, roleIdx),
                Profession = GetField(fields, professionIdx),
            });
        }

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string? GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : null;

    private static string SanitizeField(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Strip control characters and trim
        var sanitized = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    private sealed class CsvUserRow
    {
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? Role { get; init; }
        public string? Profession { get; init; }
    }

    public async Task<object> UpdateUserProfileAsync(string adminId, string adminName,
        string userId, AdminUserProfileUpdateRequest request, CancellationToken ct)
    {
        // Email is intentionally NOT part of AdminUserProfileUpdateRequest. It is the immutable
        // auth identity and can never be changed here, regardless of payload.
        var changes = new List<string>();

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static IReadOnlyList<string> ReadCatalogList(string json) => JsonSupport.Deserialize(json, Array.Empty<string>());
        static bool HasRegistrationPayload(AdminUserProfileUpdateRequest payload)
            => payload.FirstName is not null
               || payload.LastName is not null
               || payload.MobileNumber is not null
               || payload.ProfessionId is not null
               || payload.ExamTypeId is not null
               || payload.CountryTarget is not null
               || payload.TargetExamDate is not null
               || payload.MarketingOptIn is not null
               || payload.AgreeToTerms is not null
               || payload.AgreeToPrivacy is not null;

        async Task<SignupProfessionCatalog?> TryResolveProfessionAsync(string? rawProfessionId)
        {
            var value = Clean(rawProfessionId);
            if (value is null) return null;
            var normalized = value.ToLowerInvariant();
            return await db.SignupProfessionCatalog.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsActive
                    && (p.Id.ToLower() == normalized || p.Label.ToLower() == normalized), ct);
        }

        async Task<SignupProfessionCatalog> ResolveProfessionAsync(string rawProfessionId)
            => await TryResolveProfessionAsync(rawProfessionId)
               ?? throw ApiException.Validation("invalid_profession", $"Unknown or inactive profession '{rawProfessionId}'.");

        async Task<SignupExamTypeCatalog?> TryResolveExamTypeAsync(string? rawExamTypeId)
        {
            var value = Clean(rawExamTypeId);
            if (value is null) return null;
            var normalized = value.ToLowerInvariant();
            return await db.SignupExamTypeCatalog.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IsActive
                    && (e.Id.ToLower() == normalized || e.Code.ToLower() == normalized || e.Label.ToLower() == normalized), ct);
        }

        async Task<SignupExamTypeCatalog> ResolveExamTypeAsync(string rawExamTypeId)
            => await TryResolveExamTypeAsync(rawExamTypeId)
               ?? throw ApiException.Validation("invalid_exam_type", $"Unknown or inactive exam type '{rawExamTypeId}'.");

        async Task<SignupProfessionCatalog> ResolveDefaultProfessionAsync(string? preferredProfessionId)
            => await TryResolveProfessionAsync(preferredProfessionId)
               ?? await db.SignupProfessionCatalog.AsNoTracking()
                   .Where(p => p.IsActive)
                   .OrderBy(p => p.SortOrder)
                   .FirstOrDefaultAsync(ct)
               ?? throw ApiException.Validation("invalid_profession", "No active signup profession is available.");

        async Task<SignupExamTypeCatalog> ResolveDefaultExamTypeAsync(string? preferredExamTypeId, SignupProfessionCatalog profession)
        {
            var preferred = await TryResolveExamTypeAsync(preferredExamTypeId);
            var professionExamTypeIds = ReadCatalogList(profession.ExamTypeIdsJson);
            if (preferred is not null
                && (professionExamTypeIds.Count == 0 || professionExamTypeIds.Contains(preferred.Id, StringComparer.Ordinal)))
            {
                return preferred;
            }

            var allowed = professionExamTypeIds.Count > 0 ? professionExamTypeIds : Array.Empty<string>();
            var query = db.SignupExamTypeCatalog.AsNoTracking().Where(e => e.IsActive);
            if (allowed.Count > 0)
            {
                query = query.Where(e => allowed.Contains(e.Id));
            }

            return await query.OrderBy(e => e.SortOrder).FirstOrDefaultAsync(ct)
                   ?? throw ApiException.Validation("invalid_exam_type", "No active signup exam type is available for that profession.");
        }

        static void ValidateProfessionExamMatch(SignupProfessionCatalog profession, SignupExamTypeCatalog examType)
        {
            var professionExamTypeIds = ReadCatalogList(profession.ExamTypeIdsJson);
            if (professionExamTypeIds.Count > 0 && !professionExamTypeIds.Contains(examType.Id, StringComparer.Ordinal))
            {
                throw ApiException.Validation("profession_exam_mismatch", "The selected profession is not available for that exam.");
            }
        }

        static void ValidateProfessionCountryMatch(SignupProfessionCatalog profession, string countryTarget)
        {
            var professionCountryTargets = ReadCatalogList(profession.CountryTargetsJson);
            if (professionCountryTargets.Count > 0 && !professionCountryTargets.Contains(countryTarget, StringComparer.OrdinalIgnoreCase))
            {
                throw ApiException.Validation("profession_country_mismatch", "The selected target country is not available for that profession.");
            }
        }

        // ── Learner ──
        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is not null)
        {
            var registration = await db.LearnerRegistrationProfiles
                .FirstOrDefaultAsync(p => p.LearnerUserId == learner.Id, ct);

            var existingProfessionValue = registration?.ProfessionId ?? learner.ActiveProfessionId;
            var existingExamTypeValue = registration?.ExamTypeId ?? learner.ActiveExamTypeCode;
            var existingCountryTargetValue = registration?.CountryTarget;
            var rawRequestedProfession = Clean(request.ProfessionId);
            var rawRequestedExamType = Clean(request.ExamTypeId);
            var rawRequestedCountryTarget = Clean(request.CountryTarget);

            var existingProfession = await TryResolveProfessionAsync(existingProfessionValue);
            SignupProfessionCatalog? requestedProfession = null;
            if (rawRequestedProfession is not null)
            {
                var resolvedProfession = await TryResolveProfessionAsync(rawRequestedProfession);
                if (resolvedProfession is null)
                {
                    if (!string.Equals(rawRequestedProfession, existingProfessionValue, StringComparison.OrdinalIgnoreCase))
                    {
                        throw ApiException.Validation("invalid_profession", $"Unknown or inactive profession '{rawRequestedProfession}'.");
                    }
                }
                else if (!string.Equals(resolvedProfession.Id, existingProfession?.Id, StringComparison.Ordinal))
                {
                    requestedProfession = resolvedProfession;
                }
            }

            var existingExamType = await TryResolveExamTypeAsync(existingExamTypeValue);
            SignupExamTypeCatalog? requestedExamType = null;
            if (rawRequestedExamType is not null)
            {
                var resolvedExamType = await TryResolveExamTypeAsync(rawRequestedExamType);
                if (resolvedExamType is null)
                {
                    if (!string.Equals(rawRequestedExamType, existingExamTypeValue, StringComparison.OrdinalIgnoreCase))
                    {
                        throw ApiException.Validation("invalid_exam_type", $"Unknown or inactive exam type '{rawRequestedExamType}'.");
                    }
                }
                else if (!string.Equals(resolvedExamType.Id, existingExamType?.Id, StringComparison.Ordinal))
                {
                    requestedExamType = resolvedExamType;
                }
            }

            var existingCountryTarget = TargetCountryOptions.TryCanonicalize(existingCountryTargetValue, out var canonicalExistingCountryTarget)
                ? canonicalExistingCountryTarget
                : null;
            string? requestedCountryTarget = null;
            if (rawRequestedCountryTarget is not null)
            {
                if (TargetCountryOptions.TryCanonicalize(rawRequestedCountryTarget, out var canonicalRequestedCountryTarget))
                {
                    if (!string.Equals(canonicalRequestedCountryTarget, existingCountryTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        requestedCountryTarget = canonicalRequestedCountryTarget;
                    }
                }
                else if (!string.Equals(rawRequestedCountryTarget, existingCountryTargetValue, StringComparison.OrdinalIgnoreCase))
                {
                    throw ApiException.Validation("country_target_invalid", "Select a valid target country.");
                }
            }

            var effectiveProfession = requestedProfession ?? existingProfession;
            var effectiveExamType = requestedExamType ?? existingExamType;
            var effectiveCountryTarget = requestedCountryTarget ?? existingCountryTarget;

            if ((requestedProfession is not null || requestedExamType is not null) && effectiveProfession is not null && effectiveExamType is not null)
            {
                ValidateProfessionExamMatch(effectiveProfession, effectiveExamType);
            }

            if ((requestedCountryTarget is not null || requestedProfession is not null) && effectiveProfession is not null && effectiveCountryTarget is not null)
            {
                ValidateProfessionCountryMatch(effectiveProfession, effectiveCountryTarget);
            }

            if (registration is null && HasRegistrationPayload(request))
            {
                if (learner.AuthAccountId is null)
                {
                    throw ApiException.Validation("registration_profile_unavailable", "This learner has no auth account to attach registration details to.");
                }

                if (requestedCountryTarget is null)
                {
                    throw ApiException.Validation("country_target_required", "Target country is required before registration details can be created.");
                }

                var defaultProfession = await ResolveDefaultProfessionAsync(requestedProfession?.Id ?? learner.ActiveProfessionId);
                var defaultExamType = await ResolveDefaultExamTypeAsync(requestedExamType?.Id ?? registration?.ExamTypeId ?? learner.ActiveExamTypeCode, defaultProfession);
                var defaultCountry = requestedCountryTarget;
                ValidateProfessionExamMatch(defaultProfession, defaultExamType);
                ValidateProfessionCountryMatch(defaultProfession, defaultCountry);

                registration = new LearnerRegistrationProfile
                {
                    Id = GenerateDomainId("reg"),
                    ApplicationUserAccountId = learner.AuthAccountId,
                    LearnerUserId = learner.Id,
                    FirstName = Clean(request.FirstName) ?? learner.DisplayName,
                    LastName = Clean(request.LastName) ?? string.Empty,
                    ExamTypeId = defaultExamType.Id,
                    ProfessionId = defaultProfession.Id,
                    SessionId = string.Empty,
                    CountryTarget = defaultCountry,
                    TargetExamDate = request.TargetExamDate,
                    MobileNumber = Clean(request.MobileNumber) ?? string.Empty,
                    AgreeToTerms = request.AgreeToTerms ?? false,
                    AgreeToPrivacy = request.AgreeToPrivacy ?? false,
                    MarketingOptIn = request.MarketingOptIn ?? false,
                    CreatedAt = timeProvider.GetUtcNow(),
                    UpdatedAt = timeProvider.GetUtcNow()
                };
                db.LearnerRegistrationProfiles.Add(registration);
                effectiveProfession = defaultProfession;
                effectiveExamType = defaultExamType;
                if (!string.Equals(learner.ActiveProfessionId, defaultProfession.Id, StringComparison.Ordinal))
                {
                    learner.ActiveProfessionId = defaultProfession.Id;
                    changes.Add($"profession→{defaultProfession.Id}");
                }
                if (!string.Equals(learner.ActiveExamTypeCode, defaultExamType.Code, StringComparison.Ordinal))
                {
                    learner.ActiveExamTypeCode = defaultExamType.Code;
                    changes.Add($"activeExamType→{defaultExamType.Code}");
                }
                changes.Add("registrationProfile");
            }

            if (requestedProfession is not null)
            {
                if (!string.Equals(learner.ActiveProfessionId, requestedProfession.Id, StringComparison.Ordinal))
                {
                    learner.ActiveProfessionId = requestedProfession.Id;
                    changes.Add($"profession→{requestedProfession.Id}");
                }
                if (registration is not null) registration.ProfessionId = requestedProfession.Id;
            }

            if (requestedExamType is not null)
            {
                if (!string.Equals(learner.ActiveExamTypeCode, requestedExamType.Code, StringComparison.Ordinal))
                {
                    learner.ActiveExamTypeCode = requestedExamType.Code;
                    changes.Add($"activeExamType→{requestedExamType.Code}");
                }
                if (registration is not null) registration.ExamTypeId = requestedExamType.Id;
                changes.Add($"examType→{requestedExamType.Id}");
            }

            var displayName = Clean(request.DisplayName);
            if (displayName is not null && !string.Equals(learner.DisplayName, displayName, StringComparison.Ordinal))
            {
                learner.DisplayName = displayName;
                changes.Add("displayName");
            }

            var timezone = Clean(request.Timezone);
            if (timezone is not null) { learner.Timezone = timezone; changes.Add("timezone"); }

            var locale = Clean(request.Locale);
            if (locale is not null) { learner.Locale = locale; changes.Add("locale"); }

            if (registration is not null)
            {
                var firstName = Clean(request.FirstName);
                if (firstName is not null) { registration.FirstName = firstName; changes.Add("firstName"); }

                var lastName = Clean(request.LastName);
                if (lastName is not null) { registration.LastName = lastName; changes.Add("lastName"); }

                var mobile = Clean(request.MobileNumber);
                if (mobile is not null) { registration.MobileNumber = mobile; changes.Add("mobileNumber"); }

                if (requestedCountryTarget is not null) { registration.CountryTarget = requestedCountryTarget; changes.Add("countryTarget"); }

                if (request.TargetExamDate is not null) { registration.TargetExamDate = request.TargetExamDate; changes.Add("targetExamDate"); }

                if (request.MarketingOptIn is { } marketing && registration.MarketingOptIn != marketing)
                { registration.MarketingOptIn = marketing; changes.Add($"marketingOptIn→{marketing}"); }

                if (request.AgreeToTerms is { } terms && registration.AgreeToTerms != terms)
                { registration.AgreeToTerms = terms; changes.Add($"agreeToTerms→{terms}"); }

                if (request.AgreeToPrivacy is { } privacy && registration.AgreeToPrivacy != privacy)
                { registration.AgreeToPrivacy = privacy; changes.Add($"agreeToPrivacy→{privacy}"); }

                registration.UpdatedAt = timeProvider.GetUtcNow();
            }

            if (changes.Count == 0)
                return new { id = userId, updated = false };

            await db.SaveChangesAsync(ct);
            var detail = request.Reason is { Length: > 0 } r ? $"{string.Join(", ", changes)} ({r})" : string.Join(", ", changes);
            await LogAuditAsync(adminId, adminName, "Updated User Profile", "User", userId, detail, ct);
            return new { id = userId, updated = true, changes };
        }

        // ── Expert / tutor ──
        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            var displayName = Clean(request.DisplayName);
            if (displayName is not null && !string.Equals(expert.DisplayName, displayName, StringComparison.Ordinal))
            {
                expert.DisplayName = displayName;
                changes.Add("displayName");
            }

            var timezone = Clean(request.Timezone);
            if (timezone is not null) { expert.Timezone = timezone; changes.Add("timezone"); }

            if (request.Specialties is not null)
            {
                var specialties = request.Specialties
                    .Select(s => s?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToArray();
                expert.SpecialtiesJson = JsonSupport.Serialize(specialties);
                changes.Add("specialties");
            }

            if (changes.Count == 0)
                return new { id = userId, updated = false };

            await db.SaveChangesAsync(ct);
            var detail = request.Reason is { Length: > 0 } r ? $"{string.Join(", ", changes)} ({r})" : string.Join(", ", changes);
            await LogAuditAsync(adminId, adminName, "Updated User Profile", "User", userId, detail, ct);
            return new { id = userId, updated = true, changes };
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    public async Task<object> UpdateUserStatusAsync(string adminId, string adminName,
        string userId, AdminUserStatusRequest request, CancellationToken ct)
    {
        var requestedStatus = NormalizeUserStatus(request.Status);
        var target = await ResolveUserTargetAsync(userId, ct);

        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Restore the account before changing its status.");
        }

        var expert = await db.ExpertUsers.FirstOrDefaultAsync(e => e.Id == userId, ct);
        if (expert is not null)
        {
            expert.IsActive = requestedStatus == ActiveUserStatus;
            if (!expert.IsActive)
            {
                await RevokeRefreshTokensAsync(expert.AuthAccountId, ct);
            }

            await db.SaveChangesAsync(ct);
            if (securityEventLogger is not null)
            {
                await securityEventLogger.TryLogAsync(
                    expert.AuthAccountId,
                    requestedStatus == ActiveUserStatus
                        ? SecurityEventKinds.AdminAccountReactivated
                        : SecurityEventKinds.AdminAccountSuspended,
                    details: new { adminId, userId },
                    cancellationToken: ct);
            }
            await LogAuditAsync(adminId, adminName, requestedStatus == ActiveUserStatus ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {requestedStatus}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            await NotifyAdminsAsync(
                NotificationEventKey.AdminUserLifecycleAction,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                $"Expert {expert.DisplayName} status changed to {requestedStatus}.",
                ct);
            return new { id = userId, status = requestedStatus };
        }

        if (await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            var learner = await db.Users.FirstAsync(u => u.Id == userId, ct);
            learner.AccountStatus = requestedStatus;
            if (!string.Equals(requestedStatus, ActiveUserStatus, StringComparison.Ordinal))
            {
                await RevokeRefreshTokensAsync(learner.AuthAccountId, ct);
            }

            await db.SaveChangesAsync(ct);
            if (securityEventLogger is not null)
            {
                await securityEventLogger.TryLogAsync(
                    learner.AuthAccountId,
                    requestedStatus == ActiveUserStatus
                        ? SecurityEventKinds.AdminAccountReactivated
                        : SecurityEventKinds.AdminAccountSuspended,
                    details: new { adminId, userId },
                    cancellationToken: ct);
            }
            await LogAuditAsync(adminId, adminName, requestedStatus == ActiveUserStatus ? "Reactivated User" : "Suspended User",
                "User", userId, $"Status changed to {requestedStatus}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerAccountStatusChanged,
                learner.Id,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                new Dictionary<string, object?>
                {
                    ["message"] = $"Your account status changed to {requestedStatus}."
                },
                ct);
            await NotifyAdminsAsync(
                NotificationEventKey.AdminUserLifecycleAction,
                "user",
                userId,
                DateTimeOffset.UtcNow.UtcDateTime.Ticks.ToString(),
                $"Learner {learner.DisplayName} status changed to {requestedStatus}.",
                ct);
            return new { id = userId, status = requestedStatus };
        }

        if (await db.ApplicationUserAccounts.AnyAsync(a => a.Id == userId && a.Role == ApplicationUserRoles.Admin, ct))
        {
            throw ApiException.Validation("admin_status_immutable",
                "Admin account suspension is not supported by the current account model.");
        }

        throw ApiException.NotFound("user_not_found", "User not found.");
    }

    private static string NormalizeUserStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw ApiException.Validation("invalid_user_status", "Status is required.");
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is ActiveUserStatus or SuspendedUserStatus
            ? normalized
            : throw ApiException.Validation("invalid_user_status", "Status must be 'active' or 'suspended'.");
    }

    public async Task<object> DeleteUserAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserLifecycleRequest request,
        CancellationToken ct)
    {
        return await PermanentlyDeleteUserAsync(adminId, adminName, userId, request, ct);
    }

    public async Task<object> PermanentlyDeleteUserAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserLifecycleRequest request,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Role == ApplicationUserRoles.Admin)
        {
            throw ApiException.Validation("admin_lifecycle_immutable", "Admin account deletion is not supported by the current account model.");
        }

        if (userHardDeleteService is null)
        {
            throw new InvalidOperationException("User hard-delete service is not configured.");
        }

        var report = await userHardDeleteService.PurgeAsync(userId, ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "UserHardDeleted",
            "User",
            resourceId: null,
            details: $"Permanently deleted {target.Role} account; purged {report.Values.Sum()} rows across {report.Count} tables."
                + (string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $" Reason: {request.Reason}"),
            ct: ct);

        return new
        {
            id = userId,
            userId,
            status = DeletedUserStatus,
            purgedRows = report.Values.Sum(),
            tables = report.Count,
            detail = report,
        };
    }

    public async Task<object> RestoreUserAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserLifecycleRequest request,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status != DeletedUserStatus)
        {
            throw ApiException.Validation("account_not_deleted", "Only deleted accounts can be restored.");
        }

        if (target.Role == ApplicationUserRoles.Admin)
        {
            throw ApiException.Validation("admin_lifecycle_immutable", "Admin account restoration is not supported by the current account model.");
        }

        var now = timeProvider.GetUtcNow();
        if (target.Role == ApplicationUserRoles.Learner)
        {
            var learner = await db.Users.SingleAsync(u => u.Id == userId, ct);
            learner.AccountStatus = ActiveUserStatus;
            if (learner.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == learner.AuthAccountId, ct);
                authAccount.DeletedAt = null;
                authAccount.UpdatedAt = now;
            }
        }
        else if (target.Role == ApplicationUserRoles.Expert)
        {
            var expert = await db.ExpertUsers.SingleAsync(e => e.Id == userId, ct);
            expert.IsActive = true;
            if (expert.AuthAccountId is not null)
            {
                var authAccount = await db.ApplicationUserAccounts.SingleAsync(a => a.Id == expert.AuthAccountId, ct);
                authAccount.DeletedAt = null;
                authAccount.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "Restored User",
            "User",
            userId,
            $"Restored {target.Role} account{(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $": {request.Reason}")}",
            ct);

        return new { id = userId, status = ActiveUserStatus };
    }

    private async Task RevokeRefreshTokensAsync(string? authAccountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authAccountId))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var activeRefreshTokens = await db.RefreshTokenRecords
            .Where(token => token.ApplicationUserAccountId == authAccountId && token.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAt = now;
        }
    }

    public async Task<object> AdjustUserCreditsAsync(string adminId, string adminName,
        string userId, AdminUserCreditsRequest request, CancellationToken ct)
    {
        for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
        {
            try
            {
                return await AdjustUserCreditsCoreAsync(adminId, adminName, userId, request, ct);
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber == 0)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw ApiException.Conflict(
            "wallet_update_conflict",
            "The user's credit balance changed while the adjustment was being applied. Please retry.");
    }

    private async Task<object> AdjustUserCreditsCoreAsync(string adminId, string adminName,
        string userId, AdminUserCreditsRequest request, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot be adjusted.");
        }

        var learner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (learner is null)
        {
            throw ApiException.NotFound("user_not_found", "User not found.");
        }

        db.Entry(learner).Property(x => x.AccountStatus).IsModified = true;

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet { Id = Guid.NewGuid().ToString(), UserId = userId, CreditBalance = 0, LastUpdatedAt = DateTimeOffset.UtcNow };
            db.Wallets.Add(wallet);
        }

        wallet.CreditBalance += request.Amount;
        if (wallet.CreditBalance < 0)
        {
            throw ApiException.Validation("insufficient_credits",
                "Credit adjustment would result in a negative balance.",
                [new ApiFieldError("amount", "insufficient", $"Current balance ({wallet.CreditBalance - request.Amount}) plus adjustment ({request.Amount}) would be negative.")]);
        }
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await LogAuditAsync(adminId, adminName, "Credit Adjustment", "User", userId,
            $"Adjusted credits by {request.Amount}" + (request.Reason != null ? $": {request.Reason}" : ""), ct);
        return new { id = userId, newBalance = wallet.CreditBalance };
    }

    public async Task<object> TriggerUserPasswordResetAsync(
        string adminId,
        string adminName,
        string userId,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot receive password resets.");
        }

        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("password_reset_unavailable", "This user does not have a password-based sign-in account.");
        }

        EnsureOwnerAccountMutationAllowed(adminId, target.AuthAccountId, "trigger a password reset");

        var challenge = await emailOtpService.RequestPasswordResetOtpAsync(target.Email, ct);
        await LogAuditAsync(adminId, adminName, "Triggered Password Reset", "User", userId, $"Triggered password reset for {target.Email}", ct);

        return new
        {
            userId = target.Id,
            target.Email,
            purpose = challenge.Purpose,
            deliveryChannel = challenge.DeliveryChannel,
            destinationHint = challenge.DestinationHint,
            expiresAt = challenge.ExpiresAt,
            retryAfterSeconds = challenge.RetryAfterSeconds
        };
    }

    public async Task<object> SetUserPasswordAsync(
        string adminId,
        string adminName,
        string userId,
        AdminUserSetPasswordRequest request,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot have passwords updated.");
        }

        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("password_set_unavailable", "This user does not have a password-based sign-in account.");
        }

        EnsureOwnerAccountMutationAllowed(adminId, target.AuthAccountId, "set the password");

        await GetPasswordPolicyService().EnsurePasswordAcceptableAsync(request.Password, target.Email, ct);

        var authAccount = await db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == target.AuthAccountId, ct);
        if (authAccount is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "Authentication account not found.");
        }

        var now = timeProvider.GetUtcNow();
        authAccount.PasswordHash = passwordHasher.HashPassword(authAccount, request.Password);
        authAccount.LockoutUntil = null;
        authAccount.FailedSignInCount = 0;
        authAccount.UpdatedAt = now;

        var revoked = await RevokeActiveRefreshTokensAsync(authAccount.Id, ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(
            adminId,
            adminName,
            "Set Password",
            "User",
            userId,
            $"Set password for {target.Email}. Revoked {revoked} active session(s).",
            ct);

        return new
        {
            userId = target.Id,
            target.Email,
            revoked
        };
    }

    public async Task<object> RevokeUserSessionsAsync(string adminId, string adminName,
        string userId, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("auth_account_missing", "This user does not have an authentication account to revoke.");
        }

        var revoked = await RevokeActiveRefreshTokensAsync(target.AuthAccountId, ct);
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Revoked Sessions", "User", userId,
            $"Force sign-out: revoked {revoked} active session(s).", ct);
        return new { id = target.Id, revoked };
    }

    public async Task<object> VerifyUserEmailAsync(
        string adminId,
        string adminName,
        string userId,
        CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Deleted accounts cannot be email-verified.");
        }

        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("auth_account_missing", "This user does not have an authentication account to verify.");
        }

        var authAccount = await db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == target.AuthAccountId, ct);
        if (authAccount is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "Authentication account not found.");
        }

        if (authAccount.EmailVerifiedAt is not null)
        {
            return new
            {
                userId = target.Id,
                target.Email,
                alreadyVerified = true,
                emailVerifiedAt = authAccount.EmailVerifiedAt,
                revokedSessions = 0
            };
        }

        var now = timeProvider.GetUtcNow();
        authAccount.EmailVerifiedAt = now;
        authAccount.UpdatedAt = now;

        var pendingVerificationChallenges = await db.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == authAccount.Id
                && x.Purpose == EmailOtpService.EmailVerificationPurpose
                && x.VerifiedAt == null)
            .ToListAsync(ct);
        foreach (var challenge in pendingVerificationChallenges)
        {
            challenge.VerifiedAt = now;
        }

        var activeSessionCount = await db.RefreshTokenRecords
            .CountAsync(x => x.ApplicationUserAccountId == authAccount.Id && x.RevokedAt == null, ct);

        var transaction = await BeginTransactionIfNeededAsync(ct);
        try
        {
            if (sessionRevocationService is null)
            {
                await RevokeActiveRefreshTokensAsync(authAccount.Id, ct);
            }
            else
            {
                await sessionRevocationService.RevokeAllFamiliesAsync(
                    authAccount.Id,
                    exceptFamilyId: null,
                    reason: "admin_email_verified",
                    ct);
            }

            await db.SaveChangesAsync(ct);
            await LogAuditAsync(
                adminId,
                adminName,
                "Verified Email",
                "User",
                userId,
                $"Marked email verified administratively. Revoked {activeSessionCount} active session(s).",
                ct);
            await CommitIfOwnedAsync(transaction, ct);
            return new
            {
                userId = target.Id,
                target.Email,
                alreadyVerified = false,
                emailVerifiedAt = authAccount.EmailVerifiedAt,
                revokedSessions = activeSessionCount
            };
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<object> UnlockUserAsync(string adminId, string adminName,
        string userId, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("auth_account_missing", "This user does not have an authentication account.");
        }

        // Clearing the owner's lockout would hand an attacker fresh password guesses.
        EnsureOwnerAccountMutationAllowed(adminId, target.AuthAccountId, "clear the sign-in lockout");

        var authAccount = await db.ApplicationUserAccounts.FirstOrDefaultAsync(a => a.Id == target.AuthAccountId, ct);
        if (authAccount is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "Authentication account not found.");
        }

        var hadLockout = authAccount.LockoutUntil is not null || authAccount.FailedSignInCount > 0;
        authAccount.LockoutUntil = null;
        authAccount.FailedSignInCount = 0;
        authAccount.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await LogAuditAsync(adminId, adminName, "Unlocked Account", "User", userId,
            hadLockout ? "Cleared lockout and failed sign-in counter." : "Account had no active lockout; counter reset.", ct);
        return new { id = target.Id, lockoutCleared = hadLockout };
    }

    public async Task<object> ResendUserInviteAsync(string adminId, string adminName,
        string userId, CancellationToken ct)
    {
        var target = await ResolveUserTargetAsync(userId, ct);
        if (target.Status == DeletedUserStatus)
        {
            throw ApiException.Validation("account_deleted", "Restore the account before resending the invitation.");
        }
        if (string.IsNullOrWhiteSpace(target.AuthAccountId))
        {
            throw ApiException.Validation("auth_account_missing", "This user does not have an authentication account.");
        }

        var authAccount = await db.ApplicationUserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == target.AuthAccountId, ct);
        if (authAccount is null)
        {
            throw ApiException.NotFound("auth_account_not_found", "Authentication account not found.");
        }

        var challenge = await emailOtpService.RequestPasswordResetOtpAsync(target.Email, ct);
        await LogAuditAsync(adminId, adminName, "Resent Invite", "User", userId,
            $"Resent invitation to {target.Email}.", ct);
        return new
        {
            id = target.Id,
            target.Email,
            purpose = challenge.Purpose,
            deliveryChannel = challenge.DeliveryChannel,
            destinationHint = challenge.DestinationHint,
            expiresAt = challenge.ExpiresAt,
            retryAfterSeconds = challenge.RetryAfterSeconds
        };
    }

    private sealed record AdminUserSecuritySnapshot(
        bool MfaEnabled,
        int FailedSignInCount,
        DateTimeOffset? LockoutUntil,
        bool LockedOut,
        DateTimeOffset? EmailVerifiedAt,
        int ActiveSessionCount,
        DateTimeOffset? LastSessionAt,
        string? LastSessionIp,
        string? LastSessionDevice,
        bool DeviceVerificationExempt,
        int? MaxDevicesOverride,
        int EffectiveMaxDevices,
        int ActiveDeviceCount);

    private async Task<AdminUserSecuritySnapshot?> BuildSecuritySnapshotAsync(
        ApplicationUserAccount? authAccount,
        string? learnerEmail = null,
        CancellationToken ct = default)
    {
        if (authAccount is null) return null;
        var now = timeProvider.GetUtcNow();
        var sessions = await db.RefreshTokenRecords.AsNoTracking()
            .Where(t => t.ApplicationUserAccountId == authAccount.Id
                && t.RevokedAt == null
                && t.ExpiresAt > now)
            .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
            .Take(10)
            .Select(t => new
            {
                t.LastUsedAt,
                t.CreatedAt,
                t.IpAddress,
                t.DeviceInfo,
            })
            .ToListAsync(ct);

        var activeCount = sessions.Count;
        var latest = sessions.FirstOrDefault();
        var activeDeviceCount = await db.TrustedDevices.AsNoTracking()
            .Where(d => d.ApplicationUserAccountId == authAccount.Id && d.RevokedAt == null)
            .Select(d => d.DeviceId)
            .Distinct()
            .CountAsync(ct);
        var effectiveMaxDevices = authAccount.MaxDevicesOverride is > 0 and <= TrustedDeviceService.MaxAllowedDevicesOverride
            ? authAccount.MaxDevicesOverride.Value
            : TrustedDeviceService.DefaultMaxDevices;

        var deviceVerificationExempt = false;
        if (runtimeSettingsProvider is not null)
        {
            var securitySettings = (await runtimeSettingsProvider.GetAsync(ct)).Security;
            deviceVerificationExempt = AuthService.AreAnyDeviceVerificationExempt(
                [authAccount.Email, authAccount.NormalizedEmail, learnerEmail],
                securitySettings.DeviceVerificationExemptEmails);
        }

        return new AdminUserSecuritySnapshot(
            MfaEnabled: authAccount.AuthenticatorEnabledAt is not null,
            FailedSignInCount: authAccount.FailedSignInCount,
            LockoutUntil: authAccount.LockoutUntil,
            LockedOut: authAccount.LockoutUntil is not null && authAccount.LockoutUntil > now,
            EmailVerifiedAt: authAccount.EmailVerifiedAt,
            ActiveSessionCount: activeCount,
            LastSessionAt: latest?.LastUsedAt ?? latest?.CreatedAt,
            LastSessionIp: latest?.IpAddress,
            LastSessionDevice: latest?.DeviceInfo,
            DeviceVerificationExempt: deviceVerificationExempt,
            MaxDevicesOverride: authAccount.MaxDevicesOverride,
            EffectiveMaxDevices: effectiveMaxDevices,
            ActiveDeviceCount: activeDeviceCount);
    }

    private async Task<object?> BuildLearnerSubscriptionAsync(string userId, CancellationToken ct)
    {
        // Prefer the subscription that still owns its plan slot (see
        // SubscriptionStateMachine.CurrentOwnershipStatuses) so this admin-facing
        // summary reflects the learner's REAL current package. Without this, swapping
        // packages (grant new, then cancel the old one) makes the just-cancelled row
        // "more recently changed" than the new one, so it wrongly wins a bare
        // OrderByDescending(ChangedAt) and the admin sees the old/removed package.
        var learner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var currentPlanId = learner?.CurrentPlanId?.Trim();

        var activeSubs = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && SubscriptionStateMachine.CurrentOwnershipStatuses.Contains(s.Status))
            .OrderByDescending(s => s.ChangedAt)
            .ToListAsync(ct);

        Subscription? subscription = null;
        if (!string.IsNullOrWhiteSpace(currentPlanId) && activeSubs.Count > 0)
        {
            var plans = await db.BillingPlans.AsNoTracking().ToListAsync(ct);
            var planMap = new Dictionary<string, BillingPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plans)
            {
                planMap.TryAdd(p.Code, p);
                planMap.TryAdd(p.Id, p);
            }
            subscription = activeSubs.FirstOrDefault(s =>
                string.Equals(s.PlanId, currentPlanId, StringComparison.OrdinalIgnoreCase) ||
                (planMap.TryGetValue(s.PlanId, out var p) && (
                    string.Equals(p.Code, currentPlanId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Id, currentPlanId, StringComparison.OrdinalIgnoreCase)
                )));
        }

        subscription ??= activeSubs.FirstOrDefault()
            ?? await db.Subscriptions.AsNoTracking()
                .Where(s => s.UserId == userId
                    && s.Status != SubscriptionStatus.Draft
                    && !(s.Status == SubscriptionStatus.Pending && s.FulfilmentStatus == FulfilmentStatuses.Auto))
                .OrderByDescending(s => s.ChangedAt)
                .FirstOrDefaultAsync(ct);
        if (subscription is null) return null;

        // Subscription.PlanId holds the plan CODE (see UserAccessAllocationService.
        // GrantPackageAsync: `PlanId = plan.Code`), not the plan's own Id — which for
        // every real seeded plan is a different, prefixed string (e.g. "plan_<code>").
        // Matching on Id alone means `plan` is always null and planCode always null.
        var plan = await db.BillingPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == subscription.PlanId || p.Id == subscription.PlanId, ct);

        return new
        {
            subscription.Id,
            planId = subscription.PlanId,
            planName = plan?.Name ?? subscription.PlanId,
            planCode = plan?.Code ?? subscription.PlanId,
            status = subscription.Status.ToString().ToLowerInvariant(),
            startedAt = subscription.StartedAt,
            // Authoritative access end (package expiry). Admin UI labels this
            // "Access ends"; NextRenewalAt is only a billing date for renewable plans.
            expiresAt = subscription.ExpiresAt,
            nextRenewalAt = subscription.NextRenewalAt,
            changedAt = subscription.ChangedAt,
            priceAmount = subscription.PriceAmount,
            currency = subscription.Currency,
            interval = subscription.Interval,
        };
    }

    private async Task<List<object>> GetRecentUserActivityAsync(
        string userId,
        string? authAccountId,
        CancellationToken ct)
    {
        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => (e.ResourceType == "User" && e.ResourceId == userId)
                || e.ActorId == userId
                || (authAccountId != null && (e.ActorAuthAccountId == authAccountId || e.ActorId == authAccountId)))
            .OrderByDescending(e => e.OccurredAt)
            .Take(20)
            .Select(e => new
            {
                e.Id,
                occurredAt = e.OccurredAt,
                action = e.Action,
                actorName = e.ActorName,
                resourceType = e.ResourceType,
                resourceId = e.ResourceId,
                details = e.Details,
            })
            .ToListAsync(ct);
        return events.Cast<object>().ToList();
    }

    // ══════════════════════════════════════════════════════
    // A6 · Bulk Learner Operations
    // ══════════════════════════════════════════════════════

    public async Task<object> BulkCreditAdjustmentAsync(string actorId, string actorName, string[] userIds, int creditAmount, string reason, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 500)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 500 user IDs.");

        var wallets = await db.Wallets
            .Where(w => userIds.Contains(w.UserId))
            .ToListAsync(ct);

        var results = new List<object>();
        foreach (var wallet in wallets)
        {
            wallet.CreditBalance += creditAmount;
            wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
            db.WalletTransactions.Add(new WalletTransaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                TransactionType = creditAmount >= 0 ? "bulk_credit" : "bulk_debit",
                Amount = Math.Abs(creditAmount),
                BalanceAfter = wallet.CreditBalance,
                ReferenceType = "manual",
                ReferenceId = "bulk",
                Description = reason,
                CreatedBy = actorId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            results.Add(new { userId = wallet.UserId, newBalance = wallet.CreditBalance });
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "BulkCreditAdjustment", "Wallet", "bulk",
            $"Adjusted {wallets.Count} wallets by {creditAmount} credits. Reason: {reason}", ct);

        return new { processed = wallets.Count, skipped = userIds.Length - wallets.Count, results };
    }

    public async Task<object> BulkNotificationAsync(string actorId, string actorName, string[] userIds, string title, string message, string? category, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 1000)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 1000 user IDs.");

        var sent = 0;
        foreach (var userId in userIds)
        {
            await notifications.CreateForLearnerAsync(
                NotificationEventKey.LearnerAccountStatusChanged,
                userId,
                category ?? "admin_broadcast",
                $"bulk-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow.Ticks.ToString(),
                new Dictionary<string, object?> { ["title"] = title, ["message"] = message },
                ct);
            sent++;
        }

        await LogAuditAsync(actorId, actorName, "BulkNotification", "Notification", "bulk",
            $"Sent to {sent} users. Title: {title}", ct);

        return new { sent, total = userIds.Length };
    }

    public async Task<object> BulkStatusChangeAsync(string actorId, string actorName, string[] userIds, string newStatus, string reason, CancellationToken ct)
    {
        if (userIds.Length == 0 || userIds.Length > 200)
            throw ApiException.Validation("INVALID_BATCH", "Provide between 1 and 200 user IDs.");

        var accounts = await db.Users
            .Where(a => userIds.Contains(a.Id))
            .ToListAsync(ct);

        foreach (var acct in accounts)
        {
            acct.AccountStatus = newStatus;
        }

        await db.SaveChangesAsync(ct);
        await LogAuditAsync(actorId, actorName, "BulkStatusChange", "LearnerUser", "bulk",
            $"Changed {accounts.Count} accounts to '{newStatus}'. Reason: {reason}", ct);

        return new { processed = accounts.Count, skipped = userIds.Length - accounts.Count, newStatus };
    }
}
