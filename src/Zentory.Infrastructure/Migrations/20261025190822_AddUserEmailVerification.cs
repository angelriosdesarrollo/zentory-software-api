using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "email_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Sentencia de datos antepuesta a mano (ADR-0007 §5, architecture.md §3 M1): normaliza los emails existentes
            // antes de crear el check. Sin inverso en Down.
            migrationBuilder.Sql("UPDATE users SET email = lower(btrim(email))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_email_normalized",
                table: "users",
                sql: "email = lower(btrim(email))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_email_normalized",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "users");
        }
    }
}
