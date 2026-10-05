using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkSourceConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing projects need an owner-verified data backfill in this migration.
            // Stop before schema changes rather than inventing management-tool mappings.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__TrailyProjectMappingGuard" (
                    "ExistingProjectCount" INTEGER NOT NULL,
                    CONSTRAINT "ProvideExplicitProjectBackfillBeforeApplyingWorkSourceMigration"
                        CHECK ("ExistingProjectCount" = 0)
                );
                INSERT INTO "__TrailyProjectMappingGuard"
                SELECT COUNT(*) FROM "ManagedProjects";
                DROP TABLE "__TrailyProjectMappingGuard";
                """);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProjectId",
                table: "ManagedProjects",
                type: "TEXT",
                maxLength: 200,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "WorkSourceConnectionId",
                table: "ManagedProjects",
                type: "TEXT",
                maxLength: 100,
                nullable: false);

            migrationBuilder.CreateTable(
                name: "WorkSourceConnections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkSourceConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedProjects_WorkSourceConnectionId",
                table: "ManagedProjects",
                column: "WorkSourceConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ManagedProjects_WorkSourceConnections_WorkSourceConnectionId",
                table: "ManagedProjects",
                column: "WorkSourceConnectionId",
                principalTable: "WorkSourceConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManagedProjects_ExternalProjectId",
                table: "ManagedProjects",
                sql: "length(trim(\"ExternalProjectId\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManagedProjects_WorkSourceConnectionId",
                table: "ManagedProjects",
                sql: "length(trim(\"WorkSourceConnectionId\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ManagedProjects_ExternalProjectId",
                table: "ManagedProjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ManagedProjects_WorkSourceConnectionId",
                table: "ManagedProjects");

            migrationBuilder.DropForeignKey(
                name: "FK_ManagedProjects_WorkSourceConnections_WorkSourceConnectionId",
                table: "ManagedProjects");

            migrationBuilder.DropTable(
                name: "WorkSourceConnections");

            migrationBuilder.DropIndex(
                name: "IX_ManagedProjects_WorkSourceConnectionId",
                table: "ManagedProjects");

            migrationBuilder.DropColumn(
                name: "ExternalProjectId",
                table: "ManagedProjects");

            migrationBuilder.DropColumn(
                name: "WorkSourceConnectionId",
                table: "ManagedProjects");
        }
    }
}
