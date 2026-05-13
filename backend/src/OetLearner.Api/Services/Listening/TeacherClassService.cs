using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Cross-skill teacher class CRUD + roster queries. Every read and write
/// path enforces <c>OwnerUserId == currentUserId</c> server-side (OWASP A01
/// — broken access control was the highest-rated risk in PRD §6).
/// </summary>
public sealed class TeacherClassService
{
    private readonly LearnerDbContext _db;
    private readonly TimeProvider _clock;

    public TeacherClassService(LearnerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TeacherClassDto>> ListMineAsync(string ownerUserId, CancellationToken ct)
    {
        var rows = await _db.TeacherClasses
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);
        var counts = await _db.TeacherClassMembers
            .Where(m => rows.Select(r => r.Id).Contains(m.TeacherClassId))
            .GroupBy(m => m.TeacherClassId)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var lookup = counts.ToDictionary(c => c.ClassId, c => c.Count);
        return rows.Select(c => new TeacherClassDto(
            c.Id, c.Name, c.Description,
            lookup.TryGetValue(c.Id, out var n) ? n : 0,
            c.CreatedAt, c.UpdatedAt)).ToList();
    }

    public async Task<TeacherClassDto> CreateAsync(string ownerUserId, string name, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Class name is required.", nameof(name));

        var now = _clock.GetUtcNow();
        var row = new TeacherClass
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            Description = description?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.TeacherClasses.Add(row);
        await _db.SaveChangesAsync(ct);
        return new TeacherClassDto(row.Id, row.Name, row.Description, 0, row.CreatedAt, row.UpdatedAt);
    }

    public async Task DeleteAsync(string ownerUserId, string classId, CancellationToken ct)
    {
        var row = await LoadOwnedAsync(classId, ownerUserId, ct);
        _db.TeacherClasses.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddMemberAsync(string ownerUserId, string classId, string memberUserId, CancellationToken ct)
    {
        await LoadOwnedAsync(classId, ownerUserId, ct);
        if (string.IsNullOrWhiteSpace(memberUserId))
        {
            throw new ArgumentException("Member user id is required.", nameof(memberUserId));
        }

        var normalizedMemberUserId = memberUserId.Trim();
        var memberExists = await _db.Users.AsNoTracking()
            .AnyAsync(user => user.Id == normalizedMemberUserId && user.AccountStatus == "active", ct);
        if (!memberExists)
        {
            throw new KeyNotFoundException($"Learner {normalizedMemberUserId} not found.");
        }

        var exists = await _db.TeacherClassMembers
            .AnyAsync(m => m.TeacherClassId == classId && m.UserId == normalizedMemberUserId, ct);
        if (exists) return;
        _db.TeacherClassMembers.Add(new TeacherClassMember
        {
            Id = Guid.NewGuid().ToString("N"),
            TeacherClassId = classId,
            UserId = normalizedMemberUserId,
            AddedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(string ownerUserId, string classId, string memberUserId, CancellationToken ct)
    {
        await LoadOwnedAsync(classId, ownerUserId, ct);
        var row = await _db.TeacherClassMembers
            .FirstOrDefaultAsync(m => m.TeacherClassId == classId && m.UserId == memberUserId, ct);
        if (row is null) return;
        _db.TeacherClassMembers.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListMemberUserIdsAsync(string ownerUserId, string classId, CancellationToken ct)
    {
        await LoadOwnedAsync(classId, ownerUserId, ct);
        return await _db.TeacherClassMembers
            .Where(m => m.TeacherClassId == classId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
    }

    private async Task<TeacherClass> LoadOwnedAsync(string classId, string ownerUserId, CancellationToken ct)
    {
        var row = await _db.TeacherClasses.FirstOrDefaultAsync(c => c.Id == classId && c.OwnerUserId == ownerUserId, ct)
            ?? throw new KeyNotFoundException($"Class {classId} not found.");
        return row;
    }
}

public sealed record TeacherClassDto(
    string Id, string Name, string? Description, int MemberCount,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
