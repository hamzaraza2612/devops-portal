using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevOpsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14_SourceSyncFromRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SyncSourceFromRepository",
                table: "ApplicationEnvironments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SyncSourceFromRepository",
                table: "ApplicationEnvironments");
        }
    }
}
