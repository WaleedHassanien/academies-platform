using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerSessionBillingAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthCloses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    ClosedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthCloses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentBillings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    Mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PricePerSession = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SessionsPerMonth = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DueDay = table.Column<int>(type: "int", nullable: false),
                    PaymentPlanId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBillings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBillings_PaymentPlans_PaymentPlanId",
                        column: x => x.PaymentPlanId,
                        principalTable: "PaymentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentPaymentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionId = table.Column<long>(type: "bigint", nullable: true),
                    SessionStartsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentInvoiceLines_StudentPayments_StudentPaymentId",
                        column: x => x.StudentPaymentId,
                        principalTable: "StudentPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeacherPayouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    SessionsCount = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PaidByUserId = table.Column<long>(type: "bigint", nullable: true),
                    Reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherPayouts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeacherStudentRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherUserId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    RatePerSession = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherStudentRates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeacherPayoutLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherPayoutId = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    SessionStartsAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherPayoutLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherPayoutLines_TeacherPayouts_TeacherPayoutId",
                        column: x => x.TeacherPayoutId,
                        principalTable: "TeacherPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MonthCloses_AcademyId",
                table: "MonthCloses",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthCloses_AcademyId_Year_Month",
                table: "MonthCloses",
                columns: new[] { "AcademyId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthCloses_IsDeleted",
                table: "MonthCloses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_AcademyId",
                table: "StudentBillings",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_IsDeleted",
                table: "StudentBillings",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_PaymentPlanId",
                table: "StudentBillings",
                column: "PaymentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_StudentUserId",
                table: "StudentBillings",
                column: "StudentUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvoiceLines_AcademyId",
                table: "StudentInvoiceLines",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvoiceLines_IsDeleted",
                table: "StudentInvoiceLines",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvoiceLines_SessionId_Kind",
                table: "StudentInvoiceLines",
                columns: new[] { "SessionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvoiceLines_StudentPaymentId",
                table: "StudentInvoiceLines",
                column: "StudentPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayoutLines_AcademyId",
                table: "TeacherPayoutLines",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayoutLines_IsDeleted",
                table: "TeacherPayoutLines",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayoutLines_SessionId",
                table: "TeacherPayoutLines",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayoutLines_TeacherPayoutId",
                table: "TeacherPayoutLines",
                column: "TeacherPayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayouts_AcademyId",
                table: "TeacherPayouts",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayouts_IsDeleted",
                table: "TeacherPayouts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayouts_TeacherUserId_Status",
                table: "TeacherPayouts",
                columns: new[] { "TeacherUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherPayouts_Year_Month_TeacherUserId",
                table: "TeacherPayouts",
                columns: new[] { "Year", "Month", "TeacherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherStudentRates_AcademyId",
                table: "TeacherStudentRates",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherStudentRates_IsDeleted",
                table: "TeacherStudentRates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherStudentRates_TeacherUserId_StudentUserId",
                table: "TeacherStudentRates",
                columns: new[] { "TeacherUserId", "StudentUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthCloses");

            migrationBuilder.DropTable(
                name: "StudentBillings");

            migrationBuilder.DropTable(
                name: "StudentInvoiceLines");

            migrationBuilder.DropTable(
                name: "TeacherPayoutLines");

            migrationBuilder.DropTable(
                name: "TeacherStudentRates");

            migrationBuilder.DropTable(
                name: "TeacherPayouts");
        }
    }
}
