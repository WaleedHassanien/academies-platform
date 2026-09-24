using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Academies.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Compensations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PayType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Compensations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "finance_InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConsumerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LockId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RowVersion = table.Column<DateTime>(type: "timestamp(6)", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceiveCount = table.Column<int>(type: "int", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Consumed = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Delivered = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_finance_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "finance_OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LockId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RowVersion = table.Column<DateTime>(type: "timestamp(6)", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Delivered = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_OutboxState", x => x.OutboxId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "finance_People",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Roles = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_People", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OnlinePayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentPaymentId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderSessionId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CheckoutUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitiatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    CompletedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlinePayments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaymentPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDay = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlans", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Salaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    PayType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionsCount = table.Column<int>(type: "int", nullable: true),
                    RatePerSession = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PaidByUserId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_Salaries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentGuardians",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    ParentUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGuardians", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "finance_OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EnqueueTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SentTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Headers = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Properties = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InboxMessageId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    InboxConsumerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OutboxId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContentType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MessageType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConversationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CorrelationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    InitiatorId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SourceAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DestinationAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FaultAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpirationTime = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_finance_OutboxMessage_finance_InboxState_InboxMessageId_Inbo~",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "finance_InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_finance_OutboxMessage_finance_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "finance_OutboxState",
                        principalColumn: "OutboxId");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StudentPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentPlanId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    MonthNumber = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastReminderOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentPayments_PaymentPlans_PaymentPlanId",
                        column: x => x.PaymentPlanId,
                        principalTable: "PaymentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SalaryLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    SalaryId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    PayType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionsCount = table.Column<int>(type: "int", nullable: true),
                    RatePerSession = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidByUserId = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryLogs_Salaries_SalaryId",
                        column: x => x.SalaryId,
                        principalTable: "Salaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaymentLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AcademyId = table.Column<long>(type: "bigint", nullable: false),
                    StudentPaymentId = table.Column<long>(type: "bigint", nullable: false),
                    StudentUserId = table.Column<long>(type: "bigint", nullable: false),
                    ParentUserId = table.Column<long>(type: "bigint", nullable: true),
                    MonthNumber = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidByRole = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_PaymentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentLogs_StudentPayments_StudentPaymentId",
                        column: x => x.StudentPaymentId,
                        principalTable: "StudentPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Compensations_AcademyId",
                table: "Compensations",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Compensations_IsDeleted",
                table: "Compensations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Compensations_UserId_EffectiveFrom",
                table: "Compensations",
                columns: new[] { "UserId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_AcademyId",
                table: "Expenses",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_IsDeleted",
                table: "Expenses",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SpentOn",
                table: "Expenses",
                column: "SpentOn");

            migrationBuilder.CreateIndex(
                name: "IX_finance_InboxState_Delivered",
                table: "finance_InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_finance_OutboxMessage_EnqueueTime",
                table: "finance_OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_finance_OutboxMessage_ExpirationTime",
                table: "finance_OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_finance_OutboxMessage_InboxMessageId_InboxConsumerId_Sequenc~",
                table: "finance_OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_OutboxMessage_OutboxId_SequenceNumber",
                table: "finance_OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_OutboxState_Created",
                table: "finance_OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_finance_People_AcademyId",
                table: "finance_People",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_finance_People_IsDeleted",
                table: "finance_People",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_finance_People_UserId",
                table: "finance_People",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_AcademyId",
                table: "OnlinePayments",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_IsDeleted",
                table: "OnlinePayments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_OnlinePayments_Reference",
                table: "OnlinePayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_AcademyId",
                table: "PaymentLogs",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_CreatedOnUtc",
                table: "PaymentLogs",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_IsDeleted",
                table: "PaymentLogs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_ParentUserId",
                table: "PaymentLogs",
                column: "ParentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_StudentPaymentId",
                table: "PaymentLogs",
                column: "StudentPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_StudentUserId_MonthNumber",
                table: "PaymentLogs",
                columns: new[] { "StudentUserId", "MonthNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_AcademyId",
                table: "PaymentPlans",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_IsDeleted",
                table: "PaymentPlans",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_StudentUserId",
                table: "PaymentPlans",
                column: "StudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Salaries_AcademyId",
                table: "Salaries",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_Salaries_IsDeleted",
                table: "Salaries",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Salaries_Year_Month_UserId",
                table: "Salaries",
                columns: new[] { "Year", "Month", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLogs_AcademyId",
                table: "SalaryLogs",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLogs_IsDeleted",
                table: "SalaryLogs",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLogs_SalaryId",
                table: "SalaryLogs",
                column: "SalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLogs_UserId_Year_Month",
                table: "SalaryLogs",
                columns: new[] { "UserId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_AcademyId",
                table: "StudentGuardians",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_IsDeleted",
                table: "StudentGuardians",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_ParentUserId",
                table: "StudentGuardians",
                column: "ParentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentGuardians_StudentUserId",
                table: "StudentGuardians",
                column: "StudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_AcademyId",
                table: "StudentPayments",
                column: "AcademyId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_IsDeleted",
                table: "StudentPayments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_PaymentPlanId",
                table: "StudentPayments",
                column: "PaymentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_Status_DueDate",
                table: "StudentPayments",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_StudentUserId_MonthNumber",
                table: "StudentPayments",
                columns: new[] { "StudentUserId", "MonthNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Compensations");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "finance_OutboxMessage");

            migrationBuilder.DropTable(
                name: "finance_People");

            migrationBuilder.DropTable(
                name: "OnlinePayments");

            migrationBuilder.DropTable(
                name: "PaymentLogs");

            migrationBuilder.DropTable(
                name: "SalaryLogs");

            migrationBuilder.DropTable(
                name: "StudentGuardians");

            migrationBuilder.DropTable(
                name: "finance_InboxState");

            migrationBuilder.DropTable(
                name: "finance_OutboxState");

            migrationBuilder.DropTable(
                name: "StudentPayments");

            migrationBuilder.DropTable(
                name: "Salaries");

            migrationBuilder.DropTable(
                name: "PaymentPlans");
        }
    }
}
