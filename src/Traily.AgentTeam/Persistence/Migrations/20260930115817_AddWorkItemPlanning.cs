using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "WorkItemExecutionAttempts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanningCompletedAt",
                table: "WorkItemExecutionAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanningInputJson",
                table: "WorkItemExecutionAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanningResultJson",
                table: "WorkItemExecutionAttempts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phase",
                table: "WorkItemExecutionAttempts");

            migrationBuilder.DropColumn(
                name: "PlanningCompletedAt",
                table: "WorkItemExecutionAttempts");

            migrationBuilder.DropColumn(
                name: "PlanningInputJson",
                table: "WorkItemExecutionAttempts");

            migrationBuilder.DropColumn(
                name: "PlanningResultJson",
                table: "WorkItemExecutionAttempts");
        }
    }
}
