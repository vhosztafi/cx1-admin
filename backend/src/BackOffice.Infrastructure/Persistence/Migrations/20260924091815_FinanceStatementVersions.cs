using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceStatementVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AgencyPermissionRequest_Permission",
                table: "AgencyPermissionRequest");

            migrationBuilder.CreateTable(
                name: "FinanceStatementVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceCutoff = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Opening = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Debits = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Credits = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Closing = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceStatementVersion", x => x.Id);
                    table.CheckConstraint("CK_FinanceStatementVersion_Amounts", "TRY_CONVERT(decimal(15,2),[Opening])+TRY_CONVERT(decimal(15,2),[Debits])-TRY_CONVERT(decimal(15,2),[Credits])=TRY_CONVERT(decimal(15,2),[Closing]) AND TRY_CONVERT(decimal(15,2),[Debits])>=0 AND TRY_CONVERT(decimal(15,2),[Credits])>=0");
                    table.CheckConstraint("CK_FinanceStatementVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceStatementVersion_Hash", "[ContentHash]=HASHBYTES('SHA2_256',[ContentBytes])");
                    table.CheckConstraint("CK_FinanceStatementVersion_SnapshotJson_Json", "ISJSON([SnapshotJson]) = 1");
                    table.CheckConstraint("CK_FinanceStatementVersion_SourceCutoff_Utc", "DATEPART(TZOFFSET,[SourceCutoff]) = 0");
                    table.CheckConstraint("CK_FinanceStatementVersion_SourceIdsJson_Json", "ISJSON([SourceIdsJson]) = 1");
                    table.CheckConstraint("CK_FinanceStatementVersion_Window", "[From]<[To] AND [Version]>0 AND [SourceCutoff]>=[CreatedAt]");
                    table.ForeignKey(
                        name: "FK_FinanceStatementVersion_AgencyTermsVersion_AgencyTermsVersionId_AgencyId",
                        columns: x => new { x.AgencyTermsVersionId, x.AgencyId },
                        principalTable: "AgencyTermsVersion",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_FinanceStatementVersion_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceStatementVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgencyPermissionRequest_Permission",
                table: "AgencyPermissionRequest",
                sql: "([Permission] COLLATE Latin1_General_100_BIN2 = 'bordereau-download' AND DATALENGTH([Permission])=DATALENGTH(N'bordereau-download')) OR ([Permission] COLLATE Latin1_General_100_BIN2 = 'statement-download' AND DATALENGTH([Permission])=DATALENGTH(N'statement-download'))");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceStatementVersion_AgencyId_CreatedAt",
                table: "FinanceStatementVersion",
                columns: new[] { "AgencyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceStatementVersion_AgencyId_From_To_Version",
                table: "FinanceStatementVersion",
                columns: new[] { "AgencyId", "From", "To", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceStatementVersion_AgencyTermsVersionId_AgencyId",
                table: "FinanceStatementVersion",
                columns: new[] { "AgencyTermsVersionId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceStatementVersion_CreatedBy",
                table: "FinanceStatementVersion",
                column: "CreatedBy");

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceStatementVersion_Immutable ON FinanceStatementVersion AFTER UPDATE,DELETE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted)
                    THROW 51610,'A saved finance statement version is immutable.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceStatementVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgencyPermissionRequest_Permission",
                table: "AgencyPermissionRequest");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgencyPermissionRequest_Permission",
                table: "AgencyPermissionRequest",
                sql: "[Permission] COLLATE Latin1_General_100_BIN2 = 'bordereau-download' AND DATALENGTH([Permission])=DATALENGTH(N'bordereau-download')");
        }
    }
}
