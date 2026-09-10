using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7_SecretsManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecretReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnvironmentDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ProviderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretReferences_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecretReferences_EnvironmentDefinitions_EnvironmentDefiniti~",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecretValues",
                columns: table => new
                {
                    StoreKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    Tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretValues", x => x.StoreKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecretReferences_ApplicationId_EnvironmentDefinitionId",
                table: "SecretReferences",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretReferences_EnvironmentDefinitionId",
                table: "SecretReferences",
                column: "EnvironmentDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecretReferences");

            migrationBuilder.DropTable(
                name: "SecretValues");
        }
    }
}
