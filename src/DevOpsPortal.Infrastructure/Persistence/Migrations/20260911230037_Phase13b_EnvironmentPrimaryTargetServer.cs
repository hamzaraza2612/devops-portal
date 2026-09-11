using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13b_EnvironmentPrimaryTargetServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryTargetServerId",
                table: "EnvironmentDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentDefinitions_PrimaryTargetServerId",
                table: "EnvironmentDefinitions",
                column: "PrimaryTargetServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_EnvironmentDefinitions_TargetServers_PrimaryTargetServerId",
                table: "EnvironmentDefinitions",
                column: "PrimaryTargetServerId",
                principalTable: "TargetServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EnvironmentDefinitions_TargetServers_PrimaryTargetServerId",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_EnvironmentDefinitions_PrimaryTargetServerId",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropColumn(
                name: "PrimaryTargetServerId",
                table: "EnvironmentDefinitions");
        }
    }
}
