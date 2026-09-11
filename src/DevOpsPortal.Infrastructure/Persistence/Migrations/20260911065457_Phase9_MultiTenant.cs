using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_MultiTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TargetServers_Name",
                table: "TargetServers");

            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_Name",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_EnvironmentDefinitions_Name",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_BuildServers_Name",
                table: "BuildServers");

            migrationBuilder.DropIndex(
                name: "IX_Applications_Slug",
                table: "Applications");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TargetServers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SecretReferences",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Repositories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Releases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PromotionRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProductionApprovals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EnvironmentDefinitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Deployments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DeploymentLogEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BuildServers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BuildRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BuildConfigurations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Applications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ApplicationEnvironments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AllowedDeploymentRoots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetServers_TenantId_Name",
                table: "TargetServers",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretReferences_TenantId",
                table: "SecretReferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_TenantId_Name",
                table: "Repositories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_TenantId",
                table: "Releases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRequests_TenantId",
                table: "PromotionRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionApprovals_TenantId",
                table: "ProductionApprovals",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentDefinitions_TenantId_Name",
                table: "EnvironmentDefinitions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_TenantId",
                table: "Deployments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentLogEntries_TenantId",
                table: "DeploymentLogEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildServers_TenantId_Name",
                table: "BuildServers",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildRequests_TenantId",
                table: "BuildRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildConfigurations_TenantId",
                table: "BuildConfigurations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TenantId_Slug",
                table: "Applications",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEnvironments_TenantId",
                table: "ApplicationEnvironments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AllowedDeploymentRoots_TenantId",
                table: "AllowedDeploymentRoots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AllowedDeploymentRoots_Tenants_TenantId",
                table: "AllowedDeploymentRoots",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationEnvironments_Tenants_TenantId",
                table: "ApplicationEnvironments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Tenants_TenantId",
                table: "Applications",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildConfigurations_Tenants_TenantId",
                table: "BuildConfigurations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildRequests_Tenants_TenantId",
                table: "BuildRequests",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BuildServers_Tenants_TenantId",
                table: "BuildServers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentLogEntries_Tenants_TenantId",
                table: "DeploymentLogEntries",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_Tenants_TenantId",
                table: "Deployments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EnvironmentDefinitions_Tenants_TenantId",
                table: "EnvironmentDefinitions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionApprovals_Tenants_TenantId",
                table: "ProductionApprovals",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PromotionRequests_Tenants_TenantId",
                table: "PromotionRequests",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Releases_Tenants_TenantId",
                table: "Releases",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Tenants_TenantId",
                table: "Repositories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Tenants_TenantId",
                table: "Roles",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SecretReferences_Tenants_TenantId",
                table: "SecretReferences",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TargetServers_Tenants_TenantId",
                table: "TargetServers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllowedDeploymentRoots_Tenants_TenantId",
                table: "AllowedDeploymentRoots");

            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationEnvironments_Tenants_TenantId",
                table: "ApplicationEnvironments");

            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Tenants_TenantId",
                table: "Applications");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_BuildConfigurations_Tenants_TenantId",
                table: "BuildConfigurations");

            migrationBuilder.DropForeignKey(
                name: "FK_BuildRequests_Tenants_TenantId",
                table: "BuildRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_BuildServers_Tenants_TenantId",
                table: "BuildServers");

            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentLogEntries_Tenants_TenantId",
                table: "DeploymentLogEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Deployments_Tenants_TenantId",
                table: "Deployments");

            migrationBuilder.DropForeignKey(
                name: "FK_EnvironmentDefinitions_Tenants_TenantId",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionApprovals_Tenants_TenantId",
                table: "ProductionApprovals");

            migrationBuilder.DropForeignKey(
                name: "FK_PromotionRequests_Tenants_TenantId",
                table: "PromotionRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Releases_Tenants_TenantId",
                table: "Releases");

            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Tenants_TenantId",
                table: "Repositories");

            migrationBuilder.DropForeignKey(
                name: "FK_Roles_Tenants_TenantId",
                table: "Roles");

            migrationBuilder.DropForeignKey(
                name: "FK_SecretReferences_Tenants_TenantId",
                table: "SecretReferences");

            migrationBuilder.DropForeignKey(
                name: "FK_TargetServers_Tenants_TenantId",
                table: "TargetServers");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TargetServers_TenantId_Name",
                table: "TargetServers");

            migrationBuilder.DropIndex(
                name: "IX_SecretReferences_TenantId",
                table: "SecretReferences");

            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_TenantId_Name",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Releases_TenantId",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_PromotionRequests_TenantId",
                table: "PromotionRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProductionApprovals_TenantId",
                table: "ProductionApprovals");

            migrationBuilder.DropIndex(
                name: "IX_EnvironmentDefinitions_TenantId_Name",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Deployments_TenantId",
                table: "Deployments");

            migrationBuilder.DropIndex(
                name: "IX_DeploymentLogEntries_TenantId",
                table: "DeploymentLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_BuildServers_TenantId_Name",
                table: "BuildServers");

            migrationBuilder.DropIndex(
                name: "IX_BuildRequests_TenantId",
                table: "BuildRequests");

            migrationBuilder.DropIndex(
                name: "IX_BuildConfigurations_TenantId",
                table: "BuildConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Applications_TenantId_Slug",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationEnvironments_TenantId",
                table: "ApplicationEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_AllowedDeploymentRoots_TenantId",
                table: "AllowedDeploymentRoots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TargetServers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SecretReferences");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PromotionRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductionApprovals");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EnvironmentDefinitions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Deployments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DeploymentLogEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BuildServers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BuildRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BuildConfigurations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ApplicationEnvironments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AllowedDeploymentRoots");

            migrationBuilder.CreateIndex(
                name: "IX_TargetServers_Name",
                table: "TargetServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Name",
                table: "Repositories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentDefinitions_Name",
                table: "EnvironmentDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildServers_Name",
                table: "BuildServers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Slug",
                table: "Applications",
                column: "Slug",
                unique: true);
        }
    }
}
