using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "ListeningAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RowVersion",
                table: "ContentPapers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "ApplicationUserAccounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProductType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StripeProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrossSellRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuggestedProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossSellRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentPeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    CanceledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PausedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveClasses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ProfessionTrack = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TutorProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TutorDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefaultCapacity = table.Column<int>(type: "integer", nullable: false),
                    CreditCost = table.Column<int>(type: "integer", nullable: false),
                    PriceUsd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    RecurrenceJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TagsJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClasses_PrivateSpeakingTutorProfiles_TutorProfileId",
                        column: x => x.TutorProfileId,
                        principalTable: "PrivateSpeakingTutorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassWaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    JoinedWaitlistAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotifiedOfOpening = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassWaitlistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IntervalCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPrices_BillingProducts_BillingProductId",
                        column: x => x.BillingProductId,
                        principalTable: "BillingProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppliedPromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedPromoCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppliedPromoCodes_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LiveClassId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScheduledStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScheduledEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ZoomMeetingId = table.Column<long>(type: "bigint", nullable: true),
                    ZoomMeetingNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomJoinUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomStartUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomPasscode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ZoomError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZoomRetryCount = table.Column<int>(type: "integer", nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    RecordingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassSessions_LiveClasses_LiveClassId",
                        column: x => x.LiveClassId,
                        principalTable: "LiveClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItems_BillingPrices_BillingPriceId",
                        column: x => x.BillingPriceId,
                        principalTable: "BillingPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_BillingProducts_BillingProductId",
                        column: x => x.BillingProductId,
                        principalTable: "BillingProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_CartId",
                        column: x => x.CartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassAttendances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnrollmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ZoomParticipantUuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReceivedRecordingAccess = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassAttendances_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassEnrollments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreditsCharged = table.Column<int>(type: "integer", nullable: false),
                    WalletTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefundWalletTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassEnrollments_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveClassRecordings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClassSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ZoomRecordingId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    S3VideoKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    S3AudioKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    S3TranscriptKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: true),
                    AiSummary = table.Column<string>(type: "text", nullable: true),
                    AiSummaryAr = table.Column<string>(type: "text", nullable: true),
                    ChaptersJson = table.Column<string>(type: "text", nullable: false),
                    ActionItemsJson = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClassRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveClassRecordings_LiveClassSessions_ClassSessionId",
                        column: x => x.ClassSessionId,
                        principalTable: "LiveClassSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedPromoCodes_CartId",
                table: "AppliedPromoCodes",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrices_BillingProductId",
                table: "BillingPrices",
                column: "BillingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrices_StripePriceId",
                table: "BillingPrices",
                column: "StripePriceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingProducts_Code",
                table: "BillingProducts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_BillingPriceId",
                table: "CartItems",
                column: "BillingPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_BillingProductId",
                table: "CartItems",
                column: "BillingProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_SessionToken",
                table: "Carts",
                column: "SessionToken");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_Status",
                table: "Carts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_UserId",
                table: "Carts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_IdempotencyKey",
                table: "CheckoutSessions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_StripeSessionId",
                table: "CheckoutSessions",
                column: "StripeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_UserId",
                table: "CheckoutSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossSellRules_TriggerProductCode",
                table: "CrossSellRules",
                column: "TriggerProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSubscriptions_StripeSubscriptionId",
                table: "CustomerSubscriptions",
                column: "StripeSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSubscriptions_UserId",
                table: "CustomerSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassAttendances_ClassSessionId_UserId",
                table: "LiveClassAttendances",
                columns: new[] { "ClassSessionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassAttendances_ZoomParticipantUuid",
                table: "LiveClassAttendances",
                column: "ZoomParticipantUuid");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_ClassSessionId_UserId",
                table: "LiveClassEnrollments",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_IdempotencyKey",
                table: "LiveClassEnrollments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassEnrollments_UserId_Status",
                table: "LiveClassEnrollments",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_Slug",
                table: "LiveClasses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_Status_ProfessionTrack_Level",
                table: "LiveClasses",
                columns: new[] { "Status", "ProfessionTrack", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_TutorProfileId",
                table: "LiveClasses",
                column: "TutorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassRecordings_ClassSessionId",
                table: "LiveClassRecordings",
                column: "ClassSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassRecordings_ZoomRecordingId",
                table: "LiveClassRecordings",
                column: "ZoomRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_LiveClassId_ScheduledStartAt",
                table: "LiveClassSessions",
                columns: new[] { "LiveClassId", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_ScheduledStartAt",
                table: "LiveClassSessions",
                column: "ScheduledStartAt");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_Status_ScheduledStartAt",
                table: "LiveClassSessions",
                columns: new[] { "Status", "ScheduledStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassSessions_ZoomMeetingId",
                table: "LiveClassSessions",
                column: "ZoomMeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWaitlistEntries_ClassSessionId_Position",
                table: "LiveClassWaitlistEntries",
                columns: new[] { "ClassSessionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWaitlistEntries_ClassSessionId_UserId",
                table: "LiveClassWaitlistEntries",
                columns: new[] { "ClassSessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWebhookEvents_EventType_ReceivedAt",
                table: "LiveClassWebhookEvents",
                columns: new[] { "EventType", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LiveClassWebhookEvents_PayloadHash",
                table: "LiveClassWebhookEvents",
                column: "PayloadHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppliedPromoCodes");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "CheckoutSessions");

            migrationBuilder.DropTable(
                name: "CrossSellRules");

            migrationBuilder.DropTable(
                name: "CustomerSubscriptions");

            migrationBuilder.DropTable(
                name: "LiveClassAttendances");

            migrationBuilder.DropTable(
                name: "LiveClassEnrollments");

            migrationBuilder.DropTable(
                name: "LiveClassRecordings");

            migrationBuilder.DropTable(
                name: "LiveClassWaitlistEntries");

            migrationBuilder.DropTable(
                name: "LiveClassWebhookEvents");

            migrationBuilder.DropTable(
                name: "BillingPrices");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "LiveClassSessions");

            migrationBuilder.DropTable(
                name: "BillingProducts");

            migrationBuilder.DropTable(
                name: "LiveClasses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ListeningAttempts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ContentPapers");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "ApplicationUserAccounts");
        }
    }
}
