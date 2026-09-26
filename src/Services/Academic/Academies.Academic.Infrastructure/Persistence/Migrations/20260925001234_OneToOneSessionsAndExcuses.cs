using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneToOneSessionsAndExcuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionMinutes",
                table: "Students",
                type: "int",
                nullable: false,
                defaultValue: 30);   // existing students get the usual 30-minute session

            migrationBuilder.AddColumn<bool>(
                name: "AbsenceCounted",
                table: "Sessions",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AbsenceDecidedByUserId",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MakeupOfSessionId",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StudentUserId",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionExcuses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreferredStartsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Resolution = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MakeupSessionId = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolutionNote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionExcuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionExcuses_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_MakeupOfSessionId",
                table: "Sessions",
                column: "MakeupOfSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_StudentUserId_StartsAtUtc",
                table: "Sessions",
                columns: new[] { "StudentUserId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionExcuses_AcademyId",
                table: "SessionExcuses",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionExcuses_IsDeleted",
                table: "SessionExcuses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SessionExcuses_SessionId",
                table: "SessionExcuses",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionExcuses_Status",
                table: "SessionExcuses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SessionExcuses_StudentUserId_Status",
                table: "SessionExcuses",
                columns: new[] { "StudentUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionExcuses");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_MakeupOfSessionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_StudentUserId_StartsAtUtc",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SessionMinutes",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "AbsenceCounted",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AbsenceDecidedByUserId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "MakeupOfSessionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "StudentUserId",
                table: "Sessions");
        }
    }
}
