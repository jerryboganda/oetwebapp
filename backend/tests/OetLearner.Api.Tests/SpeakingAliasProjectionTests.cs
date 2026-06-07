using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// T3b — unit tests for the pure PDF booking projection used by the Speaking
/// alias route surface (<see cref="SpeakingAliasEndpoints.ToPdfResponse"/>).
/// Guards that the public <c>SpeakingSessionStatus</c> vocabulary is what clients
/// see, that the end time is derived from start + duration, and that the
/// profession track is surfaced. No web host needed — ToPdfResponse is a pure
/// function over a <see cref="PrivateSpeakingBooking"/>.
/// </summary>
public class SpeakingAliasProjectionTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 14, 30, 0, TimeSpan.Zero);

    private static PrivateSpeakingBooking NewBooking() => new()
    {
        Id = "psb-test",
        LearnerUserId = "learner-1",
        TutorProfileId = "tutor-profile-1",
        TutorProfile = new PrivateSpeakingTutorProfile { Id = "tutor-profile-1", DisplayName = "Dr. Vega" },
        SessionStartUtc = Start,
        DurationMinutes = 45,
        TutorTimezone = "Europe/London",
        LearnerTimezone = "Asia/Karachi",
        ProfessionTrack = "Nursing",
        PriceMinorUnits = 5000,
        Currency = "GBP",
        PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
        ZoomStatus = PrivateSpeakingZoomStatus.Created,
    };

    [Fact]
    public void Confirmed_Booking_Projects_Confirmed_Status_And_EndTime()
    {
        var booking = NewBooking();
        booking.Status = PrivateSpeakingBookingStatus.Confirmed;

        var dto = SpeakingAliasEndpoints.ToPdfResponse(booking);

        Assert.Equal("Confirmed", dto.Status);
        Assert.Equal(Start, dto.ScheduledStartAt);
        Assert.Equal(Start.AddMinutes(45), dto.ScheduledEndAt);
        Assert.Equal(45, dto.DurationMinutes);
        // ProfessionTrack must be surfaced through the projection.
        Assert.Equal("Nursing", dto.ProfessionTrack);
        // Sanity on a couple of passthrough fields.
        Assert.Equal("Dr. Vega", dto.TutorName);
        Assert.Equal("Asia/Karachi", dto.TimeZone);
        Assert.Equal("Europe/London", dto.TutorTimezone);
    }

    [Fact]
    public void Cancelled_With_Refund_Projects_CancelledWithRefund()
    {
        var booking = NewBooking();
        booking.Status = PrivateSpeakingBookingStatus.Cancelled;
        booking.RefundIssued = true;
        booking.RefundAmountMinorUnits = 5000;

        var dto = SpeakingAliasEndpoints.ToPdfResponse(booking);

        Assert.Equal("CancelledWithRefund", dto.Status);
        Assert.True(dto.RefundIssued);
        Assert.Equal(5000, dto.RefundAmountMinorUnits);
    }

    [Fact]
    public void Cancelled_Without_Refund_Projects_CancelledWithoutRefund()
    {
        var booking = NewBooking();
        booking.Status = PrivateSpeakingBookingStatus.Cancelled;
        booking.RefundIssued = false;

        var dto = SpeakingAliasEndpoints.ToPdfResponse(booking);

        Assert.Equal("CancelledWithoutRefund", dto.Status);
        Assert.False(dto.RefundIssued);
    }

    [Fact]
    public void NoShow_Booking_Projects_NoShow()
    {
        var booking = NewBooking();
        booking.Status = PrivateSpeakingBookingStatus.NoShow;

        var dto = SpeakingAliasEndpoints.ToPdfResponse(booking);

        Assert.Equal("NoShow", dto.Status);
    }
}
