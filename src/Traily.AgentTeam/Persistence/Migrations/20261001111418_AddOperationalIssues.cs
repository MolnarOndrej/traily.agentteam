using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScopeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ScopeId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Availability = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ObservationCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalIssues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIssues_ScopeType_ScopeId_Capability",
                table: "OperationalIssues",
                columns: new[] { "ScopeType", "ScopeId", "Capability" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalIssues");
        }
    }
}
