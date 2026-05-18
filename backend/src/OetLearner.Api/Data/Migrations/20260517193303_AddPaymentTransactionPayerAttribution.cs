using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260517193303_AddPaymentTransactionPayerAttribution")]
    public partial class AddPaymentTransactionPayerAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayerType",
                table: "PaymentTransactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SponsorshipId",
                table: "PaymentTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_SponsorshipId",
                table: "PaymentTransactions",
                column: "SponsorshipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_SponsorshipId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PayerType",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "SponsorshipId",
                table: "PaymentTransactions");
        }
    }
}
