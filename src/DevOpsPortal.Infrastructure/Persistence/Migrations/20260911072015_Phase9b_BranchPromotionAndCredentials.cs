using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9b_BranchPromotionAndCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DatabaseName",
                table: "SecretReferences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Host",
                table: "SecretReferences",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "SecretReferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "SecretReferences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchPromotionDetail",
                table: "PromotionRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BranchPromotionSucceeded",
                table: "PromotionRequests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromBranch",
                table: "PromotionRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToBranch",
                table: "PromotionRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatabaseName",
                table: "SecretReferences");

            migrationBuilder.DropColumn(
                name: "Host",
                table: "SecretReferences");

            migrationBuilder.DropColumn(
                name: "Port",
                table: "SecretReferences");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "SecretReferences");

            migrationBuilder.DropColumn(
                name: "BranchPromotionDetail",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "BranchPromotionSucceeded",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "FromBranch",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "ToBranch",
                table: "PromotionRequests");
        }
    }
}
