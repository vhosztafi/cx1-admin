using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancellationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CancellationNoticeDispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationNoticeDispatch", x => x.Id);
                    table.CheckConstraint("CK_CancellationNoticeDispatch_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CancellationNoticeDispatch_CancellationConsequence_ConsequenceId",
                        column: x => x.ConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationNoticeDispatch_DocumentVersion_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationNoticeDispatch_OperationalDelivery_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "OperationalDelivery",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationNoticeDispatch_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationOperationalReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationOperationalReceipt", x => x.Id);
                    table.CheckConstraint("CK_CancellationOperationalReceipt_Content", "ISJSON([ResultJson])=1 AND LEN([PayloadHash])=64 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_CancellationOperationalReceipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CancellationOperationalReceipt_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CancellationOperationalReceipt_CancellationConsequence_ConsequenceId",
                        column: x => x.ConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationOperationalReceipt_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationOperationalReceipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CancellationTaskClosure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationTaskClosure", x => x.Id);
                    table.CheckConstraint("CK_CancellationTaskClosure_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CancellationTaskClosure_CancellationConsequence_ConsequenceId",
                        column: x => x.ConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationTaskClosure_OperationalTaskEvent_TaskEventId",
                        column: x => x.TaskEventId,
                        principalTable: "OperationalTaskEvent",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationTaskClosure_OperationalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OperationalTask",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CancellationTaskClosure_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CertificateWithdrawal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateKind = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateWithdrawal", x => x.Id);
                    table.CheckConstraint("CK_CertificateWithdrawal_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CertificateWithdrawal_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.ForeignKey(
                        name: "FK_CertificateWithdrawal_CancellationConsequence_ConsequenceId",
                        column: x => x.ConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CertificateWithdrawal_PolicyTerm_TermId",
                        column: x => x.TermId,
                        principalTable: "PolicyTerm",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CertificateWithdrawal_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeDispatch_ConsequenceId",
                table: "CancellationNoticeDispatch",
                column: "ConsequenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeDispatch_CreatedBy",
                table: "CancellationNoticeDispatch",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeDispatch_DeliveryId",
                table: "CancellationNoticeDispatch",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationNoticeDispatch_DocumentVersionId",
                table: "CancellationNoticeDispatch",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationOperationalReceipt_ConsequenceId",
                table: "CancellationOperationalReceipt",
                column: "ConsequenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationOperationalReceipt_CreatedBy",
                table: "CancellationOperationalReceipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationOperationalReceipt_WorkId",
                table: "CancellationOperationalReceipt",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationTaskClosure_ConsequenceId_TaskId",
                table: "CancellationTaskClosure",
                columns: new[] { "ConsequenceId", "TaskId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationTaskClosure_CreatedBy",
                table: "CancellationTaskClosure",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationTaskClosure_TaskEventId",
                table: "CancellationTaskClosure",
                column: "TaskEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CancellationTaskClosure_TaskId",
                table: "CancellationTaskClosure",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateWithdrawal_ConsequenceId",
                table: "CertificateWithdrawal",
                column: "ConsequenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateWithdrawal_CreatedBy",
                table: "CertificateWithdrawal",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateWithdrawal_TermId_CertificateKind",
                table: "CertificateWithdrawal",
                columns: new[] { "TermId", "CertificateKind" },
                unique: true);
            AddCancellationGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveCancellationGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "CancellationNoticeDispatch");

            migrationBuilder.DropTable(
                name: "CancellationOperationalReceipt");

            migrationBuilder.DropTable(
                name: "CancellationTaskClosure");

            migrationBuilder.DropTable(
                name: "CertificateWithdrawal");
        }
    }
}
