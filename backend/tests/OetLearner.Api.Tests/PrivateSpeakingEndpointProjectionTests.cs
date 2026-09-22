using System.Reflection;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;

namespace OetLearner.Api.Tests;

public sealed class PrivateSpeakingEndpointProjectionTests
{
    [Fact]
    public void LearnerBookingDetail_DoesNotExposeZoomHostFields()
    {
        var booking = NewBooking();

        var learnerProjection = InvokeMapper("MapLearnerBookingDetailResponse", booking);

        Assert.DoesNotContain("ZoomStartUrl", ProjectionPropertyNames(learnerProjection));
        Assert.DoesNotContain("ZoomMeetingPassword", ProjectionPropertyNames(learnerProjection));
        Assert.DoesNotContain("ZoomJoinUrl", ProjectionPropertyNames(learnerProjection));
    }

    [Fact]
    public void ExpertBookingDetail_DoesNotExposeRawZoomUrlsOrMeetingPassword()
    {
        var booking = NewBooking();

        var expertProjection = InvokeMapper("MapExpertBookingDetailResponse", booking);

        Assert.DoesNotContain("ZoomJoinUrl", ProjectionPropertyNames(expertProjection));
        Assert.DoesNotContain("ZoomStartUrl", ProjectionPropertyNames(expertProjection));
        Assert.DoesNotContain("ZoomMeetingPassword", ProjectionPropertyNames(expertProjection));
    }

    [Fact]
    public void AdminBookingDetail_NoLongerExposesRetiredZoomFields()
    {
        // Zoom provisioning is retired for Private Speaking (LiveKit rewrite,
        // commit 74fdadd80): no booking is ever stamped with real Zoom
        // URLs/password again, so the admin projection dropped these columns
        // rather than keep permanently-null legacy fields. Operational access
        // now goes through the LiveKit room lifecycle (LiveTutorRoomLifecycleWorker
        // / SpeakingLiveRoomService), not this endpoint.
        var booking = NewBooking();

        var adminProjection = InvokeMapper("MapAdminBookingDetailResponse", booking);

        Assert.DoesNotContain("ZoomStartUrl", ProjectionPropertyNames(adminProjection));
        Assert.DoesNotContain("ZoomMeetingPassword", ProjectionPropertyNames(adminProjection));
        Assert.DoesNotContain("ZoomJoinUrl", ProjectionPropertyNames(adminProjection));
    }

    private static PrivateSpeakingBooking NewBooking()
        => new()
        {
            Id = "booking-1",
            LearnerUserId = "learner-1",
            TutorProfileId = "tutor-profile-1",
            Status = PrivateSpeakingBookingStatus.ZoomCreated,
            SessionStartUtc = DateTimeOffset.UtcNow.AddDays(1),
            DurationMinutes = 60,
            TutorTimezone = "UTC",
            LearnerTimezone = "UTC",
            PriceMinorUnits = 6900,
            Currency = "USD",
            PaymentStatus = PrivateSpeakingPaymentStatus.Succeeded,
            ZoomStatus = PrivateSpeakingZoomStatus.Created,
            ZoomJoinUrl = "https://zoom.us/j/learner",
            ZoomStartUrl = "https://zoom.us/s/host-secret",
            ZoomMeetingPassword = "host-password",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static object InvokeMapper(string methodName, PrivateSpeakingBooking booking)
    {
        var mapper = typeof(PrivateSpeakingEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Mapper {methodName} was not found.");
        return mapper.Invoke(null, [booking]) ?? throw new InvalidOperationException($"Mapper {methodName} returned null.");
    }

    private static string[] ProjectionPropertyNames(object projection)
        => projection.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).ToArray();
}