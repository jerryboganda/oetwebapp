using System.Data.Common;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Tests;

public sealed class ContentBulkImportPerformanceTests
{
    [Fact]
    public async Task Commit_preloads_media_sha_once_and_saves_reference_batch_once()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var commands = new CommandCounter();
        var saves = new SaveCounter();
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commands, saves)
            .Options;
        await using var db = new LearnerDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();
        commands.Clear();
        saves.Clear();
        var storage = new InMemoryFileStorage();
        var parser = new ContentConventionParser();
        var service = new ContentBulkImportService(
            db,
            storage,
            parser,
            new ContentPaperService(db),
            Options.Create(new StorageOptions
            {
                LocalRootPath = "/tmp",
                ContentUpload = new ContentUploadOptions(),
            }),
            scanner: null,
            NullLogger<ContentBulkImportService>.Instance);
        await using var zip = BuildDuplicateTemplateZip(entryCount: 4);
        var session = await service.StagePayloadAsync(
            "admin-batch", zip, "templates.zip", CancellationToken.None);
        Assert.Equal(4, session.Manifest.References.Count);
        var approvals = session.Manifest.References
            .Select(reference => new BulkImportApproval(
                reference.ProposalId,
                Approve: true,
                OverrideTitle: null,
                OverrideProfessionId: null,
                OverrideAppliesToAllProfessions: null,
                OverrideCardType: null,
                OverrideLetterType: null,
                OverrideSourceProvenance: null))
            .ToList();
        commands.Clear();
        saves.Clear();

        var result = await service.CommitAsync(
            "admin-batch", session.SessionId, approvals,
            CancellationToken.None);

        var mediaQueries = commands.SelectsFor("MediaAssets");
        var mediaQuery = Assert.Single(mediaQueries);
        Assert.Contains("Sha256", mediaQuery, StringComparison.Ordinal);
        Assert.Equal(1, saves.Count);
        Assert.Equal(4, result.CreatedReferenceCount);
        Assert.Equal(3, result.DeduplicatedAssetCount);
        Assert.Equal(1, await db.MediaAssets.CountAsync());
        Assert.Equal(4, await db.ResultTemplateAssets.CountAsync());
        Assert.All(
            await db.ResultTemplateAssets.ToListAsync(),
            template => Assert.False(template.IsActive));
        Assert.Equal(
            4,
            await db.AuditEvents.CountAsync(
                audit => audit.Action == "BulkImportResultTemplateImported"));
    }

    private static MemoryStream BuildDuplicateTemplateZip(int entryCount)
    {
        var zipBuffer = new MemoryStream();
        using (var archive = new ZipArchive(
                   zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var payload = Encoding.ASCII.GetBytes(
                "RIFF0000WEBPduplicate-template-content");
            for (var index = 0; index < entryCount; index++)
            {
                var entry = archive.CreateEntry(
                    $"Result Templates/Template {index + 1}.webp",
                    CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(payload);
            }
        }
        zipBuffer.Position = 0;
        return zipBuffer;
    }

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }
        public void Clear() => Count = 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public void Clear() => _commands.Clear();

        public IReadOnlyList<string> SelectsFor(string table)
            => _commands
                .Where(command =>
                    command.TrimStart().StartsWith(
                        "SELECT", StringComparison.OrdinalIgnoreCase)
                    && command.Contains(
                        $"FROM \"{table}\"", StringComparison.OrdinalIgnoreCase))
                .ToList();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
