using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260701090000_AddSubscriptionTimerFreezingAndManualPaymentFields")]
    public partial class AddSubscriptionTimerFreezingAndManualPaymentFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""AccessDurationDays"" integer NOT NULL DEFAULT 180;");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""TotalFreezeDaysUsed"" integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""MaxFreezeDaysAllowed"" integer NOT NULL DEFAULT 365;");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""PreservedRemainingDays"" integer NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""PendingFreezeRequestDate"" timestamp with time zone NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" ADD COLUMN IF NOT EXISTS ""FrozenSince"" timestamp with time zone NULL;");

            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""CandidateFullName"" character varying(128) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""CandidateEmail"" character varying(256) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""CandidateWhatsApp"" character varying(64) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""CourseName"" character varying(128) NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""CourseId"" character varying(64) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" ADD COLUMN IF NOT EXISTS ""PaymentCategory"" character varying(32) NOT NULL DEFAULT 'international';");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SubscriptionFreezes"" (
    ""Id"" character varying(64) NOT NULL,
    ""SubscriptionId"" character varying(64) NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""RequestedBy"" character varying(32) NOT NULL,
    ""RequestStatus"" character varying(32) NOT NULL,
    ""FreezeRequestDate"" timestamp with time zone NOT NULL,
    ""FreezeStartDate"" timestamp with time zone NULL,
    ""FreezeEndDate"" timestamp with time zone NULL,
    ""PreservedRemainingDaysAtFreeze"" integer NULL,
    ""FreezeDaysUsed"" integer NOT NULL DEFAULT 0,
    ""FrozenBy"" character varying(64) NULL,
    ""FreezeReason"" character varying(512) NULL,
    ""AdminNotes"" character varying(1024) NULL,
    ""RejectionReason"" character varying(512) NULL,
    ""AdminDecisionById"" character varying(64) NULL,
    ""AdminDecisionDate"" timestamp with time zone NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_SubscriptionFreezes"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_SubscriptionFreezes_SubscriptionId"" ON ""SubscriptionFreezes"" (""SubscriptionId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_SubscriptionFreezes_UserId"" ON ""SubscriptionFreezes"" (""UserId"");");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SubscriptionFreezes_SubscriptionId_Pending"" ON ""SubscriptionFreezes"" (""SubscriptionId"") WHERE ""RequestStatus"" = 'pending';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_SubscriptionFreezes_SubscriptionId_Pending"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_SubscriptionFreezes_UserId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_SubscriptionFreezes_SubscriptionId"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SubscriptionFreezes"";");

            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""PaymentCategory"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""CourseId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""CourseName"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""CandidateWhatsApp"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""CandidateEmail"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ManualPaymentRequests"" DROP COLUMN IF EXISTS ""CandidateFullName"";");

            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""FrozenSince"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""PendingFreezeRequestDate"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""PreservedRemainingDays"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""MaxFreezeDaysAllowed"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""TotalFreezeDaysUsed"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Subscriptions"" DROP COLUMN IF EXISTS ""AccessDurationDays"";");
        }
    }
}
