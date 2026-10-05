using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Live Class recordings larger than the 24 MiB in-memory transcription cap (OET-RWP/1 section 6.3, job kind
    /// <c>media.audio-extract</c>): adds the nullable <c>AudioChunksJson</c> (text) to <c>LiveClassRecordings</c>. It holds the
    /// chunk manifest a remote helper produced (storage keys of transcription-sized mp3 chunks plus the transcript of each chunk as
    /// it is produced). Null for every existing and every small recording, so nothing reads it until the
    /// <c>remote_jobs_kind_media_audio_extract</c> flag is on and a node offers the kind. Additive and idempotent; the blue/green
    /// slots overlap and the old slot neither reads nor writes the column.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file; the matching property is added
    /// to <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270111090000_AddLiveClassRecordingAudioChunks")]
    public partial class AddLiveClassRecordingAudioChunks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"LiveClassRecordings\" ADD COLUMN IF NOT EXISTS \"AudioChunksJson\" text;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"LiveClassRecordings\" DROP COLUMN IF EXISTS \"AudioChunksJson\";");
        }
    }
}
