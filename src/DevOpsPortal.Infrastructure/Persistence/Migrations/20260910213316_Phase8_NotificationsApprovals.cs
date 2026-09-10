using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8_NotificationsApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EmailSentAt",
                table: "ProductionApprovals",
                newName: "NotifiedAt");

            migrationBuilder.RenameColumn(
                name: "EmailRecipients",
                table: "ProductionApprovals",
                newName: "NotificationRecipients");

            migrationBuilder.RenameColumn(
                name: "ApprovalToken",
                table: "ProductionApprovals",
                newName: "ApprovalTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_ProductionApprovals_ApprovalToken",
                table: "ProductionApprovals",
                newName: "IX_ProductionApprovals_ApprovalTokenHash");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalTokenExpiresAt",
                table: "PromotionRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ApprovalTokenHash",
                table: "PromotionRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NotificationRecipients",
                table: "PromotionRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotifiedAt",
                table: "PromotionRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ProductionApprovals",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_ApprovalTokenHash",
                table: "PromotionRequests",
                column: "ApprovalTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PromotionRequests_ApprovalTokenHash",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "ApprovalTokenExpiresAt",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "ApprovalTokenHash",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "NotificationRecipients",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ProductionApprovals");

            migrationBuilder.RenameColumn(
                name: "NotifiedAt",
                table: "ProductionApprovals",
                newName: "EmailSentAt");

            migrationBuilder.RenameColumn(
                name: "NotificationRecipients",
                table: "ProductionApprovals",
                newName: "EmailRecipients");

            migrationBuilder.RenameColumn(
                name: "ApprovalTokenHash",
                table: "ProductionApprovals",
                newName: "ApprovalToken");

            migrationBuilder.RenameIndex(
                name: "IX_ProductionApprovals_ApprovalTokenHash",
                table: "ProductionApprovals",
                newName: "IX_ProductionApprovals_ApprovalToken");
        }
    }
}
