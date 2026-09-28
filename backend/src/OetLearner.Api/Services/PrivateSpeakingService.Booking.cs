using System.Globalization;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendar = Ical.Net.Calendar;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Services;

public sealed partial class PrivateSpeakingService
{
    // ── Booking Flow ────────────────────────────────────────────────────

    /// <summary>
    /// Reserve a slot by consuming one bundled/private-speaking entitlement.
    /// Uses a serializable transaction to prevent double-booking and double-debiting.
    /// </summary>
    public async Task<BookingCheckoutResult> CreateBookingAndCheckoutAsync(
        string learnerUserId, string? tutorProfileId,
        DateTimeOffset sessionStartUtc, int durationMinutes,
        string learnerTimezone, string? learnerNotes,
        string? professionTrack,
        string idempotencyKey, string? sessionFormat, CancellationToken ct,
        string? paymentMethod = null)
    {
        // "paypal" → pay the catalog price via embedded PayPal (no credit consumed).
        // Anything else → consume a speaking-session entitlement (the historic behaviour).
        var payWithPaypal = string.Equals(paymentMethod?.Trim(), "paypal", StringComparison.OrdinalIgnoreCase);
        var config = await GetConfigAsync(ct);
        if (!config.IsEnabled)
            return BookingCheckoutResult.Fail("Private Speaking Sessions are currently disabled.");
        // B9: refuse before any entitlement debit or payment order is created.
        EnsureLiveRoomsAvailable();
        if (!IsUsableIdempotencyKey(idempotencyKey))
            return BookingCheckoutResult.Fail("A valid booking idempotency key is required.");
        var autoAssignTutor = IsAnyTutor(tutorProfileId);

        // FINAL 2026-09-06: Live Tutor ("Book a tutor as your patient") is NOT
        // a general feature. Only candidates holding an eligible main
        // course/package or the Speaking Crash Course may book — on EITHER
        // payment path. The product entitlement flag
        // (BillingPlan.SpeakingAddonsEnabled, resolved server-side here — never
        // UI package-name matching) is the single source of truth, so a direct
        // URL/API bypass cannot create a booking. AI-credit ownership alone
        // never grants access.
        var tutorEligibility = await addonEligibility.ResolveAsync(learnerUserId, "addon-speaking-1session", ct);
        if (!tutorEligibility.Eligible)
        {
            throw ApiException.Forbidden(
                "live_tutor_not_eligible",
                "You are not eligible to book a session with a tutor.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var idempotencyScope = BuildIdempotencyScopeKey("book", learnerUserId, idempotencyKey);
        var idempotencyPrefix = BuildScopedIdempotencyPrefix(idempotencyScope);
        var scopedIdempotencyKey = BuildScopedIdempotencyKey(
            idempotencyScope,
            autoAssignTutor ? AnyTutorId : tutorProfileId!,
            sessionStartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            durationMinutes.ToString(CultureInfo.InvariantCulture),
            learnerTimezone,
            learnerNotes ?? string.Empty);

        // Idempotency check
        var existingBooking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.LearnerUserId == learnerUserId
                && b.IdempotencyKey != null
                && b.IdempotencyKey.StartsWith(idempotencyPrefix), ct);
        if (existingBooking is not null)
        {
            if (!string.Equals(existingBooking.IdempotencyKey, scopedIdempotencyKey, StringComparison.Ordinal))
            {
                return BookingCheckoutResult.Fail("This idempotency key was already used with different booking details. Please retry with a new key.");
            }

            await transaction.CommitAsync(ct);
            return existingBooking.Status == PrivateSpeakingBookingStatus.Expired
                ? BookingCheckoutResult.Fail("Previous booking attempt expired. Please try again with a new request.")
                : new BookingCheckoutResult(true, null, existingBooking.Id,
                    existingBooking.StripeCheckoutSessionId, null, existingBooking.EntitlementConsumed);
        }

        if (autoAssignTutor)
        {
            // "Any available tutor": assign inside this serializable transaction so
            // two concurrent "any" bookings cannot land on the same tutor slot.
            tutorProfileId = await FindLeastLoadedAvailableTutorAsync(sessionStartUtc, durationMinutes, ct);
            if (tutorProfileId is null)
                return BookingCheckoutResult.Fail("No tutor is available at this time. Please select another slot.");
        }

        var profile = await db.PrivateSpeakingTutorProfiles.FindAsync([tutorProfileId], ct);
        if (profile is null || !profile.IsActive)
            return BookingCheckoutResult.Fail("Tutor is not available.");

        var now = timeProvider.GetUtcNow();
        var calibration = await CheckTutorCalibrationBookingGuardAsync(profile, now, ct);
        if (!calibration.Allowed)
        {
            return BookingCheckoutResult.Fail("Tutor is temporarily unavailable while calibration quality is reviewed.");
        }

        var minBookingTime = now.AddHours(config.MinBookingLeadTimeHours);
        if (sessionStartUtc <= minBookingTime)
            return BookingCheckoutResult.Fail($"Sessions must be booked at least {config.MinBookingLeadTimeHours} hours in advance.");

        var maxBookingTime = now.AddDays(config.MaxBookingAdvanceDays);
        if (sessionStartUtc > maxBookingTime)
            return BookingCheckoutResult.Fail($"Sessions cannot be booked more than {config.MaxBookingAdvanceDays} days in advance.");

        if (!await IsRequestedSlotAvailableAsync(profile, sessionStartUtc, durationMinutes, ct))
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var sessionEnd = sessionStartUtc.AddMinutes(durationMinutes);
        var sessionStartWithBuffer = sessionStartUtc.AddMinutes(-config.BufferMinutesBetweenSlots);
        var sessionEndWithBuffer = sessionEnd.AddMinutes(config.BufferMinutesBetweenSlots);

        // Check for conflicting tutor bookings (race protection via DB unique constraint)
        var hasConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.TutorProfileId == tutorProfileId
            && b.SessionStartUtc < sessionEndWithBuffer
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > sessionStartWithBuffer
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);

        if (hasConflict)
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        // Check for overlapping learner bookings
        var learnerConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.LearnerUserId == learnerUserId
            && b.SessionStartUtc < sessionEnd
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > sessionStartUtc
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);

        if (learnerConflict)
            return BookingCheckoutResult.Fail("You already have a booking at this time. Please select a different slot.");

        var priceMinorUnits = profile.PriceOverrideMinorUnits ?? config.DefaultPriceMinorUnits;
        var calendarBusy = await calendarService.CheckBusyAsync(tutorProfileId, sessionStartUtc, sessionEnd, ct);
        if (calendarBusy.Connected)
        {
            if (calendarBusy.Error is not null)
                return BookingCheckoutResult.Fail("Tutor calendar availability could not be verified. Please try another slot shortly.");
            if (calendarBusy.IsBusy)
                return BookingCheckoutResult.Fail("Tutor calendar shows this slot is no longer available. Please select another slot.");
        }

        if (payWithPaypal)
        {
            if (priceMinorUnits <= 0)
            {
                return BookingCheckoutResult.Fail("This session can't be paid for online right now. Please contact support.");
            }

            var paidBooking = new PrivateSpeakingBooking
            {
                Id = $"psb-{Guid.NewGuid():N}",
                LearnerUserId = learnerUserId,
                TutorProfileId = tutorProfileId,
                Status = PrivateSpeakingBookingStatus.PendingPayment,
                SessionStartUtc = sessionStartUtc,
                DurationMinutes = durationMinutes,
                TutorTimezone = profile.Timezone,
                LearnerTimezone = learnerTimezone,
                PriceMinorUnits = priceMinorUnits,
                Currency = config.Currency,
                PaymentStatus = PrivateSpeakingPaymentStatus.Pending,
                PaymentGateway = "paypal",
                // The reservation holds the slot until the embedded capture (or the
                // PAYMENT.CAPTURE.COMPLETED webhook) confirms; the existing reservation-expiry
                // sweep releases it if the learner never pays.
                ReservationExpiresAt = now.AddMinutes(config.ReservationTimeoutMinutes),
                IdempotencyKey = scopedIdempotencyKey,
                LearnerNotes = learnerNotes,
                ProfessionTrack = NormalizeProfessionTrack(professionTrack),
                SessionFormat = string.Equals(sessionFormat?.Trim(), "exam", StringComparison.OrdinalIgnoreCase)
                    ? "exam"
                    : "practice",
                CreatedAt = now,
                UpdatedAt = now
            };

            // Create the PayPal order BEFORE persisting so a gateway failure aborts the whole
            // serializable transaction and never leaves a dangling slot reservation.
            PaymentIntentResult intent;
            try
            {
                intent = await paymentGateways.GetGateway("paypal").CreatePaymentIntentAsync(new CreatePaymentIntentRequest(
                    UserId: learnerUserId,
                    Amount: priceMinorUnits / 100m,
                    Currency: config.Currency,
                    ProductType: "private_speaking",
                    ProductId: paidBooking.Id,
                    Description: "OET 1:1 Speaking session",
                    Metadata: new Dictionary<string, string> { ["privateSpeakingBookingId"] = paidBooking.Id },
                    SuccessUrl: platformLinks.BuildWebUrl("/private-speaking?payment=success"),
                    CancelUrl: platformLinks.BuildWebUrl("/private-speaking?payment=cancelled"),
                    IdempotencyKey: $"{scopedIdempotencyKey}-paypal"), ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or PaymentGatewayApiException)
            {
                return BookingCheckoutResult.Fail("We couldn't start your PayPal payment. Please try again or use a session credit.");
            }

            paidBooking.PaymentGatewayOrderId = intent.GatewayTransactionId;
            db.PrivateSpeakingBookings.Add(paidBooking);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return BookingCheckoutResult.Fail("This time slot was just booked. Please select another slot.");
            }

            await AuditAsync(paidBooking.Id, learnerUserId, "learner", "booking_pending_payment",
                $"Tutor: {tutorProfileId}, Time: {sessionStartUtc:O}, PayPal order: {intent.GatewayTransactionId}, Price minor units: {priceMinorUnits}", ct);
            if (autoAssignTutor)
                await AuditAsync(paidBooking.Id, "system", "system", "tutor_auto_assigned", $"Tutor: {tutorProfileId} (least-loaded available)", ct);

            // Calendar and notification jobs run on payment confirmation.
            // (ConfirmBookingPaymentAsync), exactly as the Stripe pending-payment path does.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new BookingCheckoutResult(
                Success: true,
                Error: null,
                BookingId: paidBooking.Id,
                CheckoutSessionId: intent.GatewayTransactionId,
                CheckoutUrl: null,
                EntitlementUsed: false,
                SpeakingSessionsRemaining: null);
        }

        var subscription = await ResolveEligibleSpeakingSubscriptionAsync(learnerUserId, now, ct);
        if (subscription is null)
            return BookingCheckoutResult.Fail("You need an available private speaking session credit to book this session.");

        subscription.SpeakingSessionsRemaining -= 1;

        var booking = new PrivateSpeakingBooking
        {
            Id = $"psb-{Guid.NewGuid():N}",
            LearnerUserId = learnerUserId,
            TutorProfileId = tutorProfileId,
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = sessionStartUtc,
            DurationMinutes = durationMinutes,
            TutorTimezone = profile.Timezone,
            LearnerTimezone = learnerTimezone,
            PriceMinorUnits = 0,
            Currency = config.Currency,
            PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
            PaymentConfirmedAt = now,
            EntitlementSubscriptionId = subscription.Id,
            EntitlementConsumed = true,
            EntitlementConsumedAt = now,
            ReservationExpiresAt = null,
            IdempotencyKey = scopedIdempotencyKey,
            LearnerNotes = learnerNotes,
            ProfessionTrack = NormalizeProfessionTrack(professionTrack),
            SessionFormat = string.Equals(sessionFormat?.Trim(), "exam", StringComparison.OrdinalIgnoreCase)
                ? "exam"
                : "practice",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.PrivateSpeakingBookings.Add(booking);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return BookingCheckoutResult.Fail("This time slot was just booked. Please select another slot.");
        }

        await AuditAsync(booking.Id, learnerUserId, "learner", "booking_reserved",
            $"Tutor: {tutorProfileId}, Time: {sessionStartUtc:O}, Entitlement subscription: {subscription.Id}, Catalog price minor units: {priceMinorUnits}", ct);
        if (autoAssignTutor)
            await AuditAsync(booking.Id, "system", "system", "tutor_auto_assigned", $"Tutor: {tutorProfileId} (least-loaded available)", ct);

        QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: true);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new BookingCheckoutResult(
            Success: true,
            Error: null,
            BookingId: booking.Id,
            CheckoutSessionId: null,
            CheckoutUrl: null,
            EntitlementUsed: true,
            SpeakingSessionsRemaining: subscription.SpeakingSessionsRemaining);
    }

    /// <summary>
    /// Handle successful payment webhook, confirm the booking, and queue
    /// LiveKit-aware notifications and calendar synchronization.
    /// Idempotent: safe to call multiple times for the same booking.
    /// </summary>
    public async Task<bool> ConfirmBookingPaymentAsync(
        string stripeCheckoutSessionId, string? paymentIntentId, CancellationToken ct)
    {
        // The id may be a Stripe checkout session (hosted) or a PayPal order id (embedded),
        // so match either — both uniquely identify a single booking.
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId
                || b.PaymentGatewayOrderId == stripeCheckoutSessionId, ct);

        if (booking is null)
        {
            logger.LogWarning("No booking found for payment session {SessionId}", stripeCheckoutSessionId);
            return false;
        }

        // Idempotent: already confirmed
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded)
            return true;

        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded;
        booking.PaymentConfirmedAt = timeProvider.GetUtcNow();
        booking.StripePaymentIntentId = paymentIntentId;
        booking.Status = PrivateSpeakingBookingStatus.Confirmed;
        booking.ReservationExpiresAt = null; // Clear reservation timeout
        booking.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);

        await AuditAsync(booking.Id, "system", "system", "payment_confirmed",
            $"Stripe session: {stripeCheckoutSessionId}", ct);

        // A replacement booking carries the original entitlement and finalizes
        // the original booking after successful payment.
        if (booking.RescheduledFromBookingId is not null)
        {
            var original = await db.PrivateSpeakingBookings
                .FirstOrDefaultAsync(b => b.Id == booking.RescheduledFromBookingId, ct);
            if (original is not null && original.Status != PrivateSpeakingBookingStatus.Cancelled)
            {
                var nowReschedule = timeProvider.GetUtcNow();
                original.Status = PrivateSpeakingBookingStatus.Cancelled;
                original.CancellationReason = "rescheduled";
                original.CancelledAt = nowReschedule;
                original.UpdatedAt = nowReschedule;
                await db.SaveChangesAsync(ct);

                QueueCalendarSyncJob(original.Id);
                await db.SaveChangesAsync(ct);
            }

        }

        QueueBookingPostCommitJobs(booking.Id, includeCalendarSync: true);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>Handle payment failure for a booking.</summary>
    public async Task HandlePaymentFailureAsync(
        string stripeCheckoutSessionId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId, ct);

        if (booking is null) return;
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded) return;

        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Failed;
        booking.Status = PrivateSpeakingBookingStatus.Failed;
        booking.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await RevertRescheduleIfPendingAsync(booking, ct);

        await AuditAsync(booking.Id, "system", "system", "payment_failed",
            $"Stripe session: {stripeCheckoutSessionId}", ct);
    }

    /// <summary>Handle expired checkout sessions - release the slot.</summary>
    public async Task HandleCheckoutExpiredAsync(
        string stripeCheckoutSessionId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.StripeCheckoutSessionId == stripeCheckoutSessionId, ct);

        if (booking is null) return;
        if (booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded) return;

        booking.Status = PrivateSpeakingBookingStatus.Expired;
        booking.PaymentStatus = PrivateSpeakingPaymentStatus.Failed;
        booking.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await RevertRescheduleIfPendingAsync(booking, ct);

        await AuditAsync(booking.Id, "system", "system", "checkout_expired",
            $"Stripe session: {stripeCheckoutSessionId}", ct);
    }

    /// <summary>
    /// When a legacy replacement checkout expires or fails, abort the
    /// reschedule: clear the original's <see cref="PrivateSpeakingBooking.RescheduledToBookingId"/>
    /// link so the original booking stays intact/Confirmed and the learner keeps
    /// their slot. No penalty is applied.
    /// </summary>
    private async Task RevertRescheduleIfPendingAsync(PrivateSpeakingBooking replacement, CancellationToken ct)
    {
        if (replacement.RescheduledFromBookingId is null) return;

        var original = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(b => b.Id == replacement.RescheduledFromBookingId, ct);
        if (original is null) return;

        original.RescheduledToBookingId = null;
        original.UpdatedAt = timeProvider.GetUtcNow();

        // The replacement only inherited the entitlement reference from the original; it never
        // independently consumed a credit. Now that the reschedule is aborted, neutralize the
        // replacement's entitlement fields so a later cancellation of this Expired/Failed row
        // cannot restore a phantom credit (the original retains the real consumption).
        replacement.EntitlementConsumed = false;
        replacement.EntitlementSubscriptionId = null;
        replacement.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }

    // ── Cancellation ────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> CancelBookingAsync(
        string bookingId, string actorId, string actorRole, string? reason, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(b => b.TutorProfile)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null) return (false, "Booking not found.");

        if (booking.Status is PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.Completed
            or PrivateSpeakingBookingStatus.NoShow)
            return (false, "Booking cannot be cancelled in its current state.");

        var config = await GetConfigAsync(ct);
        var now = timeProvider.GetUtcNow();

        // PDF §2.2 / §8 refund tiers. Learners forfeit refund inside the 24h
        // window and cannot cancel after the session has started; admin/expert
        // (PDF edge case #7) always get a full refund and may cancel even after
        // the start time.
        bool fullRefund;
        if (actorRole == "learner")
        {
            // Verify the actor is the learner who booked.
            if (booking.LearnerUserId != actorId)
                return (false, "You can only cancel your own bookings.");

            if (now >= booking.SessionStartUtc)
                return (false, "This session has already started and can no longer be cancelled. It will be handled as a no-show if you do not attend.");

            fullRefund = SpeakingBookingPolicy.FullRefundEligible(
                booking.SessionStartUtc,
                now);
        }
        else
        {
            // expert / admin — always full refund regardless of timing.
            fullRefund = true;
        }

        booking.Status = PrivateSpeakingBookingStatus.Cancelled;
        booking.CancelledBy = actorId;
        booking.CancellationReason = reason;
        booking.CancelledAt = now;
        booking.UpdatedAt = now;

        string refundDetail;
        if (fullRefund)
        {
            booking.RefundIssued = true;

            if (booking.EntitlementConsumed && booking.EntitlementRestoredAt is null && booking.RescheduledToBookingId is null)
            {
                await RestoreSpeakingEntitlementAsync(booking, $"cancelled_by_{actorRole}", ct);
            }

            // Direct-paid bookings (Stripe PaymentIntent) get a money refund.
            // Most current bookings are entitlement-only and skip this branch.
            if (!string.IsNullOrWhiteSpace(booking.StripePaymentIntentId)
                && booking.PaymentStatus == PrivateSpeakingPaymentStatus.Succeeded
                && booking.PriceMinorUnits > 0)
            {
                try
                {
                    var refundId = await stripeService.CreateRefundAsync(
                        booking.StripePaymentIntentId,
                        booking.PriceMinorUnits,
                        "requested_by_customer",
                        ct);
                    booking.StripeRefundId = refundId;
                    booking.RefundAmountMinorUnits = booking.PriceMinorUnits;
                    booking.PaymentStatus = PrivateSpeakingPaymentStatus.Refunded;
                }
                catch (Exception ex)
                {
                    // Do not fail the cancellation — leave StripeRefundId null so
                    // an admin can retry the refund out of band.
                    logger.LogWarning(ex,
                        "Stripe refund failed for cancelled booking {BookingId}; cancellation remains uncommitted",
                        booking.Id);
                    return (false, "The full refund could not be completed, so the booking remains active. Please retry.");
                }
            }

            refundDetail = "full_refund";
        }
        else
        {
            booking.RefundIssued = false;
            booking.RefundAmountMinorUnits = 0;
            refundDetail = "no_refund_within_window";
        }

        await db.SaveChangesAsync(ct);
        await AuditAsync(
            booking.Id, actorId, actorRole, "booking_cancelled",
            string.IsNullOrWhiteSpace(reason) ? refundDetail : $"{reason} | {refundDetail}",
            ct);

        QueueCalendarSyncJob(booking.Id);
        await db.SaveChangesAsync(ct);

        // Notify parties
        var tutorName = booking.TutorProfile?.DisplayName ?? "Tutor";
        var sessionTime = booking.SessionStartUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var cancellationMessage = fullRefund
            ? "Your private speaking session has been cancelled and a full refund/credit has been issued."
            : "Your private speaking session has been cancelled. As this was 24 hours or less before the start time, a full refund is not available under the cancellation policy.";

        await notificationService.CreateForLearnerAsync(
            NotificationEventKey.LearnerPrivateSpeakingCancelled,
            booking.LearnerUserId,
            "private_speaking_booking", booking.Id,
            $"cancel-{booking.CancelledAt:yyyyMMddHHmm}",
            new Dictionary<string, object?>
            {
                ["tutorName"] = tutorName,
                ["sessionTime"] = sessionTime,
                ["cancelledBy"] = actorRole,
                ["refundIssued"] = fullRefund ? "true" : "false",
                ["message"] = cancellationMessage
            }, ct);

        if (booking.TutorProfile?.ExpertUserId is not null)
        {
            await notificationService.CreateForExpertAsync(
                NotificationEventKey.ExpertPrivateSpeakingCancelled,
                booking.TutorProfile.ExpertUserId,
                "private_speaking_booking", booking.Id,
                $"cancel-{booking.CancelledAt:yyyyMMddHHmm}",
                new Dictionary<string, object?>
                {
                    ["sessionTime"] = sessionTime,
                    ["cancelledBy"] = actorRole,
                    ["refundIssued"] = fullRefund ? "true" : "false"
                }, ct);
        }

        return (true, null);
    }

    public async Task<BookingCheckoutResult> RescheduleBookingAsync(
        string bookingId,
        string learnerUserId,
        DateTimeOffset newSessionStartUtc,
        string learnerTimezone,
        string? learnerNotes,
        string idempotencyKey,
        CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        if (!config.IsEnabled)
            return BookingCheckoutResult.Fail("Private Speaking Sessions are currently disabled.");
        if (!config.AllowReschedule)
            return BookingCheckoutResult.Fail("Rescheduling is not currently enabled.");
        if (!IsUsableIdempotencyKey(idempotencyKey))
            return BookingCheckoutResult.Fail("A valid reschedule idempotency key is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var idempotencyScope = BuildIdempotencyScopeKey("reschedule", learnerUserId, bookingId, idempotencyKey);
        var idempotencyPrefix = BuildScopedIdempotencyPrefix(idempotencyScope);
        var scopedIdempotencyKey = BuildScopedIdempotencyKey(
            idempotencyScope,
            newSessionStartUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            learnerTimezone,
            learnerNotes ?? string.Empty);

        var existingByIdempotency = await db.PrivateSpeakingBookings
            .FirstOrDefaultAsync(item => item.LearnerUserId == learnerUserId
                && item.IdempotencyKey != null
                && item.IdempotencyKey.StartsWith(idempotencyPrefix), ct);
        if (existingByIdempotency is not null)
        {
            if (!string.Equals(existingByIdempotency.IdempotencyKey, scopedIdempotencyKey, StringComparison.Ordinal))
            {
                return BookingCheckoutResult.Fail("This idempotency key was already used with different reschedule details. Please retry with a new key.");
            }

            await transaction.CommitAsync(ct);
            return new BookingCheckoutResult(
                true,
                null,
                existingByIdempotency.Id,
                existingByIdempotency.StripeCheckoutSessionId,
                null,
                existingByIdempotency.EntitlementConsumed);
        }

        var original = await db.PrivateSpeakingBookings
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct);
        if (original is null || original.LearnerUserId != learnerUserId)
            return BookingCheckoutResult.Fail("Booking not found.");
        if (original.Status is not (PrivateSpeakingBookingStatus.Confirmed or PrivateSpeakingBookingStatus.ZoomCreated))
            return BookingCheckoutResult.Fail("Only confirmed upcoming sessions can be rescheduled.");
        if (original.RescheduledToBookingId is not null)
            return BookingCheckoutResult.Fail("This booking has already been rescheduled.");

        var now = timeProvider.GetUtcNow();

        // PDF §2.3 / §9: any time before the session starts, the learner may
        // reschedule only to a currently available tutor-calendar slot.
        if (now >= original.SessionStartUtc)
            return BookingCheckoutResult.Fail("This session has already started and can no longer be rescheduled.");

        var profile = original.TutorProfile
            ?? await db.PrivateSpeakingTutorProfiles.FindAsync([original.TutorProfileId], ct);
        if (profile is null || !profile.IsActive)
            return BookingCheckoutResult.Fail("Tutor is not available.");

        var durationMinutes = original.DurationMinutes;
        if (newSessionStartUtc > now.AddDays(config.MaxBookingAdvanceDays))
            return BookingCheckoutResult.Fail($"Sessions cannot be rescheduled more than {config.MaxBookingAdvanceDays} days in advance.");

        if (!await IsRequestedSlotAvailableAsync(profile, newSessionStartUtc, durationMinutes, ct, original.Id))
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var newSessionEndUtc = newSessionStartUtc.AddMinutes(durationMinutes);
        var newSessionStartWithBuffer = newSessionStartUtc.AddMinutes(-config.BufferMinutesBetweenSlots);
        var newSessionEndWithBuffer = newSessionEndUtc.AddMinutes(config.BufferMinutesBetweenSlots);
        var hasTutorConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.Id != original.Id
            && b.TutorProfileId == original.TutorProfileId
            && b.SessionStartUtc < newSessionEndWithBuffer
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > newSessionStartWithBuffer
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);
        if (hasTutorConflict)
            return BookingCheckoutResult.Fail("This time slot is no longer available. Please select another slot.");

        var learnerConflict = await db.PrivateSpeakingBookings.AnyAsync(b =>
            b.Id != original.Id
            && b.LearnerUserId == learnerUserId
            && b.SessionStartUtc < newSessionEndUtc
            && b.SessionStartUtc.AddMinutes(b.DurationMinutes) > newSessionStartUtc
            && b.Status != PrivateSpeakingBookingStatus.Cancelled
            && b.Status != PrivateSpeakingBookingStatus.Expired
            && b.Status != PrivateSpeakingBookingStatus.Failed
            && b.Status != PrivateSpeakingBookingStatus.Refunded, ct);
        if (learnerConflict)
            return BookingCheckoutResult.Fail("You already have a booking at this time. Please select a different slot.");

        var calendarBusy = await calendarService.CheckBusyAsync(original.TutorProfileId, newSessionStartUtc, newSessionEndUtc, ct);
        if (calendarBusy.Connected)
        {
            if (calendarBusy.Error is not null)
                return BookingCheckoutResult.Fail("Tutor calendar availability could not be verified. Please try another slot shortly.");
            if (calendarBusy.IsBusy)
                return BookingCheckoutResult.Fail("Tutor calendar shows this slot is no longer available. Please select another slot.");
        }

        // The replacement is always free. The only eligibility condition is
        // that the original session has not started and the target slot is
        // currently available in the tutor calendar.
        var freeReplacement = new PrivateSpeakingBooking
            {
                Id = $"psb-{Guid.NewGuid():N}",
                LearnerUserId = learnerUserId,
                TutorProfileId = original.TutorProfileId,
                Status = PrivateSpeakingBookingStatus.Confirmed,
                SessionStartUtc = newSessionStartUtc,
                DurationMinutes = durationMinutes,
                TutorTimezone = profile.Timezone,
                LearnerTimezone = learnerTimezone,
                PriceMinorUnits = original.PriceMinorUnits,
                Currency = original.Currency,
                PaymentStatus = original.PaymentStatus,
                PaymentConfirmedAt = original.PaymentConfirmedAt,
                EntitlementSubscriptionId = original.EntitlementSubscriptionId,
                EntitlementConsumed = original.EntitlementConsumed,
                EntitlementConsumedAt = original.EntitlementConsumedAt,
                StripeCheckoutSessionId = null,
                LearnerNotes = learnerNotes ?? original.LearnerNotes,
                IdempotencyKey = scopedIdempotencyKey,
                RescheduledFromBookingId = original.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.PrivateSpeakingBookings.Add(freeReplacement);
            original.Status = PrivateSpeakingBookingStatus.Cancelled;
            original.CancelledBy = learnerUserId;
            original.CancellationReason = "rescheduled";
            original.CancelledAt = now;
            original.RescheduledToBookingId = freeReplacement.Id;
            original.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            await AuditAsync(original.Id, learnerUserId, "learner", "booking_rescheduled_from", freeReplacement.Id, ct);
            await AuditAsync(freeReplacement.Id, learnerUserId, "learner", "booking_rescheduled_to", original.Id, ct);
            QueueCalendarSyncJob(original.Id);
            QueueBookingPostCommitJobs(freeReplacement.Id, includeCalendarSync: true);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

        return new BookingCheckoutResult(
            true,
            null,
            freeReplacement.Id,
            null,
            null,
            freeReplacement.EntitlementConsumed,
            null);
    }
}
