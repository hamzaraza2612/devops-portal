using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_BuildPipelineJenkins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "Deployments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuildServerId",
                table: "BuildConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobName",
                table: "BuildConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishArguments",
                table: "BuildConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SdkVersion",
                table: "BuildConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildServers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProviderType = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    ApiTokenEnvVarName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "text", nullable: true),
                    CommitSha = table.Column<string>(type: "text", nullable: true),
                    RequestedSemVer = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderQueueItemId = table.Column<string>(type: "text", nullable: true),
                    BuildNumber = table.Column<int>(type: "integer", nullable: true),
                    BuildUrl = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildRequests_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildRequests_BuildServers_BuildServerId",
                        column: x => x.BuildServerId,
                        principalTable: "BuildServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Branch = table.Column<string>(type: "text", nullable: true),
                    BuildNumber = table.Column<int>(type: "integer", nullable: false),
                    ImageReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    BuildStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Releases_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Releases_BuildRequests_BuildRequestId",
                        column: x => x.BuildRequestId,
                        principalTable: "BuildRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ReleaseId",
                table: "Deployments",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildConfigurations_BuildServerId",
                table: "BuildConfigurations",
                column: "BuildServerId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildRequests_ApplicationId_RequestedAt",
                table: "BuildRequests",
                columns: new[] { "ApplicationId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildRequests_BuildServerId",
                table: "BuildRequests",
                column: "BuildServerId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildServers_Name",
                table: "BuildServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ApplicationId",
                table: "Releases",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_BuildRequestId",
                table: "Releases",
                column: "BuildRequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildConfigurations_BuildServers_BuildServerId",
                table: "BuildConfigurations",
                column: "BuildServerId",
                principalTable: "BuildServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_Releases_ReleaseId",
                table: "Deployments",
                column: "ReleaseId",
                principalTable: "Releases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BuildConfigurations_BuildServers_BuildServerId",
                table: "BuildConfigurations");

            migrationBuilder.DropForeignKey(
                name: "FK_Deployments_Releases_ReleaseId",
                table: "Deployments");

            migrationBuilder.DropTable(
                name: "Releases");

            migrationBuilder.DropTable(
                name: "BuildRequests");

            migrationBuilder.DropTable(
                name: "BuildServers");

            migrationBuilder.DropIndex(
                name: "IX_Deployments_ReleaseId",
                table: "Deployments");

            migrationBuilder.DropIndex(
                name: "IX_BuildConfigurations_BuildServerId",
                table: "BuildConfigurations");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "BuildServerId",
                table: "BuildConfigurations");

            migrationBuilder.DropColumn(
                name: "JobName",
                table: "BuildConfigurations");

            migrationBuilder.DropColumn(
                name: "PublishArguments",
                table: "BuildConfigurations");

            migrationBuilder.DropColumn(
                name: "SdkVersion",
                table: "BuildConfigurations");
        }
    }
}
