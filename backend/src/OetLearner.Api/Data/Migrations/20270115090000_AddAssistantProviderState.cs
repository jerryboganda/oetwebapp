using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20270115090000_AddAssistantProviderState")]
public sealed class AddAssistantProviderState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "EncryptedProviderState", table: "AiAssistantMessages", type: "text", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "EncryptedProviderState", table: "AiAssistantMessages");
}
