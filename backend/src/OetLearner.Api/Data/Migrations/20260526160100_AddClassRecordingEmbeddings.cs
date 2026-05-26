using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260526160100_AddClassRecordingEmbeddings")]
    public partial class AddClassRecordingEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wave A2 — transcript chunk embeddings for the "Ask AI about this
            // class" RAG retrieval surface. EmbeddingJson holds a JSON array of
            // 1536 floats; v2 should migrate to pgvector(1536) once available.
            migrationBuilder.CreateTable(
                name: "ClassRecordingEmbeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassRecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkText = table.Column<string>(type: "text", nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    EndTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassRecordingEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassRecordingEmbeddings_LiveClassRecordings_ClassRecordingId",
                        column: x => x.ClassRecordingId,
                        principalTable: "LiveClassRecordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId",
                table: "ClassRecordingEmbeddings",
                column: "ClassRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassRecordingEmbeddings_ClassRecordingId_ChunkIndex",
                table: "ClassRecordingEmbeddings",
                columns: new[] { "ClassRecordingId", "ChunkIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ClassRecordingEmbeddings");
        }
    }
}
