using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sentencia de datos antepuesta a mano (ADR-0007 §5, architecture.md §3 M2, RNF-8): los tokens en claro no
            // pueden convertirse a su hash y una token_hash NOT NULL única no cabe sobre filas existentes. Los usuarios
            // vuelven a iniciar sesión. Down también vacía la tabla.
            migrationBuilder.Sql("DELETE FROM refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Misma sentencia de datos (ADR-0007 §5): un hash no se convierte de vuelta al token y una token NOT NULL
            // única no cabe sobre filas existentes (db init --reset baja con MigrateAsync("0"), R-18).
            migrationBuilder.Sql("DELETE FROM refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);
        }
    }
}
