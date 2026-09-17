using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirstPolicyIssueStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.CreateSequence(
                name: "PolicyReferenceSequence",
                maxValue: 9999999999L);

            migrationBuilder.AddColumn<Guid>(
                name: "BoundPolicyId",
                table: "Quote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IssueFinancialComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    CoverageStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoverageEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueFinancialComponent", x => x.Id);
                    table.UniqueConstraint("AK_IssueFinancialComponent_Id_TransactionId_Code", x => new { x.Id, x.TransactionId, x.Code });
                    table.CheckConstraint("CK_IssueFinancialComponent_Amount", "[Amount]>=0");
                    table.CheckConstraint("CK_IssueFinancialComponent_Code", "[Code] IN ('premium','tax','fee','commission','fee-share')");
                    table.CheckConstraint("CK_IssueFinancialComponent_CoverageEndsAt_Utc", "DATEPART(TZOFFSET,[CoverageEndsAt]) = 0");
                    table.CheckConstraint("CK_IssueFinancialComponent_CoverageStartsAt_Utc", "DATEPART(TZOFFSET,[CoverageStartsAt]) = 0");
                    table.CheckConstraint("CK_IssueFinancialComponent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IssueFinancialComponent_Interval", "[CoverageStartsAt]<[CoverageEndsAt]");
                    table.ForeignKey(
                        name: "FK_IssueFinancialComponent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IssueFinancialObligation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DebtorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DebtorAgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebtorRelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Settlement = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Premium = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    FeeShare = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    GrossDue = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    InvoiceDue = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    NetDue = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    BrokerPayable = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    TermsSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueFinancialObligation", x => x.Id);
                    table.UniqueConstraint("AK_IssueFinancialObligation_Id_TransactionId", x => new { x.Id, x.TransactionId });
                    table.CheckConstraint("CK_IssueFinancialObligation_Amounts", "[Commission]<=[Premium] AND [FeeShare]<=[Fee] AND [GrossDue]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossDue]-[Commission]-[FeeShare]");
                    table.CheckConstraint("CK_IssueFinancialObligation_BrokerPayable", "[BrokerPayable]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_Commission", "[Commission]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IssueFinancialObligation_Currency", "[Currency]='GBP'");
                    table.CheckConstraint("CK_IssueFinancialObligation_Debtor", "([DebtorKind]='agency' AND [DebtorAgencyId] IS NOT NULL AND [DebtorAgencyId]=[AgencyId] AND [DebtorRelationshipId] IS NULL) OR ([DebtorKind]='relationship' AND [DebtorRelationshipId] IS NOT NULL AND [DebtorRelationshipId]=[RelationshipId] AND [DebtorAgencyId] IS NULL)");
                    table.CheckConstraint("CK_IssueFinancialObligation_Fee", "[Fee]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_FeeShare", "[FeeShare]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_GrossDue", "[GrossDue]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_InvoiceDue", "[InvoiceDue]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_NetDue", "[NetDue]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_Premium", "[Premium]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_Purpose", "[Purpose]='first-issue'");
                    table.CheckConstraint("CK_IssueFinancialObligation_Settlement", "([Settlement]='net-remittance' AND [DebtorKind]='agency' AND [InvoiceDue]=[NetDue] AND [BrokerPayable]=0) OR ([Settlement]='separate-payment' AND [InvoiceDue]=[GrossDue] AND [BrokerPayable]=[Commission]+[FeeShare])");
                    table.CheckConstraint("CK_IssueFinancialObligation_Tax", "[Tax]>=0");
                    table.CheckConstraint("CK_IssueFinancialObligation_TermsSnapshotJson", "ISJSON([TermsSnapshotJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[TermsSnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_AgencyTermsVersion_AgencyTermsVersionId_AgencyId",
                        columns: x => new { x.AgencyTermsVersionId, x.AgencyId },
                        principalTable: "AgencyTermsVersion",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_Agency_DebtorAgencyId",
                        column: x => x.DebtorAgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_ClientAgencyRelationship_DebtorRelationshipId",
                        column: x => x.DebtorRelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_ClientAgencyRelationship_RelationshipId_ClientId_AgencyId",
                        columns: x => new { x.RelationshipId, x.ClientId, x.AgencyId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_IssueFinancialObligation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Journal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journal", x => x.Id);
                    table.UniqueConstraint("AK_Journal_Id_TransactionId", x => new { x.Id, x.TransactionId });
                    table.CheckConstraint("CK_Journal_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Journal_Currency", "[Currency]='GBP'");
                    table.CheckConstraint("CK_Journal_PostedAt", "[PostedAt] IS NULL OR [PostedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_Journal_PostedAt_Utc", "DATEPART(TZOFFSET,[PostedAt]) = 0");
                    table.CheckConstraint("CK_Journal_Purpose", "[Purpose]='first-issue'");
                    table.ForeignKey(
                        name: "FK_Journal_IssueFinancialObligation_ObligationId_TransactionId",
                        columns: x => new { x.ObligationId, x.TransactionId },
                        principalTable: "IssueFinancialObligation",
                        principalColumns: new[] { "Id", "TransactionId" });
                    table.ForeignKey(
                        name: "FK_Journal_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PartyKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    CoverageStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoverageEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLine", x => x.Id);
                    table.CheckConstraint("CK_JournalLine_Account", "[AccountCode] IN ('agency-receivable','relationship-receivable','insurer-payable','fee-income','broker-remuneration-payable')");
                    table.CheckConstraint("CK_JournalLine_Amount", "([Debit]>0 AND [Credit]=0) OR ([Credit]>0 AND [Debit]=0)");
                    table.CheckConstraint("CK_JournalLine_CoverageEndsAt_Utc", "DATEPART(TZOFFSET,[CoverageEndsAt]) = 0");
                    table.CheckConstraint("CK_JournalLine_CoverageStartsAt_Utc", "DATEPART(TZOFFSET,[CoverageStartsAt]) = 0");
                    table.CheckConstraint("CK_JournalLine_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_JournalLine_Interval", "[CoverageStartsAt]<[CoverageEndsAt]");
                    table.CheckConstraint("CK_JournalLine_Party", "([AccountCode]='fee-income' AND [PartyKind]='internal' AND [PartyId] IS NULL) OR ([AccountCode] IN ('agency-receivable','broker-remuneration-payable') AND [PartyKind]='agency' AND [PartyId] IS NOT NULL) OR ([AccountCode]='relationship-receivable' AND [PartyKind]='relationship' AND [PartyId] IS NOT NULL) OR ([AccountCode]='insurer-payable' AND [PartyKind]='provider' AND [PartyId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_JournalLine_IssueFinancialComponent_SourceComponentId_TransactionId_ComponentCode",
                        columns: x => new { x.SourceComponentId, x.TransactionId, x.ComponentCode },
                        principalTable: "IssueFinancialComponent",
                        principalColumns: new[] { "Id", "TransactionId", "Code" });
                    table.ForeignKey(
                        name: "FK_JournalLine_Journal_JournalId_TransactionId",
                        columns: x => new { x.JournalId, x.TransactionId },
                        principalTable: "Journal",
                        principalColumns: new[] { "Id", "TransactionId" });
                    table.ForeignKey(
                        name: "FK_JournalLine_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "NEXT VALUE FOR [PolicyReferenceSequence]"),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, computedColumnSql: "'PL-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)", stored: true),
                    SourceQuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policy", x => x.Id);
                    table.UniqueConstraint("AK_Policy_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.UniqueConstraint("AK_Policy_Id_SourceQuoteId", x => new { x.Id, x.SourceQuoteId });
                    table.CheckConstraint("CK_Policy_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Policy_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_Policy_Number", "[Number] BETWEEN 1 AND 9999999999");
                    table.CheckConstraint("CK_Policy_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Policy_ClientAgencyRelationship_RelationshipId_ClientId_AgencyId",
                        columns: x => new { x.RelationshipId, x.ClientId, x.AgencyId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_Policy_Quote_SourceQuoteId_AgencyId_ProductId",
                        columns: x => new { x.SourceQuoteId, x.AgencyId, x.ProductId },
                        principalTable: "Quote",
                        principalColumns: new[] { "Id", "AgencyId", "ProductId" });
                    table.ForeignKey(
                        name: "FK_Policy_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PolicyDocumentRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyDocumentRequest", x => x.Id);
                    table.CheckConstraint("CK_PolicyDocumentRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyDocumentRequest_Hash", "[PayloadHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.CheckConstraint("CK_PolicyDocumentRequest_Kind", "[Kind] IN ('policy-schedule','policy-certificate','policy-statement')");
                    table.CheckConstraint("CK_PolicyDocumentRequest_PayloadJson", "ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_PolicyDocumentRequest_Purpose", "[Purpose]='first-issue'");
                    table.CheckConstraint("CK_PolicyDocumentRequest_State", "[State]='requested'");
                    table.CheckConstraint("CK_PolicyDocumentRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_PolicyDocumentRequest_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyDocumentRequest_TemplateVersion_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "TemplateVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyDocumentRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PolicyRegistration",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedRegistration = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyRegistration", x => new { x.VersionId, x.RiskItemId });
                    table.CheckConstraint("CK_PolicyRegistration_Registration", "LEN([NormalizedRegistration]) BETWEEN 2 AND 12 AND [NormalizedRegistration] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_PolicyRegistration_RiskItem", "[RiskItemId]<>'00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "PolicyTerm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LocalTermIntentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyTerm", x => x.Id);
                    table.UniqueConstraint("AK_PolicyTerm_Id_PolicyId", x => new { x.Id, x.PolicyId });
                    table.CheckConstraint("CK_PolicyTerm_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyTerm_EndsAt_Utc", "DATEPART(TZOFFSET,[EndsAt]) = 0");
                    table.CheckConstraint("CK_PolicyTerm_Interval", "[StartsAt]<[EndsAt]");
                    table.CheckConstraint("CK_PolicyTerm_LocalTermIntentJson", "ISJSON([LocalTermIntentJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[LocalTermIntentJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_PolicyTerm_Number", "[Number]>0");
                    table.CheckConstraint("CK_PolicyTerm_StartsAt_Utc", "DATEPART(TZOFFSET,[StartsAt]) = 0");
                    table.CheckConstraint("CK_PolicyTerm_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_PolicyTerm_Policy_PolicyId_ProductId",
                        columns: x => new { x.PolicyId, x.ProductId },
                        principalTable: "Policy",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_PolicyTerm_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_PolicyTerm_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PolicyTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceQuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyTransaction", x => x.Id);
                    table.UniqueConstraint("AK_PolicyTransaction_Id_TermId_PolicyId", x => new { x.Id, x.TermId, x.PolicyId });
                    table.CheckConstraint("CK_PolicyTransaction_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyTransaction_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_PolicyTransaction_Kind", "[Kind]='new-business'");
                    table.CheckConstraint("CK_PolicyTransaction_ProcessedAt_Utc", "DATEPART(TZOFFSET,[ProcessedAt]) = 0");
                    table.CheckConstraint("CK_PolicyTransaction_Provenance", "[CreatedBy] IS NOT NULL AND [ProcessedAt]=[CreatedAt] AND LEN(TRIM([Reason]))>0 AND LEN(TRIM([OperationKey]))>0");
                    table.CheckConstraint("CK_PolicyTransaction_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_PolicyTerm_TermId_PolicyId",
                        columns: x => new { x.TermId, x.PolicyId },
                        principalTable: "PolicyTerm",
                        principalColumns: new[] { "Id", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_Policy_PolicyId_SourceQuoteId",
                        columns: x => new { x.PolicyId, x.SourceQuoteId },
                        principalTable: "Policy",
                        principalColumns: new[] { "Id", "SourceQuoteId" });
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_QuoteAcceptance_AcceptanceId_CycleId_SourceQuoteId",
                        columns: x => new { x.AcceptanceId, x.CycleId, x.SourceQuoteId },
                        principalTable: "QuoteAcceptance",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_QuoteRatingResult_RatingId_CycleId_SourceQuoteId",
                        columns: x => new { x.RatingId, x.CycleId, x.SourceQuoteId },
                        principalTable: "QuoteRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_QuoteRevision_QuoteRevisionId_SourceQuoteId",
                        columns: x => new { x.QuoteRevisionId, x.SourceQuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_PolicyTransaction_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PolicyVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    SliceOrdinal = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyVersion", x => x.Id);
                    table.UniqueConstraint("AK_PolicyVersion_Id_PolicyId", x => new { x.Id, x.PolicyId });
                    table.UniqueConstraint("AK_PolicyVersion_Id_TermId_PolicyId", x => new { x.Id, x.TermId, x.PolicyId });
                    table.UniqueConstraint("AK_PolicyVersion_Id_TransactionId_TermId_PolicyId", x => new { x.Id, x.TransactionId, x.TermId, x.PolicyId });
                    table.CheckConstraint("CK_PolicyVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyVersion_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_PolicyVersion_Hash", "[ContentHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.CheckConstraint("CK_PolicyVersion_ProcessedAt_Utc", "DATEPART(TZOFFSET,[ProcessedAt]) = 0");
                    table.CheckConstraint("CK_PolicyVersion_Schema", "[SchemaVersion]='1.0' AND COALESCE(JSON_VALUE([SnapshotJson],'$.schemaVersion'),'')=[SchemaVersion]");
                    table.CheckConstraint("CK_PolicyVersion_Sequence", "[Sequence]>0 AND [SliceOrdinal]>0");
                    table.CheckConstraint("CK_PolicyVersion_SnapshotJson", "ISJSON([SnapshotJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[SnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.ForeignKey(
                        name: "FK_PolicyVersion_PolicyTransaction_TransactionId_TermId_PolicyId",
                        columns: x => new { x.TransactionId, x.TermId, x.PolicyId },
                        principalTable: "PolicyTransaction",
                        principalColumns: new[] { "Id", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_PolicyVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','policy-schedule','policy-certificate','policy-statement')");

            migrationBuilder.CreateIndex(
                name: "IX_Quote_BoundPolicyId_Id",
                table: "Quote",
                columns: new[] { "BoundPolicyId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Quote_BoundPolicy",
                table: "Quote",
                sql: "([State]='bound' AND [BoundPolicyId] IS NOT NULL) OR ([State]<>'bound' AND [BoundPolicyId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_CreatedBy",
                table: "IssueFinancialComponent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_ObligationId_Code",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialComponent_ObligationId_TransactionId",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_AgencyTermsVersionId_AgencyId",
                table: "IssueFinancialObligation",
                columns: new[] { "AgencyTermsVersionId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_CreatedBy",
                table: "IssueFinancialObligation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_DebtorAgencyId",
                table: "IssueFinancialObligation",
                column: "DebtorAgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_DebtorRelationshipId",
                table: "IssueFinancialObligation",
                column: "DebtorRelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_ProviderId",
                table: "IssueFinancialObligation",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_RelationshipId_ClientId_AgencyId",
                table: "IssueFinancialObligation",
                columns: new[] { "RelationshipId", "ClientId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_TransactionId_Purpose",
                table: "IssueFinancialObligation",
                columns: new[] { "TransactionId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueFinancialObligation_TransactionId_TermId_PolicyId",
                table: "IssueFinancialObligation",
                columns: new[] { "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Journal_CreatedBy",
                table: "Journal",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Journal_ObligationId_TransactionId",
                table: "Journal",
                columns: new[] { "ObligationId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Journal_TransactionId_Purpose",
                table: "Journal",
                columns: new[] { "TransactionId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_CreatedBy",
                table: "JournalLine",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_JournalId_SourceComponentId_AccountCode",
                table: "JournalLine",
                columns: new[] { "JournalId", "SourceComponentId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_JournalId_TransactionId",
                table: "JournalLine",
                columns: new[] { "JournalId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLine_SourceComponentId_TransactionId_ComponentCode",
                table: "JournalLine",
                columns: new[] { "SourceComponentId", "TransactionId", "ComponentCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Policy_AgencyId_CreatedAt_Id",
                table: "Policy",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Policy_ClientId_CreatedAt_Id",
                table: "Policy",
                columns: new[] { "ClientId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Policy_CreatedBy",
                table: "Policy",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Policy_CurrentTermId_Id",
                table: "Policy",
                columns: new[] { "CurrentTermId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Policy_Number",
                table: "Policy",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policy_Reference",
                table: "Policy",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policy_RelationshipId_ClientId_AgencyId",
                table: "Policy",
                columns: new[] { "RelationshipId", "ClientId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Policy_SourceQuoteId",
                table: "Policy",
                column: "SourceQuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Policy_SourceQuoteId_AgencyId_ProductId",
                table: "Policy",
                columns: new[] { "SourceQuoteId", "AgencyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocumentRequest_CreatedBy",
                table: "PolicyDocumentRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocumentRequest_TemplateVersionId",
                table: "PolicyDocumentRequest",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocumentRequest_VersionId_Kind_TemplateVersionId_Purpose",
                table: "PolicyDocumentRequest",
                columns: new[] { "VersionId", "Kind", "TemplateVersionId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocumentRequest_VersionId_TransactionId_TermId_PolicyId",
                table: "PolicyDocumentRequest",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocumentRequest_WorkId",
                table: "PolicyDocumentRequest",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRegistration_NormalizedRegistration_PolicyId",
                table: "PolicyRegistration",
                columns: new[] { "NormalizedRegistration", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRegistration_VersionId_PolicyId",
                table: "PolicyRegistration",
                columns: new[] { "VersionId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTerm_CreatedBy",
                table: "PolicyTerm",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTerm_CurrentVersionId_Id_PolicyId",
                table: "PolicyTerm",
                columns: new[] { "CurrentVersionId", "Id", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTerm_PolicyId_Number",
                table: "PolicyTerm",
                columns: new[] { "PolicyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTerm_PolicyId_ProductId",
                table: "PolicyTerm",
                columns: new[] { "PolicyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTerm_ProductVersionId_ProductId",
                table: "PolicyTerm",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_AcceptanceId_CycleId_SourceQuoteId",
                table: "PolicyTransaction",
                columns: new[] { "AcceptanceId", "CycleId", "SourceQuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_CreatedBy",
                table: "PolicyTransaction",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_OperationKey",
                table: "PolicyTransaction",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_PolicyId",
                table: "PolicyTransaction",
                column: "PolicyId",
                unique: true,
                filter: "[Kind]='new-business'");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_PolicyId_SourceQuoteId",
                table: "PolicyTransaction",
                columns: new[] { "PolicyId", "SourceQuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_QuoteRevisionId_SourceQuoteId",
                table: "PolicyTransaction",
                columns: new[] { "QuoteRevisionId", "SourceQuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_RatingId_CycleId_SourceQuoteId",
                table: "PolicyTransaction",
                columns: new[] { "RatingId", "CycleId", "SourceQuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_TermId_PolicyId",
                table: "PolicyTransaction",
                columns: new[] { "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyTransaction_TermId_Sequence",
                table: "PolicyTransaction",
                columns: new[] { "TermId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersion_CreatedBy",
                table: "PolicyVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersion_TermId_Sequence",
                table: "PolicyVersion",
                columns: new[] { "TermId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersion_TransactionId_SliceOrdinal",
                table: "PolicyVersion",
                columns: new[] { "TransactionId", "SliceOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersion_TransactionId_TermId_PolicyId",
                table: "PolicyVersion",
                columns: new[] { "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Quote_Policy_BoundPolicyId_Id",
                table: "Quote",
                columns: new[] { "BoundPolicyId", "Id" },
                principalTable: "Policy",
                principalColumns: new[] { "Id", "SourceQuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_IssueFinancialComponent_IssueFinancialObligation_ObligationId_TransactionId",
                table: "IssueFinancialComponent",
                columns: new[] { "ObligationId", "TransactionId" },
                principalTable: "IssueFinancialObligation",
                principalColumns: new[] { "Id", "TransactionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_IssueFinancialObligation_PolicyTransaction_TransactionId_TermId_PolicyId",
                table: "IssueFinancialObligation",
                columns: new[] { "TransactionId", "TermId", "PolicyId" },
                principalTable: "PolicyTransaction",
                principalColumns: new[] { "Id", "TermId", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Policy_PolicyTerm_CurrentTermId_Id",
                table: "Policy",
                columns: new[] { "CurrentTermId", "Id" },
                principalTable: "PolicyTerm",
                principalColumns: new[] { "Id", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyDocumentRequest_PolicyVersion_VersionId_TransactionId_TermId_PolicyId",
                table: "PolicyDocumentRequest",
                columns: new[] { "VersionId", "TransactionId", "TermId", "PolicyId" },
                principalTable: "PolicyVersion",
                principalColumns: new[] { "Id", "TransactionId", "TermId", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyRegistration_PolicyVersion_VersionId_PolicyId",
                table: "PolicyRegistration",
                columns: new[] { "VersionId", "PolicyId" },
                principalTable: "PolicyVersion",
                principalColumns: new[] { "Id", "PolicyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyTerm_PolicyVersion_CurrentVersionId_Id_PolicyId",
                table: "PolicyTerm",
                columns: new[] { "CurrentVersionId", "Id", "PolicyId" },
                principalTable: "PolicyVersion",
                principalColumns: new[] { "Id", "TermId", "PolicyId" });
            AddPolicyGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropPolicyGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_Quote_Policy_BoundPolicyId_Id",
                table: "Quote");

            migrationBuilder.DropForeignKey(
                name: "FK_PolicyVersion_PolicyTransaction_TransactionId_TermId_PolicyId",
                table: "PolicyVersion");

            migrationBuilder.DropForeignKey(
                name: "FK_Policy_PolicyTerm_CurrentTermId_Id",
                table: "Policy");

            migrationBuilder.DropTable(
                name: "JournalLine");

            migrationBuilder.DropTable(
                name: "PolicyDocumentRequest");

            migrationBuilder.DropTable(
                name: "PolicyRegistration");

            migrationBuilder.DropTable(
                name: "IssueFinancialComponent");

            migrationBuilder.DropTable(
                name: "Journal");

            migrationBuilder.DropTable(
                name: "IssueFinancialObligation");

            migrationBuilder.DropTable(
                name: "PolicyTransaction");

            migrationBuilder.DropTable(
                name: "PolicyTerm");

            migrationBuilder.DropTable(
                name: "PolicyVersion");

            migrationBuilder.DropTable(
                name: "Policy");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.DropIndex(
                name: "IX_Quote_BoundPolicyId_Id",
                table: "Quote");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Quote_BoundPolicy",
                table: "Quote");

            migrationBuilder.DropColumn(
                name: "BoundPolicyId",
                table: "Quote");

            migrationBuilder.DropSequence(
                name: "PolicyReferenceSequence");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind]='quote-terms'");
        }
    }
}
