using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services;

/// <summary>
/// Maps the internal <see cref="PrivateSpeakingBookingStatus"/> lifecycle onto the
/// public PDF <c>SpeakingSessionStatus</c> vocabulary (PDF §2.2 / §8). A booking that
/// has been rescheduled always surfaces as "Rescheduled" for the original record,
/// regardless of whether its underlying status is an active or cancelled state.
/// </summary>
public static class SpeakingSessionStatusMapper
{
    public static string Map(PrivateSpeakingBooking b)
    {
        return b.Status switch
        {
            PrivateSpeakingBookingStatus.Reserved
                or PrivateSpeakingBookingStatus.PendingPayment => "PendingPayment",

            PrivateSpeakingBookingStatus.Confirmed
                or PrivateSpeakingBookingStatus.ZoomPending
                or PrivateSpeakingBookingStatus.ZoomCreated
                or PrivateSpeakingBookingStatus.InProgress
                    => b.RescheduledToBookingId is not null ? "Rescheduled" : "Confirmed",

            PrivateSpeakingBookingStatus.Completed => "Completed",

            PrivateSpeakingBookingStatus.NoShow => "NoShow",

            PrivateSpeakingBookingStatus.Cancelled => b.RescheduledToBookingId is not null
                ? "Rescheduled"
                : b.RefundIssued ? "CancelledWithRefund" : "CancelledWithoutRefund",

            PrivateSpeakingBookingStatus.Refunded => "CancelledWithRefund",

            PrivateSpeakingBookingStatus.Expired
                or PrivateSpeakingBookingStatus.Failed => "CancelledWithoutRefund",

            _ => "CancelledWithoutRefund"
        };
    }
}
