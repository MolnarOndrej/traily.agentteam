using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemJobsAndAgentDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionRequestedAt",
                table: "AgentProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkItemJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExternalWorkItemId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkItemReference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ExternalAssigneeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemJobs_AgentProfiles_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentProfiles_DeletionRequestedAt",
                table: "AgentProfiles",
                column: "DeletionRequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemJobs_AgentId_Status",
                table: "WorkItemJobs",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemJobs_SourceId_ExternalWorkItemId",
                table: "WorkItemJobs",
                columns: new[] { "SourceId", "ExternalWorkItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemJobs_Status_CreatedAt",
                table: "WorkItemJobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemJobs");

            migrationBuilder.DropIndex(
                name: "IX_AgentProfiles_DeletionRequestedAt",
                table: "AgentProfiles");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                table: "AgentProfiles");
        }
    }
}
