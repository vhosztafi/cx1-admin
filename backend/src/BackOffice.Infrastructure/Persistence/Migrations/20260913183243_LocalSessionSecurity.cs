using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalSessionSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                table: "UserCredential",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "UserCredential",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TicketCiphertext",
                table: "Session",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCredential_FailedAttempts",
                table: "UserCredential",
                sql: "[FailedAttempts] BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCredential_LockedUntil_Utc",
                table: "UserCredential",
                sql: "DATEPART(TZOFFSET,[LockedUntil]) = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCredential_FailedAttempts",
                table: "UserCredential");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCredential_LockedUntil_Utc",
                table: "UserCredential");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                table: "UserCredential");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "UserCredential");

            migrationBuilder.DropColumn(
                name: "TicketCiphertext",
                table: "Session");
        }
    }
}
