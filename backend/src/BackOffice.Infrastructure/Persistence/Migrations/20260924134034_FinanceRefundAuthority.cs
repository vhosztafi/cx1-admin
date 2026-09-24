using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceRefundAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefundApprovalRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SecondApprovalThreshold = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    SmallApprovalCount = table.Column<int>(type: "int", nullable: false),
                    LargeApprovalCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundApprovalRule", x => x.Id);
                    table.CheckConstraint("CK_RefundApprovalRule_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RefundApprovalRule_Facts", "[SecondApprovalThreshold]>0 AND [SmallApprovalCount]=1 AND [LargeApprovalCount]=2");
                    table.ForeignKey(
                        name: "FK_RefundApprovalRule_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefundRoleAuthority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Limit = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundRoleAuthority", x => x.Id);
                    table.CheckConstraint("CK_RefundRoleAuthority_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RefundRoleAuthority_Limit", "[Limit]>0");
                    table.CheckConstraint("CK_RefundRoleAuthority_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_RefundRoleAuthority_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefundRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DebtorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DebtorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundRequest", x => x.Id);
                    table.CheckConstraint("CK_RefundRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RefundRequest_Facts", "[Amount]>0 AND [Currency]='GBP' AND [DebtorKind] IN ('agency','relationship') AND [State] IN ('building','pending','approved','rejected') AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.CheckConstraint("CK_RefundRequest_RequestedAt_Utc", "DATEPART(TZOFFSET,[RequestedAt]) = 0");
                    table.CheckConstraint("CK_RefundRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_RefundRequest_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundRequest_IssueFinancialObligation_CreditObligationId",
                        column: x => x.CreditObligationId,
                        principalTable: "IssueFinancialObligation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundRequest_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundRequest_RefundApprovalRule_RuleId",
                        column: x => x.RuleId,
                        principalTable: "RefundApprovalRule",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundRequest_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefundCashReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundCashReservation", x => x.Id);
                    table.CheckConstraint("CK_RefundCashReservation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RefundCashReservation_Positive", "[Amount]>0");
                    table.ForeignKey(
                        name: "FK_RefundCashReservation_Allocation_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "Allocation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundCashReservation_RefundRequest_RefundRequestId",
                        column: x => x.RefundRequestId,
                        principalTable: "RefundRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundCashReservation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefundDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityLimitSnapshot = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundDecision", x => x.Id);
                    table.CheckConstraint("CK_RefundDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RefundDecision_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_RefundDecision_Facts", "[Kind] IN ('approve','reject') AND [AuthorityLimitSnapshot]>0 AND LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
                    table.ForeignKey(
                        name: "FK_RefundDecision_RefundApprovalRule_RuleId",
                        column: x => x.RuleId,
                        principalTable: "RefundApprovalRule",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundDecision_RefundRequest_RefundRequestId",
                        column: x => x.RefundRequestId,
                        principalTable: "RefundRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundDecision_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefundDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefundApprovalRule_Code_Version",
                table: "RefundApprovalRule",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundApprovalRule_CreatedBy",
                table: "RefundApprovalRule",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundCashReservation_AllocationId",
                table: "RefundCashReservation",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundCashReservation_CreatedBy",
                table: "RefundCashReservation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundCashReservation_RefundRequestId_AllocationId",
                table: "RefundCashReservation",
                columns: new[] { "RefundRequestId", "AllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundDecision_ActorId",
                table: "RefundDecision",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundDecision_CreatedBy",
                table: "RefundDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundDecision_RefundRequestId_ActorId",
                table: "RefundDecision",
                columns: new[] { "RefundRequestId", "ActorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundDecision_RuleId",
                table: "RefundDecision",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_AgencyId",
                table: "RefundRequest",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_CreatedBy",
                table: "RefundRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_CreditObligationId_State",
                table: "RefundRequest",
                columns: new[] { "CreditObligationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_PolicyId",
                table: "RefundRequest",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_RequestedBy",
                table: "RefundRequest",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRequest_RuleId",
                table: "RefundRequest",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRoleAuthority_CreatedBy",
                table: "RefundRoleAuthority",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRoleAuthority_RoleCode",
                table: "RefundRoleAuthority",
                column: "RoleCode",
                unique: true);

            migrationBuilder.Sql(FinanceRefundMigrationSql.Seed);
            migrationBuilder.Sql(FinanceRefundMigrationSql.RuleGuard);
            migrationBuilder.Sql(FinanceRefundMigrationSql.ReservationGuard);
            migrationBuilder.Sql(FinanceRefundMigrationSql.RequestGuard);
            migrationBuilder.Sql(FinanceRefundMigrationSql.DecisionGuard);
            migrationBuilder.Sql(FinanceRefundMigrationSql.AllocationGuard);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Allocation_RefundReservationGuard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_RefundDecision_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_RefundRequest_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_RefundCashReservation_Guard");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_RefundApprovalRule_Immutable");
            migrationBuilder.DropTable(
                name: "RefundCashReservation");

            migrationBuilder.DropTable(
                name: "RefundDecision");

            migrationBuilder.DropTable(
                name: "RefundRoleAuthority");

            migrationBuilder.DropTable(
                name: "RefundRequest");

            migrationBuilder.DropTable(
                name: "RefundApprovalRule");
        }
    }
}
