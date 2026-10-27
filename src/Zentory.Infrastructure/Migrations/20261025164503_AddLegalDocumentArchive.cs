using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zentory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_document_archive",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    issuer_tax_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issuer_legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dian_cufe = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    pdf_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_document_archive", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_archive_retain_until",
                schema: "core",
                table: "legal_document_archive",
                column: "retain_until");

            migrationBuilder.CreateIndex(
                name: "ux_legal_document_archive_source",
                schema: "core",
                table: "legal_document_archive",
                columns: new[] { "source_type", "source_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legal_document_archive",
                schema: "core");
        }
    }
}
