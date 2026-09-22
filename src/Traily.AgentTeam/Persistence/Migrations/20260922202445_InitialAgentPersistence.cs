using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Traily.AgentTeam.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAgentPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxConcurrentJobs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentProfiles", x => x.Id);
                    table.CheckConstraint("CK_AgentProfiles_MaxConcurrentJobs", "\"MaxConcurrentJobs\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "AgentExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExternalUserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentExternalIdentities_AgentProfiles_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSkills",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SkillId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSkills_AgentProfiles_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExternalIdentities_AgentId",
                table: "AgentExternalIdentities",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExternalIdentities_SourceId_ExternalUserId",
                table: "AgentExternalIdentities",
                columns: new[] { "SourceId", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentExternalIdentities_SourceId_Login",
                table: "AgentExternalIdentities",
                columns: new[] { "SourceId", "Login" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSkills_AgentId_SkillId",
                table: "AgentSkills",
                columns: new[] { "AgentId", "SkillId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentExternalIdentities");

            migrationBuilder.DropTable(
                name: "AgentSkills");

            migrationBuilder.DropTable(
                name: "AgentProfiles");
        }
    }
}
