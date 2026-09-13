using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdapterAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Request = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Response = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterAttempt", x => x.Id);
                    table.CheckConstraint("CK_AdapterAttempt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AdapterAttempt_EndedAt_Utc", "DATEPART(TZOFFSET,[EndedAt]) = 0");
                    table.CheckConstraint("CK_AdapterAttempt_Number", "[AttemptNumber] > 0");
                    table.CheckConstraint("CK_AdapterAttempt_Request_Json", "ISJSON([Request]) = 1");
                    table.CheckConstraint("CK_AdapterAttempt_Response_Json", "[Response] IS NULL OR ISJSON([Response]) = 1");
                    table.CheckConstraint("CK_AdapterAttempt_StartedAt_Utc", "DATEPART(TZOFFSET,[StartedAt]) = 0");
                    table.CheckConstraint("CK_AdapterAttempt_Times", "[EndedAt] IS NULL OR [EndedAt] >= [StartedAt]");
                    table.CheckConstraint("CK_AdapterAttempt_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "AdapterInbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QuarantineReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterInbox", x => x.Id);
                    table.CheckConstraint("CK_AdapterInbox_AppliedAt_Utc", "DATEPART(TZOFFSET,[AppliedAt]) = 0");
                    table.CheckConstraint("CK_AdapterInbox_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AdapterInbox_State", "[State] IN ('received','applied','quarantined')");
                    table.CheckConstraint("CK_AdapterInbox_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "AuditEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvent", x => x.Id);
                    table.CheckConstraint("CK_AuditEvent_After_Json", "[After] IS NULL OR ISJSON([After]) = 1");
                    table.CheckConstraint("CK_AuditEvent_Before_Json", "[Before] IS NULL OR ISJSON([Before]) = 1");
                    table.CheckConstraint("CK_AuditEvent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AuditEvent_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "CapacityProvider",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityProvider", x => x.Id);
                    table.CheckConstraint("CK_CapacityProvider_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CapacityProvider_State", "[State] IN ('active','inactive')");
                    table.CheckConstraint("CK_CapacityProvider_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "DemoClock",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FrozenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoClock", x => x.Id);
                    table.CheckConstraint("CK_DemoClock_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_DemoClock_FrozenAt_Utc", "DATEPART(TZOFFSET,[FrozenAt]) = 0");
                    table.CheckConstraint("CK_DemoClock_Singleton", "[Name] = 'demo'");
                    table.CheckConstraint("CK_DemoClock_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "DemoProviderOperation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoProviderOperation", x => x.Id);
                    table.CheckConstraint("CK_DemoProviderOperation_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_DemoProviderOperation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_DemoProviderOperation_Result_Json", "[Result] IS NULL OR ISJSON([Result]) = 1");
                    table.CheckConstraint("CK_DemoProviderOperation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorScope = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ResultStatus = table.Column<int>(type: "int", nullable: false),
                    ResultBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecord", x => x.Id);
                    table.CheckConstraint("CK_IdempotencyRecord_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IdempotencyRecord_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_IdempotencyRecord_ResultBody_Json", "ISJSON([ResultBody]) = 1");
                    table.CheckConstraint("CK_IdempotencyRecord_Status", "[ResultStatus] BETWEEN 100 AND 599");
                });

            migrationBuilder.CreateTable(
                name: "OutboxWork",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SubjectRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxWork", x => x.Id);
                    table.CheckConstraint("CK_OutboxWork_Attempts", "[Attempts] >= 0");
                    table.CheckConstraint("CK_OutboxWork_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OutboxWork_LeaseExpiresAt_Utc", "DATEPART(TZOFFSET,[LeaseExpiresAt]) = 0");
                    table.CheckConstraint("CK_OutboxWork_NextAttemptAt_Utc", "DATEPART(TZOFFSET,[NextAttemptAt]) = 0");
                    table.CheckConstraint("CK_OutboxWork_Payload_Json", "ISJSON([Payload]) = 1");
                    table.CheckConstraint("CK_OutboxWork_Result_Json", "[Result] IS NULL OR ISJSON([Result]) = 1");
                    table.CheckConstraint("CK_OutboxWork_State", "[State] IN ('pending','leased','succeeded','failed')");
                    table.CheckConstraint("CK_OutboxWork_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                    table.CheckConstraint("CK_Product_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Product_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "ProductVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JsonSchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuestionSetVersion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVersion", x => x.Id);
                    table.CheckConstraint("CK_ProductVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ProductVersion_Definition_Json", "ISJSON([Definition]) = 1");
                    table.CheckConstraint("CK_ProductVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_ProductVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_ProductVersion_State", "[State] IN ('draft','published','retired')");
                    table.CheckConstraint("CK_ProductVersion_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.CheckConstraint("CK_ProductVersion_Validity", "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
                    table.CheckConstraint("CK_ProductVersion_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_ProductVersion_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductVersion_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                    table.CheckConstraint("CK_Role_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Role_Scope", "[Scope] IN ('internal','agency')");
                    table.CheckConstraint("CK_Role_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "Session",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeviceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.Id);
                    table.CheckConstraint("CK_Session_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Session_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_Session_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_Session_LastSeenAt_Utc", "DATEPART(TZOFFSET,[LastSeenAt]) = 0");
                    table.CheckConstraint("CK_Session_RevokedAt_Utc", "DATEPART(TZOFFSET,[RevokedAt]) = 0");
                    table.CheckConstraint("CK_Session_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "SettingVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Values = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingVersion", x => x.Id);
                    table.CheckConstraint("CK_SettingVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_SettingVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_SettingVersion_Values_Json", "ISJSON([Values]) = 1");
                    table.CheckConstraint("CK_SettingVersion_Version", "[Version] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Team",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.Id);
                    table.CheckConstraint("CK_Team_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Team_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                    table.CheckConstraint("CK_User_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_User_State", "[State] IN ('invited','active','suspended')");
                    table.CheckConstraint("CK_User_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_User_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_User_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserCredential",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderSubject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MfaSecretCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    MustReset = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCredential", x => x.Id);
                    table.CheckConstraint("CK_UserCredential_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UserCredential_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_UserCredential_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserCredential_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRole", x => x.Id);
                    table.CheckConstraint("CK_UserRole_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UserRole_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_UserRole_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRole_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRole_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdapterAttempt_CreatedBy",
                table: "AdapterAttempt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AdapterAttempt_WorkId_AttemptNumber",
                table: "AdapterAttempt",
                columns: new[] { "WorkId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdapterInbox_CreatedBy",
                table: "AdapterInbox",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AdapterInbox_Provider_EventId",
                table: "AdapterInbox",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdapterInbox_WorkId",
                table: "AdapterInbox",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_ActorId_OccurredAt",
                table: "AuditEvent",
                columns: new[] { "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_CreatedBy",
                table: "AuditEvent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_SubjectRecordId_OccurredAt",
                table: "AuditEvent",
                columns: new[] { "SubjectRecordId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityProvider_Code",
                table: "CapacityProvider",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityProvider_CreatedBy",
                table: "CapacityProvider",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DemoClock_CreatedBy",
                table: "DemoClock",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DemoClock_Name",
                table: "DemoClock",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoProviderOperation_CreatedBy",
                table: "DemoProviderOperation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DemoProviderOperation_Kind_OperationKey",
                table: "DemoProviderOperation",
                columns: new[] { "Kind", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemoProviderOperation_ScenarioVersionId",
                table: "DemoProviderOperation",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecord_ActorScope_Route_Key",
                table: "IdempotencyRecord",
                columns: new[] { "ActorScope", "Route", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecord_CreatedBy",
                table: "IdempotencyRecord",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxWork_CreatedBy",
                table: "OutboxWork",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxWork_Kind_OperationKey",
                table: "OutboxWork",
                columns: new[] { "Kind", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxWork_State_NextAttemptAt",
                table: "OutboxWork",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Code",
                table: "Product",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_CreatedBy",
                table: "Product",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVersion_CreatedBy",
                table: "ProductVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVersion_ProductId_Version",
                table: "ProductVersion",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVersion_ProviderId",
                table: "ProductVersion",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Role_Code",
                table: "Role",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Role_CreatedBy",
                table: "Role",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Session_CreatedBy",
                table: "Session",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Session_TokenHash",
                table: "Session",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Session_UserId_ExpiresAt",
                table: "Session",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SettingVersion_CreatedBy",
                table: "SettingVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SettingVersion_Scope_Version",
                table: "SettingVersion",
                columns: new[] { "Scope", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Team_CreatedBy",
                table: "Team",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Team_Name",
                table: "Team",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_CreatedBy",
                table: "User",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_User_NormalizedEmail",
                table: "User",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_TeamId",
                table: "User",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCredential_CreatedBy",
                table: "UserCredential",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserCredential_Provider_ProviderSubject",
                table: "UserCredential",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCredential_UserId_Provider",
                table: "UserCredential",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_CreatedBy",
                table: "UserRole",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_RoleId",
                table: "UserRole",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_UserId_RoleId",
                table: "UserRole",
                columns: new[] { "UserId", "RoleId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AdapterAttempt_OutboxWork_WorkId",
                table: "AdapterAttempt",
                column: "WorkId",
                principalTable: "OutboxWork",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AdapterAttempt_User_CreatedBy",
                table: "AdapterAttempt",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AdapterInbox_OutboxWork_WorkId",
                table: "AdapterInbox",
                column: "WorkId",
                principalTable: "OutboxWork",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AdapterInbox_User_CreatedBy",
                table: "AdapterInbox",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEvent_User_ActorId",
                table: "AuditEvent",
                column: "ActorId",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditEvent_User_CreatedBy",
                table: "AuditEvent",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CapacityProvider_User_CreatedBy",
                table: "CapacityProvider",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DemoClock_User_CreatedBy",
                table: "DemoClock",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DemoProviderOperation_SettingVersion_ScenarioVersionId",
                table: "DemoProviderOperation",
                column: "ScenarioVersionId",
                principalTable: "SettingVersion",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DemoProviderOperation_User_CreatedBy",
                table: "DemoProviderOperation",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyRecord_User_CreatedBy",
                table: "IdempotencyRecord",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OutboxWork_User_CreatedBy",
                table: "OutboxWork",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Product_User_CreatedBy",
                table: "Product",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVersion_User_CreatedBy",
                table: "ProductVersion",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Role_User_CreatedBy",
                table: "Role",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Session_User_CreatedBy",
                table: "Session",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Session_User_UserId",
                table: "Session",
                column: "UserId",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SettingVersion_User_CreatedBy",
                table: "SettingVersion",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Team_User_CreatedBy",
                table: "Team",
                column: "CreatedBy",
                principalTable: "User",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Team_User_CreatedBy",
                table: "Team");

            migrationBuilder.DropTable(
                name: "AdapterAttempt");

            migrationBuilder.DropTable(
                name: "AdapterInbox");

            migrationBuilder.DropTable(
                name: "AuditEvent");

            migrationBuilder.DropTable(
                name: "DemoClock");

            migrationBuilder.DropTable(
                name: "DemoProviderOperation");

            migrationBuilder.DropTable(
                name: "IdempotencyRecord");

            migrationBuilder.DropTable(
                name: "ProductVersion");

            migrationBuilder.DropTable(
                name: "Session");

            migrationBuilder.DropTable(
                name: "UserCredential");

            migrationBuilder.DropTable(
                name: "UserRole");

            migrationBuilder.DropTable(
                name: "OutboxWork");

            migrationBuilder.DropTable(
                name: "SettingVersion");

            migrationBuilder.DropTable(
                name: "CapacityProvider");

            migrationBuilder.DropTable(
                name: "Product");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "Team");
        }
    }
}
