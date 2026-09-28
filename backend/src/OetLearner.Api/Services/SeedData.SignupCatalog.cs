using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    private static async Task<bool> EnsureSignupCatalogAsync(
        LearnerDbContext db,
        CancellationToken cancellationToken)
    {
        var existingExamTypeIds = (await db.SignupExamTypeCatalog
            .AsNoTracking()
            .Select(row => row.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var existingProfessionIds = (await db.SignupProfessionCatalog
            .AsNoTracking()
            .Select(row => row.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var existingSessionIds = (await db.SignupSessionCatalog
            .AsNoTracking()
            .Select(row => row.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return SeedSignupCatalog(db, existingExamTypeIds, existingProfessionIds, existingSessionIds);
    }

    private static bool AddMissingSignupRows<T>(
        DbSet<T> set,
        IEnumerable<T> rows,
        HashSet<string> existingIds,
        Func<T, string> idSelector)
        where T : class
    {
        var changed = false;
        foreach (var row in rows)
        {
            if (!existingIds.Add(idSelector(row)))
            {
                continue;
            }

            set.Add(row);
            changed = true;
        }

        return changed;
    }

    private static bool SeedSignupCatalog(
        LearnerDbContext db,
        HashSet<string> existingExamTypeIds,
        HashSet<string> existingProfessionIds,
        HashSet<string> existingSessionIds)
    {
        var changed = false;

        changed |= AddMissingSignupRows(
            db.SignupExamTypeCatalog,
            new[]
            {
            new SignupExamTypeCatalog
            {
                Id = "oet",
                Label = "OET",
                Code = "OET",
                Description = "Occupational English Test preparation and enrollment.",
                SortOrder = 1,
                IsActive = true
            },
            new SignupExamTypeCatalog
            {
                Id = "ielts",
                Label = "IELTS",
                Code = "IELTS",
                Description = "IELTS preparation and session enrollment.",
                SortOrder = 2,
                IsActive = true
            }
            },
            existingExamTypeIds,
            static row => row.Id);

        changed |= AddMissingSignupRows(
            db.SignupProfessionCatalog,
            new[]
            {
            new SignupProfessionCatalog
            {
                Id = "nursing",
                Label = "Nursing",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Registered nurse and clinical nursing candidates.",
                SortOrder = 1,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "medicine",
                Label = "Medicine",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Doctors and physicians preparing for healthcare pathways.",
                SortOrder = 2,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "pharmacy",
                Label = "Pharmacy",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Pharmacists and pharmacy practice candidates.",
                SortOrder = 3,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "dentistry",
                Label = "Dentistry",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Dental professionals and dentistry applicants.",
                SortOrder = 4,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "physiotherapy",
                Label = "Physiotherapy",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Physiotherapists and physical therapy candidates.",
                SortOrder = 5,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "radiography",
                Label = "Radiography",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Radiographers and medical imaging candidates.",
                SortOrder = 6,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "other-allied-health",
                Label = "Modified Allied Health Profession",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "oet" }),
                Description = "Other allied health professionals (occupational therapy, dietetics, speech pathology, podiatry, optometry, etc.).",
                SortOrder = 7,
                IsActive = true
            },
            new SignupProfessionCatalog
            {
                Id = "academic-english",
                Label = "Academic / General English",
                CountryTargetsJson = "[]",
                ExamTypeIdsJson = JsonSupport.Serialize(new[] { "ielts" }),
                Description = "General academic and migration IELTS candidates.",
                SortOrder = 8,
                IsActive = true
            }
            },
            existingProfessionIds,
            static row => row.Id);

        changed |= AddMissingSignupRows(
            db.SignupSessionCatalog,
            new[]
            {
            new SignupSessionCatalog
            {
                Id = "session-oet-nursing-apr",
                Name = "OET Nursing April Cohort",
                ExamTypeId = "oet",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "nursing" }),
                PriceLabel = "$299",
                StartDate = "2026-04-06",
                EndDate = "2026-06-28",
                DeliveryMode = "online",
                Capacity = 40,
                SeatsRemaining = 11,
                SortOrder = 1,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-oet-medicine-may",
                Name = "OET Medicine Intensive",
                ExamTypeId = "oet",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "medicine", "dentistry", "pharmacy" }),
                PriceLabel = "$349",
                StartDate = "2026-05-11",
                EndDate = "2026-07-05",
                DeliveryMode = "hybrid",
                Capacity = 32,
                SeatsRemaining = 9,
                SortOrder = 2,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-ielts-foundation-apr",
                Name = "IELTS Foundation Sprint",
                ExamTypeId = "ielts",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "academic-english" }),
                PriceLabel = "$199",
                StartDate = "2026-04-20",
                EndDate = "2026-06-01",
                DeliveryMode = "online",
                Capacity = 60,
                SeatsRemaining = 21,
                SortOrder = 3,
                IsActive = true
            },
            new SignupSessionCatalog
            {
                Id = "session-ielts-weekend-may",
                Name = "IELTS Weekend Cohort",
                ExamTypeId = "ielts",
                ProfessionIdsJson = JsonSupport.Serialize(new[] { "academic-english" }),
                PriceLabel = "$239",
                StartDate = "2026-05-23",
                EndDate = "2026-07-19",
                DeliveryMode = "online",
                Capacity = 45,
                SeatsRemaining = 0,
                SortOrder = 4,
                IsActive = true
            }
            },
            existingSessionIds,
            static row => row.Id);

        return changed;
    }
}
