using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemExecutionAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentAttemptId",
                table: "WorkItemJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkItemExecutionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkItemJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProviderSessionId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TaskSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    WorkingDirectory = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemExecutionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemExecutionAttempts_WorkItemJobs_WorkItemJobId",
                        column: x => x.WorkItemJobId,
                        principalTable: "WorkItemJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemExecutionAttempts_WorkItemJobId",
                table: "WorkItemExecutionAttempts",
                column: "WorkItemJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemExecutionAttempts");

            migrationBuilder.DropColumn(
                name: "CurrentAttemptId",
                table: "WorkItemJobs");
        }
    }
}
