using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessTokenEnvVarName",
                table: "Repositories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDownWithVolumesOnDeploy",
                table: "ApplicationEnvironments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BuildConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectOrSolutionPath = table.Column<string>(type: "text", nullable: true),
                    PublishConfiguration = table.Column<string>(type: "text", nullable: true),
                    DockerfilePath = table.Column<string>(type: "text", nullable: true),
                    ImageRegistry = table.Column<string>(type: "text", nullable: true),
                    ImageRepository = table.Column<string>(type: "text", nullable: true),
                    ImageTagStrategy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildConfigurations_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationEnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CommitMessage = table.Column<string>(type: "text", nullable: true),
                    CommitAuthor = table.Column<string>(type: "text", nullable: true),
                    Branch = table.Column<string>(type: "text", nullable: true),
                    ImageReference = table.Column<string>(type: "text", nullable: true),
                    VersionLabel = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsRollback = table.Column<bool>(type: "boolean", nullable: false),
                    RollbackOfDeploymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromotionRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    HealthCheckPassed = table.Column<bool>(type: "boolean", nullable: true),
                    HealthCheckDetail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_ApplicationEnvironments_ApplicationEnvironmentId",
                        column: x => x.ApplicationEnvironmentId,
                        principalTable: "ApplicationEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deployments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deployments_Deployments_RollbackOfDeploymentId",
                        column: x => x.RollbackOfDeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deployments_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromEnvironmentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToEnvironmentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionRequests_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionRequests_Deployments_SourceDeploymentId",
                        column: x => x.SourceDeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionRequests_EnvironmentDefinitions_FromEnvironmentDef~",
                        column: x => x.FromEnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionRequests_EnvironmentDefinitions_ToEnvironmentDefin~",
                        column: x => x.ToEnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "text", nullable: true),
                    EmailSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailRecipients = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionApprovals_PromotionRequests_PromotionRequestId",
                        column: x => x.PromotionRequestId,
                        principalTable: "PromotionRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildConfigurations_ApplicationId",
                table: "BuildConfigurations",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentLogEntries_DeploymentId_Sequence",
                table: "DeploymentLogEntries",
                columns: new[] { "DeploymentId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationEnvironmentId",
                table: "Deployments",
                column: "ApplicationEnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationId_EnvironmentDefinitionId",
                table: "Deployments",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId" },
                unique: true,
                filter: "\"Status\" IN (0,1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationId_EnvironmentDefinitionId_CommitSha",
                table: "Deployments",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId", "CommitSha" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_ApplicationId_EnvironmentDefinitionId_Status",
                table: "Deployments",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_EnvironmentDefinitionId",
                table: "Deployments",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_PromotionRequestId",
                table: "Deployments",
                column: "PromotionRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_RollbackOfDeploymentId",
                table: "Deployments",
                column: "RollbackOfDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_ApprovalToken",
                table: "ProductionApprovals",
                column: "ApprovalToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_PromotionRequestId",
                table: "ProductionApprovals",
                column: "PromotionRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_ApplicationId_ToEnvironmentDefinitionId_S~",
                table: "PromotionRequests",
                columns: new[] { "ApplicationId", "ToEnvironmentDefinitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_FromEnvironmentDefinitionId",
                table: "PromotionRequests",
                column: "FromEnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_SourceDeploymentId",
                table: "PromotionRequests",
                column: "SourceDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_ToEnvironmentDefinitionId",
                table: "PromotionRequests",
                column: "ToEnvironmentDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentLogEntries_Deployments_DeploymentId",
                table: "DeploymentLogEntries",
                column: "DeploymentId",
                principalTable: "Deployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_PromotionRequests_PromotionRequestId",
                table: "Deployments",
                column: "PromotionRequestId",
                principalTable: "PromotionRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PromotionRequests_Deployments_SourceDeploymentId",
                table: "PromotionRequests");

            migrationBuilder.DropTable(
                name: "BuildConfigurations");

            migrationBuilder.DropTable(
                name: "DeploymentLogEntries");

            migrationBuilder.DropTable(
                name: "ProductionApprovals");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "AccessTokenEnvVarName",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "UseDownWithVolumesOnDeploy",
                table: "ApplicationEnvironments");
        }
    }
}
