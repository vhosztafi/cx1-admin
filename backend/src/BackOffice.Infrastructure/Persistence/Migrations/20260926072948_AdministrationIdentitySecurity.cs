using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdministrationIdentitySecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpStep",
                table: "UserCredential",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdentityAction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SecretCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityAction", x => x.Id);
                    table.CheckConstraint("CK_IdentityAction_Attempts", "[Attempts] BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_IdentityAction_ConsumedAt_Utc", "DATEPART(TZOFFSET,[ConsumedAt]) = 0");
                    table.CheckConstraint("CK_IdentityAction_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IdentityAction_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_IdentityAction_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_IdentityAction_Kind", "[Kind] IN ('invitation','password-reset','mfa-enrolment','mfa-login','mfa-recovery')");
                    table.CheckConstraint("CK_IdentityAction_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_IdentityAction_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IdentityAction_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityAction_CreatedBy",
                table: "IdentityAction",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityAction_TokenHash",
                table: "IdentityAction",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityAction_UserId_Kind",
                table: "IdentityAction",
                columns: new[] { "UserId", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentityAction");

            migrationBuilder.DropColumn(
                name: "LastTotpStep",
                table: "UserCredential");
        }
    }
}
