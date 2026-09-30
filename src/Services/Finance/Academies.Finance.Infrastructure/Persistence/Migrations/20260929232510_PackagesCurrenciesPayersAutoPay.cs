using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackagesCurrenciesPayersAutoPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentBillings_StudentUserId",
                table: "StudentBillings");

            migrationBuilder.AddColumn<int>(
                name: "AutoChargeAttempts",
                table: "StudentPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CourseId",
                table: "StudentPayments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoChargeOnUtc",
                table: "StudentPayments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CourseId",
                table: "StudentBillings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPrice",
                table: "StudentBillings",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "PackageId",
                table: "StudentBillings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionMinutes",
                table: "StudentBillings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "OnlinePayments",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "MandateId",
                table: "OnlinePayments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRates",
                table: "FinanceSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Existing prepaid billings cost sessions × price a month.
            migrationBuilder.Sql("UPDATE `StudentBillings` SET `MonthlyPrice` = `PricePerSession` * `SessionsPerMonth` WHERE `Mode` = 'Prepaid';");

            migrationBuilder.CreateTable(
                name: "AutoPayMandates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    PayerUserId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderSetupId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CustomerId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentMethodId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CardBrand = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CardLast4 = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoPayMandates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionsPerMonth = table.Column<int>(type: "int", nullable: false),
                    SessionMinutes = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentPayers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    PayerUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPayers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_StudentUserId_CourseId",
                table: "StudentBillings",
                columns: new[] { "StudentUserId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_AcademyId",
                table: "AutoPayMandates",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_IsDeleted",
                table: "AutoPayMandates",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_PayerUserId",
                table: "AutoPayMandates",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_ProviderSetupId",
                table: "AutoPayMandates",
                column: "ProviderSetupId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_Reference",
                table: "AutoPayMandates",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutoPayMandates_StudentUserId_Status",
                table: "AutoPayMandates",
                columns: new[] { "StudentUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Packages_AcademyId",
                table: "Packages",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_IsActive_Currency",
                table: "Packages",
                columns: new[] { "IsActive", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_Packages_IsDeleted",
                table: "Packages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayers_AcademyId",
                table: "StudentPayers",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayers_IsDeleted",
                table: "StudentPayers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayers_PayerUserId",
                table: "StudentPayers",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayers_StudentUserId",
                table: "StudentPayers",
                column: "StudentUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoPayMandates");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropTable(
                name: "StudentPayers");

            migrationBuilder.DropIndex(
                name: "IX_StudentBillings_StudentUserId_CourseId",
                table: "StudentBillings");

            migrationBuilder.DropColumn(
                name: "AutoChargeAttempts",
                table: "StudentPayments");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "StudentPayments");

            migrationBuilder.DropColumn(
                name: "LastAutoChargeOnUtc",
                table: "StudentPayments");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "StudentBillings");

            migrationBuilder.DropColumn(
                name: "MonthlyPrice",
                table: "StudentBillings");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "StudentBillings");

            migrationBuilder.DropColumn(
                name: "SessionMinutes",
                table: "StudentBillings");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "MandateId",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRates",
                table: "FinanceSettings");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBillings_StudentUserId",
                table: "StudentBillings",
                column: "StudentUserId",
                unique: true);
        }
    }
}
