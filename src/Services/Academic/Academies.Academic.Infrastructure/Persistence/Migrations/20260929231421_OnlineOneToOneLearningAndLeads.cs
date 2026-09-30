using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlineOneToOneLearningAndLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The academy is online and one-to-one only. Group and teacher-wide sessions, and group
            // assignments, leave the model: they are soft-deleted (kept in the table, hidden).
            migrationBuilder.Sql("UPDATE `Sessions` SET `IsDeleted` = 1, `StudentUserId` = 0 WHERE `StudentUserId` IS NULL;");
            migrationBuilder.Sql("UPDATE `Assignments` SET `IsDeleted` = 1 WHERE `GroupId` IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Groups_GroupId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "GroupStudents");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_GroupId_StartsAtUtc",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Assignments");

            migrationBuilder.AddColumn<long>(
                name: "StudentUserId",
                table: "Assignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PayerUserId",
                table: "Students",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "StudentUserId",
                table: "Sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportSentOnUtc",
                table: "Sessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "SessionFeedbacks",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Accomplished",
                table: "SessionFeedbacks",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Homework",
                table: "SessionFeedbacks",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Memorization",
                table: "SessionFeedbacks",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Mistakes",
                table: "SessionFeedbacks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                table: "SessionFeedbacks",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Courses",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE `Courses` SET `Kind` = CASE " +
                "WHEN `Name` LIKE '%قرآن%' OR `Name` LIKE '%قران%' OR `Name` LIKE '%Quran%' OR `Name` LIKE '%Qur''an%' OR `Name` LIKE '%تحفيظ%' THEN 'Quran' " +
                "WHEN `Name` LIKE '%عربي%' OR `Name` LIKE '%Arabic%' THEN 'Arabic' " +
                "WHEN `Name` LIKE '%إسلام%' OR `Name` LIKE '%اسلام%' OR `Name` LIKE '%Islamic%' THEN 'IslamicStudies' " +
                "ELSE 'Other' END;");

            migrationBuilder.CreateTable(
                name: "Enrollments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Enrollments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Country = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsAdult = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    GuardianName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourseId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LostReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedToUserId = table.Column<long>(type: "bigint", nullable: true),
                    NextFollowUpOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TrialTeacherUserId = table.Column<long>(type: "bigint", nullable: true),
                    TrialStartsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TrialStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TrialNotes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConvertedStudentUserId = table.Column<long>(type: "bigint", nullable: true),
                    ConvertedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LearningPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: false),
                    Goal = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedAmount = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningPlans_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeacherCourses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LeadActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    LeadId = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadActivities_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Students_PayerUserId",
                table: "Students",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_StudentUserId",
                table: "Assignments",
                column: "StudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_AcademyId",
                table: "Enrollments",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_CourseId",
                table: "Enrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_IsDeleted",
                table: "Enrollments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_StudentUserId_CourseId",
                table: "Enrollments",
                columns: new[] { "StudentUserId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_TeacherUserId",
                table: "Enrollments",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_AcademyId",
                table: "LeadActivities",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_IsDeleted",
                table: "LeadActivities",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_LeadId",
                table: "LeadActivities",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AcademyId",
                table: "Leads",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedToUserId",
                table: "Leads",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_IsDeleted",
                table: "Leads",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Status_Id",
                table: "Leads",
                columns: new[] { "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TrialTeacherUserId_TrialStartsAtUtc",
                table: "Leads",
                columns: new[] { "TrialTeacherUserId", "TrialStartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_AcademyId",
                table: "LearningPlans",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_CourseId",
                table: "LearningPlans",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_IsDeleted",
                table: "LearningPlans",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_LearningPlans_StudentUserId_CourseId",
                table: "LearningPlans",
                columns: new[] { "StudentUserId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourses_AcademyId",
                table: "TeacherCourses",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourses_CourseId",
                table: "TeacherCourses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourses_IsDeleted",
                table: "TeacherCourses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourses_TeacherUserId_CourseId",
                table: "TeacherCourses",
                columns: new[] { "TeacherUserId", "CourseId" });

            // Every subject a student already has sessions in becomes an enrollment, with their latest teacher.
            migrationBuilder.Sql(
                "INSERT INTO `Enrollments` (`AcademyId`, `StudentUserId`, `CourseId`, `TeacherUserId`, `Status`, `StartedOn`, `CreatedOnUtc`, `IsDeleted`) " +
                "SELECT s.`AcademyId`, s.`StudentUserId`, s.`CourseId`, " +
                "(SELECT l.`TeacherUserId` FROM `Sessions` l WHERE l.`IsDeleted` = 0 AND l.`StudentUserId` = s.`StudentUserId` AND l.`CourseId` = s.`CourseId` " +
                "ORDER BY l.`StartsAtUtc` DESC LIMIT 1), " +
                "'Active', DATE(MIN(s.`StartsAtUtc`)), UTC_TIMESTAMP(6), 0 " +
                "FROM `Sessions` s WHERE s.`IsDeleted` = 0 AND s.`StudentUserId` > 0 " +
                "GROUP BY s.`AcademyId`, s.`StudentUserId`, s.`CourseId`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Enrollments");

            migrationBuilder.DropTable(
                name: "LeadActivities");

            migrationBuilder.DropTable(
                name: "LearningPlans");

            migrationBuilder.DropTable(
                name: "TeacherCourses");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Students_PayerUserId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_StudentUserId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PayerUserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ReportSentOnUtc",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Accomplished",
                table: "SessionFeedbacks");

            migrationBuilder.DropColumn(
                name: "Homework",
                table: "SessionFeedbacks");

            migrationBuilder.DropColumn(
                name: "Memorization",
                table: "SessionFeedbacks");

            migrationBuilder.DropColumn(
                name: "Mistakes",
                table: "SessionFeedbacks");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SessionFeedbacks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "StudentUserId",
                table: "Assignments");

            migrationBuilder.AddColumn<long>(
                name: "GroupId",
                table: "Assignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "StudentUserId",
                table: "Sessions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "GroupId",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Sessions",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Sessions",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Online")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "SessionFeedbacks",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupStudents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupStudents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupStudents_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_GroupId_StartsAtUtc",
                table: "Sessions",
                columns: new[] { "GroupId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_AcademyId",
                table: "Groups",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_IsDeleted",
                table: "Groups",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TeacherUserId",
                table: "Groups",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupStudents_AcademyId",
                table: "GroupStudents",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupStudents_GroupId_StudentUserId",
                table: "GroupStudents",
                columns: new[] { "GroupId", "StudentUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupStudents_IsDeleted",
                table: "GroupStudents",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_GroupStudents_StudentUserId",
                table: "GroupStudents",
                column: "StudentUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Groups_GroupId",
                table: "Sessions",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
