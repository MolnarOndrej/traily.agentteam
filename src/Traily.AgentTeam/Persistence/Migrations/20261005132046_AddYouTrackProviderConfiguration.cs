using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTrackProviderConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YouTrackConnectionConfigurations",
                columns: table => new
                {
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTrackConnectionConfigurations", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_YouTrackConnectionConfigurations_WorkSourceConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "WorkSourceConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YouTrackProjectConfigurations",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DiscoveryQueryTemplate = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    WorkflowStateField = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AssigneeField = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTrackProjectConfigurations", x => x.SourceId);
                    table.ForeignKey(
                        name: "FK_YouTrackProjectConfigurations_ManagedProjects_SourceId",
                        column: x => x.SourceId,
                        principalTable: "ManagedProjects",
                        principalColumn: "SourceId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YouTrackConnectionConfigurations");

            migrationBuilder.DropTable(
                name: "YouTrackProjectConfigurations");
        }
    }
}
