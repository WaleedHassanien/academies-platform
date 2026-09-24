using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayPalAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "StudentPayments",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EGP")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentPlans",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EGP")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentLogs",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EGP")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ChargedAmount",
                table: "OnlinePayments",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ChargedCurrency",
                table: "OnlinePayments",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD")
                .Annotation("MySql:CharSet", "utf8mb4");
            // Rows created before currencies existed used the old placeholder (SAR).
            migrationBuilder.Sql("UPDATE `OnlinePayments` SET `Currency` = 'EGP', `ChargedAmount` = `Amount`, `ChargedCurrency` = 'EGP' WHERE `Currency` = 'SAR';");


            migrationBuilder.CreateTable(
                name: "FinanceSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_ProviderSessionId",
                table: "OnlinePayments",
                column: "ProviderSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceSettings_AcademyId",
                table: "FinanceSettings",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceSettings_IsDeleted",
                table: "FinanceSettings",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceSettings");

            migrationBuilder.DropIndex(
                name: "IX_OnlinePayments_ProviderSessionId",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "StudentPayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PaymentLogs");

            migrationBuilder.DropColumn(
                name: "ChargedAmount",
                table: "OnlinePayments");

            migrationBuilder.DropColumn(
                name: "ChargedCurrency",
                table: "OnlinePayments");
        }
    }
}
