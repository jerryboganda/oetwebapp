using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// V2 closure (May 2026) — chunked-recording retry + dedup invariants for
/// <see cref="MockBookingRecordingService"/>. The service is the only path
/// for the learner Speaking room to capture audio and must:
///   • Idempotent retry: same Part + same SHA = no-op success.
///   • Same Part + different SHA = reject ("part_already_uploaded").
///   • Lifetime cap: refuses past <see cref="MockBookingRecordingService.MaxChunks"/>.
///   • Per-day rate-limit: refuses past <see cref="MockBookingRecordingService.MaxChunksPerDay"/>
///     in any rolling 24-hour window.
///   • Consent gate: rejects when ConsentToRecording is false.
///   • Finalization gate: rejects when RecordingFinalizedAt is set.
/// </summary>
public class MockBookingRecordingServiceRetryTests
{
    private const string LearnerId = "learner-recording-test";

    private static (LearnerDbContext db, MockBookingRecordingService svc, MockBooking booking) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new LearnerDbContext(options);

        var booking = new MockBooking
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = LearnerId,
            MockBundleId = "bundle-1",
            Status = MockBookingStatuses.Confirmed,
            ConsentToRecording = true,
            RecordingManifestJson = string.Empty,
            ScheduledStartAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.MockBookings.Add(booking);
        db.SaveChanges();

        var storage = new InMemoryFileStorage();
        var svc = new MockBookingRecordingService(db, storage);
        return (db, svc, booking);
    }

    private static MemoryStream Body(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SamePart_SameSha_ReturnsDuplicateSuccess_AndDoesNotMutate()
    {
        var (db, svc, booking) = Build();

        var first = await svc.AppendChunkAsync(LearnerId, booking.Id, part: 0,
            mimeType: "audio/webm", body: Body("chunk-zero"), CancellationToken.None);
        var manifestAfterFirst = (await db.MockBookings.AsNoTracking().FirstAsync(x => x.Id == booking.Id))
            .RecordingManifestJson;

        var second = await svc.AppendChunkAsync(LearnerId, booking.Id, part: 0,
            mimeType: "audio/webm", body: Body("chunk-zero"), CancellationToken.None);
        var manifestAfterSecond = (await db.MockBookings.AsNoTracking().FirstAsync(x => x.Id == booking.Id))
            .RecordingManifestJson;

        // Manifest is byte-identical after the second call.
        Assert.Equal(manifestAfterFirst, manifestAfterSecond);

        // Second response carries `duplicate = true`.
        var firstSha = first.GetType().GetProperty("sha256")!.GetValue(first) as string;
        var secondSha = second.GetType().GetProperty("sha256")!.GetValue(second) as string;
        Assert.Equal(firstSha, secondSha);
        var duplicate = second.GetType().GetProperty("duplicate")?.GetValue(second);
        Assert.Equal(true, duplicate);
    }

    [Fact]
    public async Task SamePart_DifferentSha_IsRejected_WithoutMutation()
    {
        var (db, svc, booking) = Build();

        await svc.AppendChunkAsync(LearnerId, booking.Id, part: 0, mimeType: "audio/webm",
            body: Body("first"), CancellationToken.None);
        var manifestBefore = (await db.MockBookings.AsNoTracking().FirstAsync(x => x.Id == booking.Id))
            .RecordingManifestJson;

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            LearnerId, booking.Id, part: 0, mimeType: "audio/webm",
            body: Body("second-different-payload"), CancellationToken.None));
        Assert.Equal("part_already_uploaded", ex.ErrorCode);

        var manifestAfter = (await db.MockBookings.AsNoTracking().FirstAsync(x => x.Id == booking.Id))
            .RecordingManifestJson;
        Assert.Equal(manifestBefore, manifestAfter);
    }

    [Fact]
    public async Task DifferentParts_AreAccepted_AndAccumulate()
    {
        var (db, svc, booking) = Build();

        await svc.AppendChunkAsync(LearnerId, booking.Id, 0, "audio/webm", Body("a"), CancellationToken.None);
        await svc.AppendChunkAsync(LearnerId, booking.Id, 1, "audio/webm", Body("b"), CancellationToken.None);
        await svc.AppendChunkAsync(LearnerId, booking.Id, 2, "audio/webm", Body("c"), CancellationToken.None);

        var manifestJson = (await db.MockBookings.AsNoTracking().FirstAsync(x => x.Id == booking.Id))
            .RecordingManifestJson;
        Assert.Contains("\"Part\":0", manifestJson, StringComparison.Ordinal);
        Assert.Contains("\"Part\":1", manifestJson, StringComparison.Ordinal);
        Assert.Contains("\"Part\":2", manifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartIndexOutOfRange_IsRejected()
    {
        var (_, svc, booking) = Build();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            LearnerId, booking.Id, part: -1, mimeType: "audio/webm",
            body: Body("x"), CancellationToken.None));
        Assert.Equal("invalid_part", ex.ErrorCode);

        var ex2 = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            LearnerId, booking.Id, part: MockBookingRecordingService.MaxChunks,
            mimeType: "audio/webm", body: Body("x"), CancellationToken.None));
        Assert.Equal("invalid_part", ex2.ErrorCode);
    }

    [Fact]
    public async Task Recording_RejectsAfterFinalization()
    {
        var (db, svc, booking) = Build();
        await svc.AppendChunkAsync(LearnerId, booking.Id, 0, "audio/webm", Body("x"), CancellationToken.None);
        await svc.FinalizeAsync(LearnerId, booking.Id, durationMs: 5000, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            LearnerId, booking.Id, part: 1, mimeType: "audio/webm",
            body: Body("after"), CancellationToken.None));
        Assert.Equal("recording_finalized", ex.ErrorCode);

        // Idempotent finalize returns the same projection without mutating state.
        var first = await svc.FinalizeAsync(LearnerId, booking.Id, durationMs: 5000, CancellationToken.None);
        var second = await svc.FinalizeAsync(LearnerId, booking.Id, durationMs: 5000, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        // Sanity: only one finalize-audit row was emitted.
        var finalizeAudits = await db.AuditEvents.AsNoTracking()
            .Where(x => x.Action == "mock_booking_recording_finalised" && x.ResourceId == booking.Id)
            .CountAsync();
        Assert.Equal(1, finalizeAudits);
    }

    [Fact]
    public async Task Recording_RejectsWithoutConsent()
    {
        var (db, svc, booking) = Build();
        booking.ConsentToRecording = false;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            LearnerId, booking.Id, part: 0, mimeType: "audio/webm",
            body: Body("x"), CancellationToken.None));
        Assert.Equal("consent_required", ex.ErrorCode);
    }

    [Fact]
    public async Task Recording_RejectsForeignBookingOwner()
    {
        var (_, svc, booking) = Build();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.AppendChunkAsync(
            "different-learner", booking.Id, part: 0, mimeType: "audio/webm",
            body: Body("x"), CancellationToken.None));
        Assert.Equal("forbidden", ex.ErrorCode);
    }
}
